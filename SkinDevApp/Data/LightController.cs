// ============================================================================
// LightController.cs  -  namespace SkinDevApp.Data
//
// The lighting features of LUMYVUE (soft lights and ring light) as the rest of the application uses them. All hardware communication is in
// SkinDevApp.Hardware.ESP32Controller (USB serial); this class only translates between the screens (percent, on/off) and that controller,
// remembers the chosen COM port, and applies the lighting rules of the scan:
//   * soft lights: on from power-up (the ESP32 fades them in by itself); the app only makes sure they are on once it is connected,
//     without restarting a fade that is already running
//   * ring light: off while idle; CAPTURE_START when image capture begins, CAPTURE_END when it ends, is cancelled or fails
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkinDevApp.Hardware;

namespace SkinDevApp.Data
{
    /// <summary>What the controller last reported, in the units the screens use (percent). These are the controller's settings, not measured light.</summary>
    public sealed class LightState
    {
        public bool On { get; set; }                      // ring light is lit
        public int Brightness { get; set; } = 60;         // ring brightness 0-100 %
        public int Temperature { get; set; } = 50;        // ring colour 0 warm - 100 cool
        public bool CobOn { get; set; }                   // soft lights are lit
        public int CobBrightness { get; set; } = 100;     // soft brightness 0-100 %
        public string Host { get; set; }                  // the COM port it is connected on
        public string Firmware { get; set; }
    }

    public static class LightController
    {
        private const string KeyPort = "light.port", KeyAuto = "light.auto";
        /// <summary>Time given to the ring light to reach its brightness after the controller accepted the command.</summary>
        public const int RingStabilizationMs = 400;

        private static ESP32Controller C => ESP32Controller.Instance;
        private static bool _initialised, _softChecked;

        // ---------------------------------------------------------------- settings

        /// <summary>The COM port chosen by hand; empty means "find it automatically".</summary>
        public static string PreferredPort
        {
            get { Init(); return C.PreferredPort ?? ""; }
            set
            {
                Init();
                C.PreferredPort = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
                Write(KeyPort, C.PreferredPort);
                C.Disconnect();                           // the monitor reconnects on the new port
                _softChecked = false;
            }
        }

        /// <summary>Switch the ring light on when image capture starts and off when it ends (on by default).</summary>
        public static bool AutoDuringScan
        {
            get { return !string.Equals(Read(KeyAuto, "true"), "false", StringComparison.OrdinalIgnoreCase); }
            set { Write(KeyAuto, value ? "true" : "false"); }
        }

        public static bool IsConnected => C.IsConnected;
        public static string PortName => C.PortName;
        public static string LastError => C.LastError;
        public static event Action ConnectionChanged;

        private static string Read(string key, string def)
        {
            try
            {
                if (!StudyDatabase.IsReady) return def;
                string v;
                return StudyRepository.LoadSettings().TryGetValue(key, out v) && v != null ? v : def;
            }
            catch { return def; }
        }

        private static void Write(string key, string value)
        {
            try { StudyRepository.SaveSettings(new Dictionary<string, string> { { key, value } }); } catch { }
        }

        private static void Init()
        {
            if (_initialised) return;
            _initialised = true;
            C.PreferredPort = Read(KeyPort, "");
            C.StateChanged += OnControllerChanged;
        }

        private static void OnControllerChanged()
        {
            try { ConnectionChanged?.Invoke(); } catch { }
            // First time connected: make sure the soft lights are on. The controller fades them in by itself when it gets power, so nothing is sent
            // while they are already on or fading (a fade in progress is never restarted); only if they are OFF (for example after a shutdown with
            // the USB port still powered) are they switched on, and the controller then fades them in.
            if (C.IsConnected && !_softChecked && C.LastStatus != null)
            {
                _softChecked = true;
                if (C.LastStatus.Soft == 0)
                {
                    int level = C.LastStatus.SoftLevel > 0 ? ESP32Controller.ToPercent(C.LastStatus.SoftLevel) : 100;
                    Task.Run(() => C.SetSoftAsync(level));
                }
            }
            if (!C.IsConnected) _softChecked = false;
        }

        // ---------------------------------------------------------------- start-up and shutdown

        /// <summary>Called once when LUMYVUE starts: starts the background connection to the controller. Never blocks and never fails.</summary>
        public static void StartupSync()
        {
            Init();
            C.StartMonitor();
        }

