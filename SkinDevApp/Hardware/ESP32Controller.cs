// ============================================================================
// ESP32Controller.cs  -  namespace SkinDevApp.Hardware
//
// All communication between LUMYVUE and the ESP32 light controller, over USB serial. Nothing here touches the user interface.
// Protocol version 1 (identical to LUMYVUE_ESP32_Firmware.ino, documented in Hardware\PROTOCOL.md):
//   one command per line (\n), one answer per command. Brightness is 0..255 on the wire (the UI uses 0..100 %).
//   HELLO -> READY:LUMYVUE:<fw>:<protocol>      PING -> PONG       STATUS -> STATUS:SOFT=..;SOFTLEVEL=..;RING=..;...
//   SOFT:n  RING:n  RINGLEVEL:n  TEMP:n  -> OK:<NAME>:n         CAPTURE_START / CAPTURE_END / ALL_OFF -> OK:<NAME>
//   anything else -> ERROR:INVALID_COMMAND, a bad number -> ERROR:INVALID_BRIGHTNESS
// An "OK" answer means the controller ACCEPTED the command (it set its target). It is not a measurement: LUMYVUE cannot see the real
// brightness of the lights and never claims to.
//
// Behaviour: finds the board by itself (every non-Bluetooth COM port is asked HELLO; a port can also be chosen by hand), connects when
// LUMYVUE starts, checks the link every few seconds, and reconnects after a cable is pulled or the board restarts. Only one connection is
// ever open. Every call is asynchronous with a time-out, so the screen never freezes. DTR and RTS are kept off so opening the port does
// not reset the board (which would restart the soft-light fade-in).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkinDevApp.Hardware
{
    /// <summary>The answer to one command.</summary>
    public sealed class Esp32Response
    {
        public bool Ok { get; set; }
        public string Line { get; set; }          // the answer line, e.g. "OK:SOFT:150"
        public string Error { get; set; }         // why it failed (time-out, not connected, ERROR:INVALID_BRIGHTNESS, ...)
    }

    /// <summary>What the controller last reported (STATUS). Brightness values are the controller's settings (0..255), not measurements.</summary>
    public sealed class Esp32Status
    {
        public int Soft { get; set; }             // current soft level, 0 when the soft lights are off
        public int SoftLevel { get; set; }        // remembered soft level
        public int Ring { get; set; }             // current ring level, 0 when the ring is off
        public int RingLevel { get; set; }        // remembered ring level
        public int Temp { get; set; }             // ring colour 0 warm .. 255 cool
        public bool Capture { get; set; }
        public string Firmware { get; set; }
        public int Protocol { get; set; }
        public long UptimeSeconds { get; set; }
        public bool SoftOn => Soft > 0;
        public bool RingOn => Ring > 0;
    }

    /// <summary>A byte stream to the board. The real one is a COM port; a test can provide its own.</summary>
    public interface IEsp32Transport : IDisposable
    {
        Stream Stream { get; }
        /// <summary>False once the other end is gone (a read that returns nothing is not proof of that on a real COM port).</summary>
        bool IsAlive { get; }
    }

    public sealed class ESP32Controller : IDisposable
    {
        public const int Baud = 115200;
        public const int ProtocolVersion = 1;

        public static ESP32Controller Instance { get; } = new ESP32Controller();

        /// <summary>How a port name is opened. Replaced only by tests.</summary>
        public static Func<string, IEsp32Transport> TransportFactory = name => new SerialTransport(name);
        /// <summary>The ports tried when no port is chosen. Replaced only by tests.</summary>
        public static Func<IList<string>> PortLister = () => SkinDevApp.Printing.ThermalPrinter.PrinterPortNames();

        private readonly object _lock = new object();
        private readonly SemaphoreSlim _commandGate = new SemaphoreSlim(1, 1);      // one command at a time
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);      // one connection attempt at a time
        private IEsp32Transport _transport;
        private CancellationTokenSource _readerCts;
        private TaskCompletionSource<string> _pending;
        private string _expect;
        private CancellationTokenSource _monitorCts;
        private int _missedPolls;
        private string _lastGoodPort;
        private bool _disposed;
        private string _portProblem = "";

        /// <summary>The COM port chosen by hand ("" = find it automatically).</summary>
        public string PreferredPort { get; set; } = "";
        public string PortName { get; private set; }
        public bool IsConnected { get; private set; }
        public string LastError { get; private set; } = "";
        public Esp32Status LastStatus { get; private set; }
        public bool AutoConnect { get; set; } = true;

        /// <summary>Raised (on a background thread) when the connection or the last status changes.</summary>
        public event Action StateChanged;

        private void Changed() { try { StateChanged?.Invoke(); } catch { } }

        // ---------------------------------------------------------------- ports

        public static IList<string> AvailablePorts()
        {
            try { return PortLister(); } catch { return new List<string>(); }
        }

        // ---------------------------------------------------------------- connection

        /// <summary>Starts the background monitor: connects (and reconnects) automatically and checks the link every few seconds.</summary>
        public void StartMonitor()
        {
            lock (_lock)
            {
                if (_monitorCts != null || _disposed) return;
                _monitorCts = new CancellationTokenSource();
            }
            CancellationToken token = _monitorCts.Token;
            Task.Run(() => MonitorLoop(token));
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsConnected)
                    {
                        // STATUS doubles as the keep-alive that the firmware's ring-light watchdog needs
                        Esp32Response r = await SendAsync("STATUS", "STATUS", 900).ConfigureAwait(false);
                        if (r.Ok) { _missedPolls = 0; }
                        else if (++_missedPolls >= 2) { Disconnect("The light controller stopped answering."); }
                        await Task.Delay(3000, token).ConfigureAwait(false);
                    }
                    else
                    {
                        if (AutoConnect) await ConnectAsync().ConfigureAwait(false);
                        await Task.Delay(IsConnected ? 3000 : 4000, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { LastError = ex.Message; try { await Task.Delay(4000, token).ConfigureAwait(false); } catch { return; } }
            }
        }

        /// <summary>Opens the preferred port, or looks through the ports until one answers HELLO like the controller. Safe to call repeatedly.</summary>
        public async Task<bool> ConnectAsync()
        {
            if (_disposed) return false;
            if (!await _connectGate.WaitAsync(0).ConfigureAwait(false)) return IsConnected;      // already connecting
            try
            {
                if (IsConnected) return true;
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(PreferredPort)) candidates.Add(PreferredPort.Trim());
                else
                {
                    if (!string.IsNullOrEmpty(_lastGoodPort)) candidates.Add(_lastGoodPort);
                    foreach (string p in AvailablePorts()) candidates.Add(p);
                }
                candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (candidates.Count == 0) { SetError("No serial port was found. Connect the controller's USB cable."); return false; }

                _portProblem = "";
                foreach (string port in candidates)
                {
                    if (await TryPortAsync(port).ConfigureAwait(false)) return true;
                }
                SetError((string.IsNullOrWhiteSpace(PreferredPort)
                    ? "No light controller answered on " + string.Join(", ", candidates) + "."
                    : "The light controller did not answer on " + PreferredPort + ".")
                    + (string.IsNullOrEmpty(_portProblem) ? "" : " (" + _portProblem + ")"));
                return false;
            }
            finally { _connectGate.Release(); }
        }

        private async Task<bool> TryPortAsync(string port)
        {
            IEsp32Transport transport = null;
            try
            {
                transport = TransportFactory(port);
                lock (_lock)
                {
                    _transport = transport;
                    StartReader(transport);
                }
                // the board may still be starting: ask twice
                Esp32Response hello = await SendAsync("HELLO", "READY", 1500, requireConnected: false).ConfigureAwait(false);
                if (!hello.Ok) hello = await SendAsync("HELLO", "READY", 1500, requireConnected: false).ConfigureAwait(false);
                if (!hello.Ok || !IsLumyvueReady(hello.Line)) { Close(); return false; }

                PortName = port;
                _lastGoodPort = port;
                IsConnected = true;
                _missedPolls = 0;
                LastError = "";
                await RefreshStatusAsync().ConfigureAwait(false);
                Changed();
                return true;
            }
            catch (Exception ex)
            {
                _portProblem = port + ": " + ex.Message;
                Close();
                return false;
            }
        }

        private static bool IsLumyvueReady(string line)
        {
            // READY:LUMYVUE:<fw>:<protocol>
            string[] p = (line ?? "").Split(':');
            int proto;
            return p.Length >= 4 && p[0] == "READY" && p[1] == "LUMYVUE" && int.TryParse(p[3], out proto) && proto == ProtocolVersion;
        }

        private void SetError(string message)
        {
            if (LastError == message) return;
            LastError = message;
            Changed();
        }

        /// <summary>Closes the connection. The monitor reconnects later (unless AutoConnect is off).</summary>
        public void Disconnect(string reason = null)
        {
            bool was = IsConnected;
            Close();
            if (reason != null) LastError = reason;
            if (was) Changed();
        }

        private void Close()
        {
            IEsp32Transport t;
            CancellationTokenSource cts;
            lock (_lock)
            {
                t = _transport; _transport = null;
                cts = _readerCts; _readerCts = null;
                IsConnected = false;
                PortName = null;
            }
            try { cts?.Cancel(); } catch { }
            try { t?.Dispose(); } catch { }
            TaskCompletionSource<string> p;
            lock (_lock) { p = _pending; _pending = null; }
            p?.TrySetResult(null);
        }

        // ---------------------------------------------------------------- reading

        // A COM port must be read with ordinary blocking reads on a thread of its own: Windows aborts an overlapped (async) serial read when the
        // thread pool thread that started it is reused ("The I/O operation has been aborted..."). So: one dedicated background thread, no async I/O.
        private void StartReader(IEsp32Transport transport)
        {
            _readerCts?.Cancel();
            var cts = new CancellationTokenSource();
            _readerCts = cts;
            Stream stream = transport.Stream;
            var thread = new Thread(() =>
            {
                var buffer = new byte[256];
                var line = new StringBuilder();
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        int n;
                        try { n = stream.Read(buffer, 0, buffer.Length); }
                        catch (TimeoutException) { continue; }                      // no data yet (the port has a short read time-out)
                        if (n <= 0)
                        {
                            if (!transport.IsAlive)
                            {
                                if (!cts.IsCancellationRequested && ReferenceEquals(_readerCts, cts)) Disconnect("The light controller was disconnected.");
                                return;
                            }
                            Thread.Sleep(15);
                            continue;
                        }
                        for (int i = 0; i < n; i++)
                        {
                            char c = (char)buffer[i];
                            if (c == '\n' || c == '\r')
                            {
                                if (line.Length > 0) { OnLine(line.ToString()); line.Clear(); }
                            }
                            else if (c >= ' ' && c <= '~') { if (line.Length < 200) line.Append(c); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // the cable was pulled or the port vanished
                    if (!cts.IsCancellationRequested && ReferenceEquals(_readerCts, cts)) { _portProblem = ex.GetType().Name + ": " + ex.Message; Disconnect("The light controller was disconnected."); }
                }
            }) { IsBackground = true, Name = "LUMYVUE ESP32 reader" };
            thread.Start();
        }

        private void OnLine(string line)
        {
            if (line.StartsWith("EVENT:", StringComparison.Ordinal)) return;           // unsolicited notices (e.g. the ring watchdog)
            if (line.StartsWith("READY:", StringComparison.Ordinal) && IsConnected && _pending == null)
            {
                // the board restarted by itself (power glitch): read its state again
                Task.Run(() => RefreshStatusAsync());
                return;
            }
            TaskCompletionSource<string> p;
            string expect;
            lock (_lock) { p = _pending; expect = _expect; }
            if (p == null) return;                                                      // boot messages and noise are ignored
            if (line.StartsWith(expect, StringComparison.Ordinal) || line.StartsWith("ERROR:", StringComparison.Ordinal)) p.TrySetResult(line);
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Sends one command and waits for its answer (never longer than timeoutMs). Never throws.</summary>
        public async Task<Esp32Response> SendAsync(string command, string expectPrefix, int timeoutMs = 800, bool requireConnected = true)
        {
            if (_disposed) return new Esp32Response { Error = "The controller is closed." };
            if (requireConnected && !IsConnected) return new Esp32Response { Error = "The light controller is not connected." };
            if (!await _commandGate.WaitAsync(timeoutMs).ConfigureAwait(false)) return new Esp32Response { Error = "The light controller is busy." };
            try
            {
                IEsp32Transport t;
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_lock) { t = _transport; _pending = tcs; _expect = expectPrefix; }
                if (t == null) return new Esp32Response { Error = "The light controller is not connected." };
                try
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(command + "\n");
                    t.Stream.Write(bytes, 0, bytes.Length);                  // short synchronous write (the port has a 0.5 s write time-out)
                    t.Stream.Flush();
                }
                catch (Exception ex)
                {
                    Disconnect("Could not write to the light controller: " + ex.Message);
                    return new Esp32Response { Error = "Could not write to the light controller." };
                }
                Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (done != tcs.Task) return new Esp32Response { Error = "The light controller did not answer in time." };
                string line = tcs.Task.Result;
                if (line == null) return new Esp32Response { Error = "The light controller was disconnected." };
                if (line.StartsWith("ERROR:", StringComparison.Ordinal)) return new Esp32Response { Line = line, Error = line };
                return new Esp32Response { Ok = true, Line = line };
            }
            finally
            {
                lock (_lock) { _pending = null; }
                _commandGate.Release();
            }
        }

        /// <summary>Percent (0..100) to the wire range (0..255).</summary>
        public static int ToWire(int percent) => (int)Math.Round(Math.Max(0, Math.Min(100, percent)) * 255.0 / 100.0);

        /// <summary>Wire range (0..255) to percent (0..100).</summary>
        public static int ToPercent(int wire) => (int)Math.Round(Math.Max(0, Math.Min(255, wire)) * 100.0 / 255.0);

        public Task<Esp32Response> SetSoftAsync(int percent) => SendAsync("SOFT:" + ToWire(percent), "OK:SOFT:");
        public Task<Esp32Response> SetRingAsync(int percent) => SendAsync("RING:" + ToWire(percent), "OK:RING:");
        public Task<Esp32Response> SetRingLevelAsync(int percent) => SendAsync("RINGLEVEL:" + Math.Max(1, ToWire(percent)), "OK:RINGLEVEL:");
        public Task<Esp32Response> SetTemperatureAsync(int percent) => SendAsync("TEMP:" + ToWire(percent), "OK:TEMP:");
        public Task<Esp32Response> CaptureStartAsync() => SendAsync("CAPTURE_START", "OK:CAPTURE_START", 1000);
        public Task<Esp32Response> CaptureEndAsync() => SendAsync("CAPTURE_END", "OK:CAPTURE_END", 1000);
        public Task<Esp32Response> AllOffAsync(int timeoutMs = 1000) => SendAsync("ALL_OFF", "OK:ALL_OFF", timeoutMs);
        public Task<Esp32Response> PingAsync() => SendAsync("PING", "PONG");

        /// <summary>Reads STATUS and remembers it in LastStatus.</summary>
        public async Task<Esp32Status> RefreshStatusAsync()
        {
            Esp32Response r = await SendAsync("STATUS", "STATUS", 900, requireConnected: false).ConfigureAwait(false);
            if (!r.Ok) return null;
            Esp32Status s = ParseStatus(r.Line);
            if (s != null) { LastStatus = s; Changed(); }
            return s;
        }

        internal static Esp32Status ParseStatus(string line)
        {
            try
            {
                if (line == null || !line.StartsWith("STATUS:", StringComparison.Ordinal)) return null;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string pair in line.Substring(7).Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) d[pair.Substring(0, eq)] = pair.Substring(eq + 1);
                }
                Func<string, int> I = k => { int v; return d.ContainsKey(k) && int.TryParse(d[k], out v) ? v : 0; };
                return new Esp32Status
                {
                    Soft = I("SOFT"), SoftLevel = I("SOFTLEVEL"), Ring = I("RING"), RingLevel = I("RINGLEVEL"), Temp = I("TEMP"),
                    Capture = I("CAPTURE") == 1, Firmware = d.ContainsKey("FW") ? d["FW"] : "", Protocol = I("P"),
                    UptimeSeconds = d.ContainsKey("UP") && long.TryParse(d["UP"], out long up) ? up : 0
                };
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- shutdown

        /// <summary>Turns every light off and closes the port. Waits at most timeoutMs; never blocks Windows from shutting down.</summary>
        public void ShutdownLights(int timeoutMs = 1500)
        {
            try
            {
                StopMonitor();
                if (IsConnected)
                {
                    Task<Esp32Response> t = AllOffAsync(timeoutMs);
                    t.Wait(timeoutMs + 300);
                }
            }
            catch { }
            finally { Close(); }
        }

        private void StopMonitor()
        {
            CancellationTokenSource m;
            lock (_lock) { m = _monitorCts; _monitorCts = null; }
            try { m?.Cancel(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopMonitor();
            Close();
        }

        // ---------------------------------------------------------------- the real serial port

        private sealed class SerialTransport : IEsp32Transport
        {
            private readonly SerialPort _port;
            public Stream Stream => _port.BaseStream;
            public bool IsAlive { get { try { return _port.IsOpen; } catch { return false; } } }

            public SerialTransport(string name)
            {
                _port = new SerialPort(name, Baud, Parity.None, 8, StopBits.One)
                {
                    // never raise DTR / RTS: on most ESP32 boards they pulse the reset line, which would restart the fade-in
                    DtrEnable = false,
                    RtsEnable = false,
                    Handshake = Handshake.None,
                    ReadTimeout = 250,
                    WriteTimeout = 500,
                    Encoding = Encoding.ASCII
                };
                _port.Open();
                try { _port.DiscardInBuffer(); } catch { }
            }

            public void Dispose()
            {
                try { if (_port.IsOpen) _port.Close(); } catch { }
                try { _port.Dispose(); } catch { }
            }
        }
    }
}
