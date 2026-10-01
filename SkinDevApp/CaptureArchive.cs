// ============================================================================
// CaptureArchive.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// Writes one folder per capture:
//
//   %LOCALAPPDATA%\LUMYVUE\Captures\yyyy-MM-dd\HHmmss_<id8>\
//       original.png            exact frame as captured (full resolution)
//       analysed.png            the frame that was analysed (working width)
//       model_input_224.png     the 224x224 image the classifier actually saw
//       heatmap_<Class>.png     raw 8-bit Grad-CAM++ map, one per class
//       overlay_<Class>.png     class-coloured overlay, one per class
//       overlay_all.png         all four class maps together
//       record.json             scores, model hash, stability + quality metrics,
//                               region metadata, setup profile, notes
//       review.json             written later by a reviewer (PendingReview -> Reviewed)
//
// Captures are NEVER copied into a training set automatically.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    public static class CaptureArchive
    {
        public static string RootDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LUMYVUE", "Captures");

        /// <summary>Set to match how the camera/driver delivers frames. See RegionAttribution.</summary>
        public static bool CameraMirrored { get; set; } = false;

        /// <summary>Describes any cropping done before analysis (saved in record.json).</summary>
        public static string FrameCropDescription { get; set; } = "none";

        private static readonly JsonSerializerOptions JsonOut = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static double Pct(float p) { return Math.Round(p * 100.0, 3); }

        private static Dictionary<string, double> ToPercentDict(float[] probs)
        {
            var d = new Dictionary<string, double>();
            for (int i = 0; i < ClassPalette.Names.Length; i++)
                d[ClassPalette.Names[i]] = probs != null && i < probs.Length ? Pct(probs[i]) : 0.0;
            return d;
        }

        private static void WritePng(string path, Mat m)
        {
            // ImEncode + WriteAllBytes: Cv2.ImWrite breaks on non-ASCII Windows user names.
            byte[] bytes = m.ImEncode(".png");
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// Save a capture. Heavy (disk + PNG encoding): call from Task.Run.
        /// <paramref name="maps"/> may be null if the Grad-CAM service was unavailable;
        /// the capture is still saved, with a note explaining why.
        /// </summary>
        public static string Save(
            CaptureRequest req,
            ClassHeatmapSet maps,
            string gradcamError,
            out CaptureRecord record)
        {
            string day = req.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd");
            string stamp = req.CapturedUtc.ToLocalTime().ToString("HHmmss") + "_" + req.CaptureId.Substring(0, 8);
            string folder = Path.Combine(RootDirectory, day, stamp);
            Directory.CreateDirectory(folder);

            var files = new Dictionary<string, string>();

            // ---- images ------------------------------------------------------
            if (req.Original != null && !req.Original.Empty())
            {
                WritePng(Path.Combine(folder, "original.png"), req.Original);
                files["original"] = "original.png";
            }

            if (req.Working != null && !req.Working.Empty())
            {
                WritePng(Path.Combine(folder, "analysed.png"), req.Working);
                files["analysed"] = "analysed.png";

                using (Mat input224 = new Mat())
                {
                    Cv2.Resize(req.Working, input224, new OpenCvSharp.Size(224, 224),
                               interpolation: InterpolationFlags.Linear);
                    WritePng(Path.Combine(folder, "model_input_224.png"), input224);
                    files["model_input_224"] = "model_input_224.png";
                }
            }

            // ---- record ------------------------------------------------------
            ModelInfo mi = ModelInfo.Current;
            PredictionResult onnx = req.Onnx;
            StabilizedResult st = req.Stable;

            record = new CaptureRecord
            {
                CaptureId = req.CaptureId,
                TimestampUtc = req.CapturedUtc.ToString("o"),
                Trigger = req.Trigger,
                Setup = SetupProfile.Load(),
                Model = new ModelSection
                {
                    RunName = mi.RunName,
                    OnnxSha256 = mi.OnnxSha256,
                    KerasSha256 = mi.KerasSha256,
                    ServiceSha256 = mi.ServiceSha256,
                    ServiceVersionReported = maps != null ? maps.ServiceVersion : mi.ServiceVersion,
                    Source = mi.Source,
                    Classes = ClassPalette.Names
                },
                Input = new InputSection
                {
                    OriginalWidth = req.Original != null ? req.Original.Width : 0,
                    OriginalHeight = req.Original != null ? req.Original.Height : 0,
                    AnalysedWidth = req.Working != null ? req.Working.Width : 0,
                    AnalysedHeight = req.Working != null ? req.Working.Height : 0,
                    FrameCrop = FrameCropDescription,
                    CameraMirrored = CameraMirrored
                },
                Scores = new ScoresSection
                {
                    OnnxPercent = ToPercentDict(onnx != null ? onnx.Probabilities : null),
                    KerasPercent = maps != null ? ToPercentDict(maps.Probabilities) : null,
                    SmoothedPercent = st != null ? ToPercentDict(st.SmoothedProbabilities) : null
                }
            };

            // primary + runner-up
            if (onnx != null)
            {
                float[] sm = (st != null && st.SmoothedProbabilities != null && st.SmoothedProbabilities.Length == 4)
                    ? st.SmoothedProbabilities : onnx.Probabilities;

                var order = Enumerable.Range(0, sm.Length).OrderByDescending(i => sm[i]).ToArray();

                record.Primary = new PrimarySection
                {
                    OnnxTopClass = onnx.PredictedClass,
                    OnnxConfidencePercent = Pct(onnx.Confidence),
                    DisplayedClass = st != null && !string.IsNullOrEmpty(st.DisplayClass)
                        ? st.DisplayClass : ClassPalette.Names[order[0]],
                    DisplayedConfidencePercent = Pct(sm[order[0]]),
                    RunnerUpClass = ClassPalette.Names[order[1]],
                    MarginPercent = Pct(sm[order[0]] - sm[order[1]])
                };
            }

            // ONNX vs Keras consistency (kiosk-side check of the service's second opinion)
            record.Consistency = new ConsistencySection { KerasAvailable = maps != null };
            if (maps != null && onnx != null)
            {
                double maxDiff = 0.0;
                for (int i = 0; i < 4; i++)
                    maxDiff = Math.Max(maxDiff, Math.Abs(onnx.Probabilities[i] - maps.Probabilities[i]));

                record.Consistency.MaxAbsDifference = Math.Round(maxDiff, 6);
                record.Consistency.SameTopClass = onnx.PredictedIndex == maps.PredictedIndex;

                if (onnx.PredictedIndex != maps.PredictedIndex)
                    record.Consistency.Warning =
                        "ONNX and Keras disagree on the top class for this frame. Treat the result with caution.";
                else if (maxDiff > 0.02)
                    record.Consistency.Warning =
                        "ONNX and Keras differ by more than 2 percentage points on at least one class.";
            }

            if (req.Stability != null)
            {
                StabilityMetrics sm2 = req.Stability;
                record.Stability = new StabilitySection
                {
                    Trigger = sm2.Trigger,
                    DisplayedClass = sm2.DisplayClass,
                    FramesHeldStable = sm2.FramesHeldStable,
                    StableFramesRequired = sm2.StableFramesRequired,
                    WindowTicks = sm2.WindowTicks,
                    Consistency = Math.Round(sm2.ConsistencyRatio, 3),
                    MotionMean = Math.Round(sm2.MotionMean, 3),
                    MotionMax = Math.Round(sm2.MotionMax, 3),
                    HoldMs = Math.Round(sm2.HoldMs, 0),
                    EmaAlpha = sm2.EmaAlpha,
                    ConfidenceThreshold = sm2.ConfidenceThreshold,
                    HysteresisMargin = sm2.HysteresisMargin
                };
            }

            if (req.Quality != null)
            {
                FrameQuality q = req.Quality;
                record.Quality = new QualitySection
                {
                    Sharpness = Math.Round(q.Sharpness, 2),
                    Brightness = Math.Round(q.Brightness, 1),
                    UnderExposed = Math.Round(q.UnderExposedFraction, 4),
                    OverExposed = Math.Round(q.OverExposedFraction, 4),
                    FaceFound = q.FaceFound,
                    FaceBox = q.FaceFound ? new[] { q.FaceX, q.FaceY, q.FaceW, q.FaceH } : null,
                    Guidance = q.GuidanceText
                };
            }

            // ---- Grad-CAM maps ----------------------------------------------
            if (maps != null)
            {
                record.GradCam["method"] = maps.Method;
                record.GradCam["service_version"] = maps.ServiceVersion;
                record.GradCam["exec_mode"] = maps.ExecMode;
                record.GradCam["service_latency_ms"] = maps.ServiceLatencyMs.ToString("0.0");
                record.GradCam["round_trip_ms"] = maps.RoundTripMs.ToString("0.0");
                record.GradCam["normalisation"] =
                    "per-class percentile map; compare shape, not brightness. relative_strength compares classes and is not a probability.";

                foreach (ClassMapInfo m in maps.Maps)
                {
                    string hm = "heatmap_" + m.Name + ".png";
                    string ov = "overlay_" + m.Name + ".png";

                    WritePng(Path.Combine(folder, hm), m.Heat8U);

                    if (req.Working != null && !req.Working.Empty())
                    {
                        using (Mat overlay = ClassHeatmapRenderer.RenderSingleClass(req.Working, maps, m.Index))
                            WritePng(Path.Combine(folder, ov), overlay);
                    }

                    record.ClassMaps.Add(new MapSection
                    {
                        Class = m.Name,
                        ProbabilityPercent = Pct(m.Probability),
                        RawPeak = m.RawPeak,
                        RelativeStrength = Math.Round(m.RelativeStrength, 4),
                        Diffuse = m.Diffuse || m.Region == null || m.Region.Diffuse,
                        TopZone = m.HasFocalRegion ? m.Region.TopZone : null,
                        TopZoneShare = m.Region != null ? Math.Round(m.Region.TopShare, 3) : 0.0,
                        ZoneShare = m.Region != null ? m.Region.ZoneShare : null,
                        HeatmapFile = hm,
                        OverlayFile = ov
                    });
                }

                if (req.Working != null && !req.Working.Empty())
                {
                    using (Mat all = ClassHeatmapRenderer.RenderOverlay(req.Working, maps, OverlayView.All, -1))
                        WritePng(Path.Combine(folder, "overlay_all.png"), all);
                    files["overlay_all"] = "overlay_all.png";
                }

                ClassMapInfo pm = maps.PredictedMap;
                record.Region = new RegionSection
                {
                    Summary = maps.RegionSummary(),
                    ClassExplained = pm != null ? pm.Name : null
                };
            }
            else
            {
                record.Notes.Add("Grad-CAM++ maps unavailable: " + (gradcamError ?? "service not reachable") +
                                 ". Scores come from the ONNX classifier only.");
            }

            record.Files = files;
            record.Notes.Add("Pending Review: this AI output is not ground truth and is never added to training data automatically.");
            record.Notes.Add("Screening aid only - not a diagnosis.");

            File.WriteAllText(Path.Combine(folder, "record.json"), JsonSerializer.Serialize(record, JsonOut));
            return folder;
        }
    }

    /// <summary>Folder-based Pending Review workflow (record.json + review.json).</summary>
    public static class ReviewStore
    {
        private static readonly JsonSerializerOptions JsonOut = new JsonSerializerOptions { WriteIndented = true };

        public static List<string> ListAll()
        {
            var result = new List<string>();
            string root = CaptureArchive.RootDirectory;
            if (!Directory.Exists(root)) return result;

            try
            {
                foreach (string day in Directory.GetDirectories(root))
                    foreach (string cap in Directory.GetDirectories(day))
                        if (File.Exists(Path.Combine(cap, "record.json")))
                            result.Add(cap);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ReviewStore] " + ex.Message);
            }

            result.Sort(StringComparer.Ordinal);
            result.Reverse();           // newest first
            return result;
        }

        public static List<string> ListPending()
        {
            return ListAll().Where(f => !File.Exists(Path.Combine(f, "review.json"))).ToList();
        }

        public static CaptureRecord LoadRecord(string folder)
        {
            string p = Path.Combine(folder, "record.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(p)) : null;
        }

        public static ReviewRecord LoadReview(string folder)
        {
            string p = Path.Combine(folder, "review.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<ReviewRecord>(File.ReadAllText(p)) : null;
        }

        public static void SaveReview(string folder, ReviewRecord review)
        {
            review.ReviewedUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(folder, "review.json"), JsonSerializer.Serialize(review, JsonOut));
        }
    }
}