        /// <summary>LUMYVUE is shutting down the device: both lights off, then the port is closed. Waits at most timeoutMs, so a missing controller cannot stall the shutdown.</summary>
        public static void ShutdownLights(int timeoutMs = 1500)
        {
            C.ShutdownLights(timeoutMs);
        }

        // ---------------------------------------------------------------- reading and changing

        /// <summary>Connects if necessary and reads the controller's state. Null when it is not connected.</summary>
        public static async Task<LightState> GetAsync()
        {
            Init();
            if (!C.IsConnected) await C.ConnectAsync().ConfigureAwait(false);
            if (!C.IsConnected) return null;
            Esp32Status s = await C.RefreshStatusAsync().ConfigureAwait(false);
            return s == null ? null : ToState(s);
        }

        /// <summary>Changes only what is given. Brightness values are percent (0-100). Returns the state reported afterwards, or null if not connected.</summary>
        public static async Task<LightState> SetAsync(bool? on = null, int? brightness = null, int? temperature = null, bool? cob = null, int? cobBrightness = null)
        {
            Init();
            if (!C.IsConnected) await C.ConnectAsync().ConfigureAwait(false);
            if (!C.IsConnected) return null;
            if (brightness.HasValue) await C.SetRingLevelAsync(brightness.Value).ConfigureAwait(false);
            if (temperature.HasValue) await C.SetTemperatureAsync(temperature.Value).ConfigureAwait(false);
            if (cob.HasValue || cobBrightness.HasValue)
            {
                bool soft = cob ?? (cobBrightness.Value > 0);
                int level = cobBrightness ?? (C.LastStatus != null ? ESP32Controller.ToPercent(C.LastStatus.SoftLevel) : 100);
                await C.SetSoftAsync(soft ? Math.Max(1, level) : 0).ConfigureAwait(false);
            }
            if (on.HasValue)
            {
                if (on.Value) await C.CaptureStartAsync().ConfigureAwait(false);       // lights the ring at its remembered level
                else await C.CaptureEndAsync().ConfigureAwait(false);
            }
            Esp32Status s = await C.RefreshStatusAsync().ConfigureAwait(false);
            return s == null ? null : ToState(s);
        }

        private static LightState ToState(Esp32Status s)
        {
            return new LightState
            {
                On = s.RingOn,
                Brightness = ESP32Controller.ToPercent(s.RingLevel),
                Temperature = ESP32Controller.ToPercent(s.Temp),
                CobOn = s.SoftOn,
                CobBrightness = ESP32Controller.ToPercent(s.SoftOn ? s.Soft : s.SoftLevel),
                Host = C.PortName,
                Firmware = s.Firmware
            };
        }

        /// <summary>LUMYVUE is closing normally: the ring light goes off (the soft lights stay on) and the port is closed.</summary>
        public static void CloseForExit()
        {
            try { if (_ringOn && C.IsConnected) C.CaptureEndAsync().Wait(800); } catch { }
            C.Dispose();
        }

        // ---------------------------------------------------------------- the ring light during image capture

        private static bool _ringOn;

        /// <summary>
        /// Image capture is about to begin: lights the ring and waits until the controller has ACCEPTED the command, then gives the light a moment to settle.
        /// True when the ring is lit (or lighting is switched off / not connected, so capture is never blocked by a missing controller).
        /// </summary>
        public static async Task<bool> RingOnAsync()
        {
            if (!AutoDuringScan) return true;
            Init();
            if (!C.IsConnected) return true;                                   // no controller: capture goes on without it
            Esp32Response r = await C.CaptureStartAsync().ConfigureAwait(false);
            if (!r.Ok) return false;
            _ringOn = true;
            await Task.Delay(RingStabilizationMs).ConfigureAwait(false);
            return true;
        }

        /// <summary>Fire-and-forget version of RingOnAsync.</summary>
        public static void RingOn() { Task.Run(() => RingOnAsync()); }

        /// <summary>Image capture ended, was cancelled or failed: the ring light goes off (the soft lights stay on). Only if this app lit it.</summary>
        public static void RingOff()
        {
            if (!_ringOn) return;
            _ringOn = false;
            Task.Run(() => C.CaptureEndAsync());
        }
    }
}
