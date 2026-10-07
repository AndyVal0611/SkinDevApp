// CameraDiscovery.cs - finds the usable physical webcam on this PC (not tied to one model).
//
// Ranking: a camera chosen in Settings, then Logitech BRIO, then any other Logitech, then any other physical webcam.
// Virtual and infrared cameras (OBS, Snap, Windows Hello IR, phone link, ...) are only used when nothing else exists.
// "Connected" is reported only after the camera actually delivers a frame (ProbeAsync), not just because it is listed.
// This class only enumerates and probes; the scan page keeps its own, unchanged capture code.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AForge.Video.DirectShow;
using SkinDevApp.Data;

namespace SkinDevApp
{
    public sealed class CameraInfo
    {
        public string Name { get; set; }
        public string Moniker { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsBrio { get; set; }
        public bool IsPreferredBySettings { get; set; }
        public int Rank { get; set; }                         // lower = better
        public override string ToString() => Name + (IsVirtual ? " (virtual / infrared)" : "");
    }

    public sealed class CameraProbeResult
    {
        public bool Ok { get; set; }
        public CameraInfo Camera { get; set; }
        public string Error { get; set; }
    }

    public static class CameraDiscovery
    {
        private static readonly string[] NotARealWebcam =
            { "virtual", "obs", "ir camera", "infrared", " ir ", "snap camera", "droidcam", "ndi", "manycam", "xsplit", "depth", "camo", "nvidia broadcast", "phone link" };

        /// <summary>Pure ranking by name; separated from DirectShow so it can be tested.</summary>
        public static List<CameraInfo> Rank(IEnumerable<KeyValuePair<string, string>> nameAndMoniker, string chosenName)
        {
            string chosen = (chosenName ?? "").Trim().ToLowerInvariant();
            var list = new List<CameraInfo>();
            foreach (var kv in nameAndMoniker)
            {
                string n = kv.Key ?? "";
                string low = n.ToLowerInvariant();
                var c = new CameraInfo
                {
                    Name = n,
                    Moniker = kv.Value,
                    IsVirtual = NotARealWebcam.Any(k => (" " + low + " ").Contains(k)),
                    IsBrio = low.Contains("brio"),
                    IsPreferredBySettings = chosen.Length > 0 && low == chosen
                };
                c.Rank = c.IsPreferredBySettings ? 0
                       : c.IsVirtual ? 90
                       : c.IsBrio ? 10
                       : low.Contains("logitech") ? 20
                       : 30;
                list.Add(c);
            }
            return list.OrderBy(c => c.Rank).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>All video input devices, best first. Empty when none (or when enumeration fails).</summary>
        public static List<CameraInfo> List()
        {
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                var pairs = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < devices.Count; i++)
                    pairs.Add(new KeyValuePair<string, string>(devices[i].Name, devices[i].MonikerString));
                return Rank(pairs, ScanSettings.Current.CameraName);
            }
            catch { return new List<CameraInfo>(); }
        }

        /// <summary>Opens the camera, waits for one real frame, then releases it. Never leaves the device open.</summary>
        public static Task<CameraProbeResult> ProbeAsync(CameraInfo cam, int timeoutMs = 4000)
        {
            return Task.Run(() =>
            {
                var res = new CameraProbeResult { Camera = cam };
                VideoCaptureDevice src = null;
                try
                {
                    src = new VideoCaptureDevice(cam.Moniker);
                    var got = new System.Threading.ManualResetEventSlim(false);
                    string err = null;
                    src.NewFrame += (s, e) => got.Set();
                    src.VideoSourceError += (s, e) => err = e.Description;
                    src.Start();
                    res.Ok = got.Wait(timeoutMs);
                    if (!res.Ok) res.Error = err ?? "No frame arrived within " + (timeoutMs / 1000) + " s (camera busy or driver problem).";
                }
                catch (Exception ex) { res.Error = ex.Message; }
                finally
                {
                    try { if (src != null && src.IsRunning) { src.SignalToStop(); src.WaitForStop(); } } catch { }
                }
                return res;
            });
        }

        /// <summary>Probes cameras in rank order and returns the first that delivers frames (skips virtual ones unless nothing else works).</summary>
        public static async Task<CameraProbeResult> FindUsableAsync(IList<CameraInfo> ranked, string forceName = null)
        {
            IEnumerable<CameraInfo> order = ranked;
            if (!string.IsNullOrEmpty(forceName))
                order = ranked.Where(c => string.Equals(c.Name, forceName, StringComparison.OrdinalIgnoreCase)).Concat(ranked);
            string lastError = null;
            foreach (CameraInfo c in order.Distinct())
            {
                CameraProbeResult r = await ProbeAsync(c).ConfigureAwait(true);
                if (r.Ok) return r;
                lastError = c.Name + ": " + r.Error;
            }
            return new CameraProbeResult { Ok = false, Error = lastError ?? "No camera found." };
        }

        /// <summary>Index into a FilterInfoCollection for the scan page (same ranking, same fallback as before).</summary>
        public static int PickIndex(FilterInfoCollection devices)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < devices.Count; i++) pairs.Add(new KeyValuePair<string, string>(devices[i].Name, devices[i].MonikerString));
            CameraInfo best = Rank(pairs, ScanSettings.Current.CameraName).FirstOrDefault();
            if (best == null) return 0;
            for (int i = 0; i < devices.Count; i++) if (devices[i].MonikerString == best.Moniker) return i;
            return 0;
        }
    }
}
