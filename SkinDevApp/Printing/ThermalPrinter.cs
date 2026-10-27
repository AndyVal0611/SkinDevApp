// ============================================================================
// ThermalPrinter.cs  -  namespace SkinDevApp.Printing
//
// Sends bytes to the QR701 over a serial (COM) port - normally a USB-to-TTL
// serial adapter wired to the printer's TTL RX/GND. Never wire the printer's
// TTL pins to an RS-232 (+/-12 V) port.
//
// What this class can and cannot know:
//   * It can tell whether the COM port exists and can be opened, and whether
//     every byte was handed to the serial driver.
//   * It cannot tell whether paper came out: the QR701 is used write-only (no
//     verified status command), so a successful send is reported as
//     "Sent to printer", never as "printed". Staff confirm the paper.
//
// Simulation mode writes the receipt to a file and is labelled everywhere as
// "nothing was printed".
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkinDevApp.Printing
{
    public enum SendOutcome { SentToPrinter, Simulated, Failed, Cancelled }

    public sealed class SendResult
    {
        public SendOutcome Outcome { get; set; }
        public int BytesSent { get; set; }
        public int BytesTotal { get; set; }
        public string Error { get; set; }
        public string SimulationFile { get; set; }
    }

    /// <summary>What the screens show about the printer. Never claims more than the port can tell.</summary>
    public sealed class PrinterStatus
    {
        public bool CanSend { get; set; }
        public string Headline { get; set; }
        public string Detail { get; set; }
        public int Level { get; set; }                      // 0 fine, 1 look closer, 2 cannot print
    }

    public static class ThermalPrinter
    {
        private static int _busy;                           // one job at a time, across all windows

        public static string SimulationFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUMYVUE", "ReceiptSimulation");

        public static string[] PortNames()
        {
            try { return SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ThenBy(p => p).ToArray(); }
            catch { return new string[0]; }
        }

        /// <summary>Quick check without opening the port (safe to call while a screen is drawn).</summary>
        public static PrinterStatus Status(PrinterSettings s)
        {
            if (!s.Enabled)
                return new PrinterStatus { CanSend = false, Level = 2, Headline = "Printer turned off", Detail = "Receipts can be previewed. Turn the printer on in Settings > Thermal printer to print." };
            if (s.Simulation)
                return new PrinterStatus { CanSend = true, Level = 1, Headline = "Simulation mode: nothing is printed", Detail = "Receipts are saved as files for testing. Turn simulation off in Settings to print on the QR701." };
            if (string.IsNullOrWhiteSpace(s.PortName))
                return new PrinterStatus { CanSend = false, Level = 2, Headline = "No COM port chosen", Detail = "Choose the printer's serial port in Settings > Thermal printer." };
            if (!PortNames().Contains(s.PortName, StringComparer.OrdinalIgnoreCase))
                return new PrinterStatus { CanSend = false, Level = 2, Headline = s.PortName + " not found", Detail = "The serial adapter may be unplugged, or Windows gave it another COM number. Check the cable, then choose the port in Settings." };
            return new PrinterStatus
            {
                CanSend = true, Level = 0, Headline = "QR701 on " + s.PortName,
                Detail = s.BaudRate + " baud. The printer cannot report paper or power, so please check that it is on and has paper."
            };
        }

        /// <summary>Opens and closes the port. Proves the port works, not that the printer is ready.</summary>
        public static Task<string> TestConnectionAsync(PrinterSettings s)
        {
            return Task.Run(() =>
            {
                if (s.Simulation) return "Simulation mode is on: no port is used and nothing is printed.";
                if (string.IsNullOrWhiteSpace(s.PortName)) return "Choose a COM port first.";
                try
                {
                    using (SerialPort port = Open(s)) { }
                    return s.PortName + " opened and closed correctly with " + s.BaudRate + " baud. This shows the port works; it cannot show " +
                           "whether the printer is powered or has paper. Use Test print to check the printer itself.";
                }
                catch (Exception ex) { return Explain(ex, s); }
            });
        }

        /// <summary>
        /// Sends the chunks (one per receipt line) with a pause after each. Cancelling stops between lines.
        /// "SentToPrinter" means every byte left the serial driver - not that paper came out.
        /// </summary>
        public static Task<SendResult> SendAsync(IList<byte[]> chunks, PrinterSettings s, IProgress<double> progress, CancellationToken cancel)
        {
            return Task.Run(() =>
            {
                int total = chunks.Sum(c => c.Length);
                var result = new SendResult { BytesTotal = total };
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                {
                    result.Outcome = SendOutcome.Failed;
                    result.Error = "Another receipt is being sent right now. Wait until it has finished.";
                    return result;
                }
                try
                {
                    if (s.Simulation) return Simulate(chunks, s, progress, cancel, result);
                    if (!s.Enabled) { result.Outcome = SendOutcome.Failed; result.Error = "The printer is turned off in Settings."; return result; }
                    if (string.IsNullOrWhiteSpace(s.PortName)) { result.Outcome = SendOutcome.Failed; result.Error = "No COM port is chosen in Settings."; return result; }

                    using (SerialPort port = Open(s))
                    {
                        foreach (byte[] chunk in chunks)
                        {
                            if (cancel.IsCancellationRequested)
                            {
                                result.Outcome = SendOutcome.Cancelled;
                                result.Error = result.BytesSent == 0 ? "Cancelled before anything was sent." : "Cancelled part-way: part of the receipt may have printed.";
                                return result;
                            }
                            port.Write(chunk, 0, chunk.Length);
                            result.BytesSent += chunk.Length;
                            progress?.Report(total == 0 ? 1 : (double)result.BytesSent / total);
                            if (s.LineDelayMs > 0) Thread.Sleep(s.LineDelayMs);
                        }

                        // wait until the driver has passed every byte on (about 10 bits per byte on the wire)
                        int waitMs = (int)(total * 10.0 * 1000 / Math.Max(1200, s.BaudRate)) + 3000;
                        var until = DateTime.UtcNow.AddMilliseconds(waitMs);
                        while (port.BytesToWrite > 0 && DateTime.UtcNow < until) Thread.Sleep(20);
                        if (port.BytesToWrite > 0)
                        {
                            result.Outcome = SendOutcome.Failed;
                            result.BytesSent = Math.Max(0, total - port.BytesToWrite);
                            result.Error = "The receipt was not sent completely (" + port.BytesToWrite + " bytes left). Check the cable, the printer's power and the flow-control setting.";
                            return result;
                        }
                    }
                    result.Outcome = SendOutcome.SentToPrinter;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Outcome = SendOutcome.Failed;
                    result.Error = Explain(ex, s);
                    return result;
                }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        private static SendResult Simulate(IList<byte[]> chunks, PrinterSettings s, IProgress<double> progress, CancellationToken cancel, SendResult result)
        {
            int total = result.BytesTotal;
            int perChunkMs = Math.Max(5, s.LineDelayMs / 2);
            foreach (byte[] chunk in chunks)
            {
                if (cancel.IsCancellationRequested) { result.Outcome = SendOutcome.Cancelled; result.Error = "Cancelled (simulation)."; return result; }
                result.BytesSent += chunk.Length;
                progress?.Report(total == 0 ? 1 : (double)result.BytesSent / total);
                Thread.Sleep(perChunkMs);
            }
            try
            {
                Directory.CreateDirectory(SimulationFolder);
                string file = Path.Combine(SimulationFolder, "receipt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bin");
                File.WriteAllBytes(file, chunks.SelectMany(c => c).ToArray());
                // a readable copy next to it (printable ASCII only; control bytes removed)
                string text = new string(Encoding.ASCII.GetString(chunks.SelectMany(c => c).ToArray()).Where(ch => ch == '\n' || (ch >= ' ' && ch <= '~')).ToArray());
                File.WriteAllText(Path.ChangeExtension(file, ".txt"), "SIMULATION - NOTHING WAS PRINTED" + Environment.NewLine + Environment.NewLine + text);
                result.SimulationFile = file;
                result.Outcome = SendOutcome.Simulated;
            }
            catch (Exception ex)
            {
                result.Outcome = SendOutcome.Failed;
                result.Error = "Simulation file could not be written: " + ex.Message;
            }
            return result;
        }

        private static SerialPort Open(PrinterSettings s)
        {
            var port = new SerialPort(s.PortName.Trim(), s.BaudRate,
                (Parity)Enum.Parse(typeof(Parity), s.Parity ?? "None", true), s.DataBits,
                (StopBits)Enum.Parse(typeof(StopBits), s.StopBits ?? "One", true))
            {
                Handshake = (Handshake)Enum.Parse(typeof(Handshake), s.Handshake ?? "None", true),
                Encoding = Encoding.ASCII,
                WriteTimeout = 5000,
                ReadTimeout = 500
            };
            try { port.Open(); }
            catch { port.Dispose(); throw; }
            return port;
        }

        /// <summary>Plain-language reason for a serial error (no patient data is ever part of it).</summary>
        public static string Explain(Exception ex, PrinterSettings s)
        {
            string port = string.IsNullOrWhiteSpace(s.PortName) ? "the port" : s.PortName;
            if (ex is UnauthorizedAccessException)
                return port + " is in use by another program, or access was denied. Close any other program using the printer and try again.";
            if (ex is TimeoutException)
                return "The printer did not accept the data in time. Check the printer's power, paper and the flow-control setting.";
            if (ex is ArgumentOutOfRangeException || ex is ArgumentException)
                return "These serial settings are not supported by " + port + " (" + s.BaudRate + " baud, " + s.DataBits + " data bits, parity " + s.Parity + ", stop bits " + s.StopBits + ").";
            if (ex is FileNotFoundException || ex is IOException)
                return "Communication with " + port + " failed: the serial adapter may be unplugged, the COM number may have changed, or the printer lost power. (" + ex.Message.Trim() + ")";
            if (ex is InvalidOperationException)
                return port + " could not be used: " + ex.Message;
            return "Printing failed: " + ex.Message;
        }
    }
}
