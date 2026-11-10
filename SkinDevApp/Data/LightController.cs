// ============================================================================
// LightController.cs  -  namespace SkinDevApp.Data
//
// Talks to the ESP32 light controller (ringlight.ino) over WiFi. The controller serves a tiny web API:
//   GET /api/state                                   -> {"on":true,"b":60,"t":50,"cob":true,"cb":100}
//   GET /api/set?on=1&b=60&t=50&cob=1&cb=100         -> the same JSON after the change
// (on = ring light on/off, b = ring brightness 0-100, t = colour temperature 0 warm - 100 cool, cob = COB lights on/off, cb = COB brightness)
// It is found as http://ringlight.local, then (the controller's own hotspot) http://192.168.4.1. A different address can be set on the
// Lighting page. Every call has a short time-out and never throws: when the controller is not reachable the result is null.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SkinDevApp.Data
{
    public sealed class LightState
    {
        public bool On { get; set; }
        public int Brightness { get; set; } = 60;
        public int Temperature { get; set; } = 50;
        public bool CobOn { get; set; }
        public int CobBrightness { get; set; } = 100;
        public string Host { get; set; }                  // where it answered
    }

    public static class LightController
    {
        private const string KeyHost = "light.host", KeyAuto = "light.auto";
        public const string DefaultHost = "ringlight.local";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        private static string _working;

        /// <summary>The address entered on the Lighting page (without http://); empty means the default.</summary>
        public static string Host
        {
            get { return Read(KeyHost, DefaultHost); }
            set { Write(KeyHost, string.IsNullOrWhiteSpace(value) ? DefaultHost : value.Trim()); _working = null; }
        }

        /// <summary>Switch the ring light on when a scan starts and off when it ends (on by default).</summary>
        public static bool AutoDuringScan
        {
            get { return !string.Equals(Read(KeyAuto, "true"), "false", StringComparison.OrdinalIgnoreCase); }
            set { Write(KeyAuto, value ? "true" : "false"); }
        }

        private static string Read(string key, string def)
        {
            try
            {
                if (!StudyDatabase.IsReady) return def;
                string v;
                return StudyRepository.LoadSettings().TryGetValue(key, out v) && !string.IsNullOrWhiteSpace(v) ? v : def;
            }
            catch { return def; }
        }

        private static void Write(string key, string value)
        {
            try { StudyRepository.SaveSettings(new Dictionary<string, string> { { key, value } }); } catch { }
        }

        private static IEnumerable<string> Hosts()
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(_working)) list.Add(_working);
            list.Add(Host);
            list.Add(DefaultHost);
            list.Add("192.168.4.1");
            return list.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static Task<LightState> GetAsync() => Call("/api/state");

        /// <summary>Changes only what is given; the rest keeps its value on the controller.</summary>
        public static Task<LightState> SetAsync(bool? on = null, int? brightness = null, int? temperature = null, bool? cob = null, int? cobBrightness = null)
        {
            var q = new List<string>();
            if (on.HasValue) q.Add("on=" + (on.Value ? 1 : 0));
            if (brightness.HasValue) q.Add("b=" + Clamp(brightness.Value));
            if (temperature.HasValue) q.Add("t=" + Clamp(temperature.Value));
            if (cob.HasValue) q.Add("cob=" + (cob.Value ? 1 : 0));
            if (cobBrightness.HasValue) q.Add("cb=" + Clamp(cobBrightness.Value));
            return Call("/api/set?" + string.Join("&", q));
        }

        private static int Clamp(int v) => Math.Max(0, Math.Min(100, v));

        private static async Task<LightState> Call(string path)
        {
            foreach (string host in Hosts())
            {
                try
                {
                    string json = await Http.GetStringAsync("http://" + host + path).ConfigureAwait(false);
                    LightState s = Parse(json);
                    if (s == null) continue;
                    s.Host = host;
                    _working = host;
                    return s;
                }
                catch { }
            }
            _working = null;
            return null;
        }

        internal static LightState Parse(string json)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(json))
                {
                    JsonElement r = d.RootElement;
                    return new LightState
                    {
                        On = r.GetProperty("on").GetBoolean(),
                        Brightness = r.GetProperty("b").GetInt32(),
                        Temperature = r.GetProperty("t").GetInt32(),
                        CobOn = r.GetProperty("cob").GetBoolean(),
                        CobBrightness = r.GetProperty("cb").GetInt32()
                    };
                }
            }
            catch { return null; }
        }

        private static bool _ringOn;

        /// <summary>Image capture starts (the camera is started): the ring light comes on, when the automatic setting is on. Never blocks and never fails.</summary>
        public static void RingOn()
        {
            if (!AutoDuringScan) return;
            _ringOn = true;
            Task.Run(() => SetAsync(on: true));
        }

        /// <summary>Image capture ends (the camera is stopped): the ring light goes off again. Only if this app switched it on.</summary>
        public static void RingOff()
        {
            if (!_ringOn) return;
            _ringOn = false;
            Task.Run(() => SetAsync(on: false));
        }

        /// <summary>
        /// When the app starts (the Mini PC has just started): make sure the soft lights are on. The controller fades them in by itself as soon as it has power;
        /// it needs a few seconds to join the WiFi, so this retries for about 100 seconds and then gives up quietly.
        /// </summary>
        public static void StartupSync()
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    LightState s = await GetAsync().ConfigureAwait(false);
                    if (s != null)
                    {
                        if (!s.CobOn) await SetAsync(cob: true).ConfigureAwait(false);
                        return;
                    }
                    await Task.Delay(5000).ConfigureAwait(false);
                }
            });
        }
    }
}
