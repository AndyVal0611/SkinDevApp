// ============================================================================
// ScanSettings.cs  -  namespace SkinDevApp.Data
//
// Researcher/admin settings stored in SystemSettings (every change is audited by
// StudyRepository.SaveSettings). Defaults equal the values that were hard-coded
// before, so an empty table changes nothing.
//
// Apply(...) pushes them into the live scan objects when the camera starts.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.Data
{
    public sealed class ScanSettings
    {
        // camera
        public string CameraName { get; set; } = "";            // "" = auto (prefers Logitech BRIO)
        public int CameraWidth { get; set; } = 640;
        public int CameraHeight { get; set; } = 480;

        // capture
        public bool AutoCapture { get; set; } = true;
        public int StableFramesRequired { get; set; } = 8;
        public int HoldStillMs { get; set; } = 1200;
        public double CooldownSeconds { get; set; } = 4.0;
        public double MinSharpness { get; set; } = 25.0;
        public double MinBrightness { get; set; } = 45.0;
        public double MaxBrightness { get; set; } = 215.0;
        public double FrontMaxAbsYaw { get; set; } = 0.12;
        public double SideMinAbsYaw { get; set; } = 0.20;
        public bool ReviewBeforeFinish { get; set; } = true;

        // AI / stability
        public double MinConfidence { get; set; } = 0.65;
        public double MinMargin { get; set; } = 0.15;
        public double MinConsistency { get; set; } = 0.75;

        // Grad-CAM++
        public bool LiveGradCam { get; set; } = true;
        public int GradCamIntervalMs { get; set; } = 80;
        public double MaxHeatmapAgeSeconds { get; set; } = 6.0;
        public double OverlayOpacity { get; set; } = 0.60;
        public int GradCamTimeoutSeconds { get; set; } = 60;
        public string GradCamTargetLayer { get; set; } = "";      // "" = service default (research/debug only)

        private static ScanSettings _current;
        public static ScanSettings Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public static ScanSettings Load()
        {
            var s = new ScanSettings();
            try
            {
                if (!StudyDatabase.IsReady) return s;
                Dictionary<string, string> d = StudyRepository.LoadSettings();

                s.CameraName = Str(d, "camera.name", s.CameraName);
                s.CameraWidth = Int(d, "camera.width", s.CameraWidth);
                s.CameraHeight = Int(d, "camera.height", s.CameraHeight);

                s.AutoCapture = Bool(d, "capture.auto", s.AutoCapture);
                s.StableFramesRequired = Int(d, "capture.stable_frames", s.StableFramesRequired);
                s.HoldStillMs = Int(d, "capture.hold_ms", s.HoldStillMs);
                s.CooldownSeconds = Dbl(d, "capture.cooldown_s", s.CooldownSeconds);
                s.MinSharpness = Dbl(d, "capture.min_sharpness", s.MinSharpness);
                s.MinBrightness = Dbl(d, "capture.min_brightness", s.MinBrightness);
                s.MaxBrightness = Dbl(d, "capture.max_brightness", s.MaxBrightness);
                s.FrontMaxAbsYaw = Dbl(d, "pose.front_max_yaw", s.FrontMaxAbsYaw);
                s.SideMinAbsYaw = Dbl(d, "pose.side_min_yaw", s.SideMinAbsYaw);
                s.ReviewBeforeFinish = Bool(d, "capture.review_before_finish", s.ReviewBeforeFinish);

                s.MinConfidence = Dbl(d, "ai.min_confidence", s.MinConfidence);
                s.MinMargin = Dbl(d, "ai.min_margin", s.MinMargin);
                s.MinConsistency = Dbl(d, "ai.min_consistency", s.MinConsistency);

                s.LiveGradCam = Bool(d, "gradcam.live", s.LiveGradCam);
                s.GradCamIntervalMs = Int(d, "gradcam.interval_ms", s.GradCamIntervalMs);
                s.MaxHeatmapAgeSeconds = Dbl(d, "gradcam.max_age_s", s.MaxHeatmapAgeSeconds);
                s.OverlayOpacity = Dbl(d, "gradcam.overlay_opacity", s.OverlayOpacity);
                s.GradCamTimeoutSeconds = Int(d, "gradcam.timeout_s", s.GradCamTimeoutSeconds);
                s.GradCamTargetLayer = Str(d, "gradcam.target_layer", s.GradCamTargetLayer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ScanSettings] " + ex.Message);
            }
            return s;
        }

        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>
            {
                { "camera.name", CameraName ?? "" },
                { "camera.width", F(CameraWidth) },
                { "camera.height", F(CameraHeight) },
                { "capture.auto", F(AutoCapture) },
                { "capture.stable_frames", F(StableFramesRequired) },
                { "capture.hold_ms", F(HoldStillMs) },
                { "capture.cooldown_s", F(CooldownSeconds) },
                { "capture.min_sharpness", F(MinSharpness) },
                { "capture.min_brightness", F(MinBrightness) },
                { "capture.max_brightness", F(MaxBrightness) },
                { "pose.front_max_yaw", F(FrontMaxAbsYaw) },
                { "pose.side_min_yaw", F(SideMinAbsYaw) },
                { "capture.review_before_finish", F(ReviewBeforeFinish) },
                { "ai.min_confidence", F(MinConfidence) },
                { "ai.min_margin", F(MinMargin) },
                { "ai.min_consistency", F(MinConsistency) },
                { "gradcam.live", F(LiveGradCam) },
                { "gradcam.interval_ms", F(GradCamIntervalMs) },
                { "gradcam.max_age_s", F(MaxHeatmapAgeSeconds) },
                { "gradcam.overlay_opacity", F(OverlayOpacity) },
                { "gradcam.timeout_s", F(GradCamTimeoutSeconds) },
                { "gradcam.target_layer", GradCamTargetLayer ?? "" }
            };
        }

        /// <summary>Save, audit and make these the current settings. Returns how many values changed.</summary>
        public int Save()
        {
            int n = StudyRepository.SaveSettings(ToDictionary(), Load().ToDictionary());
            _current = this;
            ApplyGlobal();
            return n;
        }

        /// <summary>Settings that are global rather than per camera session.</summary>
        public void ApplyGlobal()
        {
            ClassPalette.AlphaMax = Clamp(OverlayOpacity, 0.05, 1.0);
            GradCamServiceConfig.TimeoutSeconds = Math.Max(5, GradCamTimeoutSeconds);
            GradCamServiceConfig.TargetLayer = string.IsNullOrWhiteSpace(GradCamTargetLayer) ? null : GradCamTargetLayer.Trim();
        }

        /// <summary>Push the capture / stability / pose settings into a live controller before Start().</summary>
        public void Apply(LiveGradCamController live)
        {
            if (live == null) return;
            ApplyGlobal();
            live.ApplySettings(
                stableFrames: Math.Max(1, StableFramesRequired),
                holdStillMs: Math.Max(0, HoldStillMs),
                cooldownSeconds: Math.Max(0.5, CooldownSeconds),
                minConfidence: (float)Clamp(MinConfidence, 0.25, 0.99),
                minMargin: (float)Clamp(MinMargin, 0.0, 0.9),
                minConsistency: Clamp(MinConsistency, 0.0, 1.0),
                minSharpness: Math.Max(0, MinSharpness),
                minBrightness: Clamp(MinBrightness, 0, 255),
                maxBrightness: Clamp(MaxBrightness, 0, 255),
                frontMaxAbsYaw: Clamp(FrontMaxAbsYaw, 0.02, 0.5),
                sideMinAbsYaw: Clamp(SideMinAbsYaw, 0.05, 0.8));

            live.AutoCaptureEnabled = AutoCapture;
            live.Enabled = LiveGradCam;
            live.MinGradCamIntervalMs = Math.Max(0, GradCamIntervalMs);
            live.MaxHeatmapAgeSeconds = Math.Max(1.0, MaxHeatmapAgeSeconds);
        }

        // ------------------------------------------------------------------ parse

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string F(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string F(bool v) => v ? "true" : "false";

        private static string Str(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v != null ? v : def;
        }

        private static int Int(Dictionary<string, string> d, string k, int def)
        {
            string v; int r;
            return d.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static double Dbl(Dictionary<string, string> d, string k, double def)
        {
            string v; double r;
            return d.TryGetValue(k, out v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static bool Bool(Dictionary<string, string> d, string k, bool def)
        {
            string v; bool r;
            return d.TryGetValue(k, out v) && bool.TryParse(v, out r) ? r : def;
        }
    }
}
