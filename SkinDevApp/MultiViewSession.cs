// ============================================================================
// MultiViewSession.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// One multi-view scan: Front -> Left -> Right captures that share a session id,
// plus the fusion of the three per-view results.
//
// Folder layout (inside the existing capture archive):
//
//   Captures\yyyy-MM-dd\Session_HHmmss_<patient>_<id8>\
//       session.json                    session index + fusion + per-view summary
//       Front\  original.png analysed.png model_input_224.png
//               heatmap_<Class>.png overlay_<Class>.png camraw_<Class>.npy
//               overlay_all.png record.json (review.json later)
//       Left\   (same files)
//       Right\  (same files)
//
// FUSION (ViewFusion) - what it is and what it is not
//   Each view yields the deployed ONNX model's four class scores. The overall
//   scan score is a QUALITY-WEIGHTED LINEAR OPINION POOL:
//
//       pooled_k = sum_v w_v * p_vk / sum_v w_v          w_v = view quality (0..1)
//
//   Quality comes from image sharpness, exposure and whether the pose gate
//   verified the angle. It deliberately does NOT use the model's own confidence
//   as a weight: a confident view already has a peaked p_v, and weighting by
//   confidence again would count it twice.
//
//   A pool dilutes anything seen from one side only, so the fused result is
//   never shown alone. Always reported next to it:
//     - every view's own scores and top class (never discarded),
//     - the unweighted mean (shows how much the weights matter),
//     - the per-class MAXIMUM over views and which view it came from,
//     - a disagreement flag (top classes differ, or a class's score spreads by
//       >= DisagreementSpread across views),
//     - "side-specific" notes: a non-Normal class >= SideSpecificMin in one
//       view while the pool stays below it.
//
//   This is a pre-specified heuristic. It has NOT been validated on labelled
//   multi-view data; compare pooled / mean / max against reviewer labels
//   (review.json) before relying on any one of them.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkinDevApp.AI;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    // ---------------------------------------------------------------- DTOs ---

    public sealed class ViewOutcome
    {
        [JsonPropertyName("view")] public string View { get; set; }
        [JsonPropertyName("folder")] public string Folder { get; set; }              // relative to the session folder
        [JsonPropertyName("capture_id")] public string CaptureId { get; set; }
        [JsonPropertyName("frame_id")] public long FrameId { get; set; }
        [JsonPropertyName("captured_utc")] public string CapturedUtc { get; set; }
        [JsonPropertyName("trigger")] public string Trigger { get; set; }
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; }
        [JsonPropertyName("scores_percent")] public Dictionary<string, double> ScoresPercent { get; set; } = new Dictionary<string, double>();
        [JsonPropertyName("predicted_class")] public string PredictedClass { get; set; }
        [JsonPropertyName("confidence_percent")] public double ConfidencePercent { get; set; }
        [JsonPropertyName("quality_weight")] public double QualityWeight { get; set; }
        [JsonPropertyName("pose_gate_verified")] public bool PoseGateVerified { get; set; }
        [JsonPropertyName("yaw_ratio")] public double? YawRatio { get; set; }
        [JsonPropertyName("has_gradcam_maps")] public bool HasGradCamMaps { get; set; }
        [JsonPropertyName("class_map_similarity")] public string ClassMapSimilarityLevel { get; set; }

        [JsonIgnore] public float[] Probabilities { get; set; } = new float[ClassPalette.ClassCount];
        [JsonIgnore] public string AbsoluteFolder { get; set; }
    }

    public sealed class SideSpecificNote
    {
        [JsonPropertyName("class")] public string Class { get; set; }
        [JsonPropertyName("view")] public string View { get; set; }
        [JsonPropertyName("view_score_percent")] public double ViewScorePercent { get; set; }
        [JsonPropertyName("pooled_score_percent")] public double PooledScorePercent { get; set; }
    }

    public sealed class FusedResult
    {
        [JsonPropertyName("method")] public string Method { get; set; } =
            "Quality-weighted linear opinion pool of per-view ONNX class scores. Heuristic, not validated on labelled multi-view data. Per-view results are always kept.";
        [JsonPropertyName("views_used")] public int ViewsUsed { get; set; }
        [JsonPropertyName("views_expected")] public int ViewsExpected { get; set; } = 3;
        [JsonPropertyName("weights")] public Dictionary<string, double> Weights { get; set; } = new Dictionary<string, double>();
        [JsonPropertyName("pooled_percent")] public Dictionary<string, double> PooledPercent { get; set; } = new Dictionary<string, double>();
        [JsonPropertyName("unweighted_mean_percent")] public Dictionary<string, double> MeanPercent { get; set; } = new Dictionary<string, double>();
        [JsonPropertyName("max_over_views_percent")] public Dictionary<string, double> MaxPercent { get; set; } = new Dictionary<string, double>();
        [JsonPropertyName("max_over_views_from")] public Dictionary<string, string> MaxFromView { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("top_class")] public string TopClass { get; set; }
        [JsonPropertyName("top_class_percent")] public double TopClassPercent { get; set; }
        [JsonPropertyName("per_view_top_class")] public Dictionary<string, string> PerViewTopClass { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("views_agree")] public bool ViewsAgree { get; set; }
        [JsonPropertyName("disagreement")] public bool Disagreement { get; set; }
        [JsonPropertyName("max_class_spread_points")] public double MaxClassSpreadPoints { get; set; }
        [JsonPropertyName("side_specific")] public List<SideSpecificNote> SideSpecific { get; set; } = new List<SideSpecificNote>();
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new List<string>();

        [JsonIgnore] public float[] Pooled { get; set; } = new float[ClassPalette.ClassCount];
    }

    // -------------------------------------------------------------- fusion ---

    public static class ViewFusion
    {
        /// <summary>Spread (max-min over views, percentage points) that flags disagreement.</summary>
        public const double DisagreementSpread = 30.0;

        /// <summary>A non-Normal class at/above this in one view is called out even if the pool is lower.</summary>
        public const double SideSpecificMin = 50.0;

        /// <summary>View quality 0.05..1 from sharpness, exposure and pose verification.</summary>
        public static double QualityWeight(FrameQuality q, bool poseVerified)
        {
            if (q == null) return poseVerified ? 0.7 : 0.5;

            double sharp = Math.Min(1.0, q.Sharpness / 100.0);               // 100 ~ 4x the gate minimum
            double expo = 1.0 - Math.Min(1.0, 3.0 * (q.UnderExposedFraction + q.OverExposedFraction));
            double pose = poseVerified ? 1.0 : 0.7;
            return Math.Max(0.05, Math.Round(sharp * expo * pose, 3));
        }

        public static FusedResult Fuse(IList<ViewOutcome> views, int viewsExpected = 3)
        {
            var f = new FusedResult { ViewsExpected = viewsExpected };
            List<ViewOutcome> ok = (views ?? new List<ViewOutcome>())
                .Where(v => v != null && v.Ok && v.Probabilities != null && v.Probabilities.Length >= ClassPalette.ClassCount)
                .ToList();

            f.ViewsUsed = ok.Count;
            if (ok.Count == 0)
            {
                f.Notes.Add("No view produced a usable result.");
                return f;
            }

            double wSum = ok.Sum(v => Math.Max(0.0, v.QualityWeight));
            if (wSum <= 0.0) wSum = ok.Count;

            int K = ClassPalette.ClassCount;
            var pooled = new double[K];
            var mean = new double[K];
            var max = new double[K];
            var maxFrom = new string[K];

            foreach (ViewOutcome v in ok)
            {
                double w = v.QualityWeight > 0 ? v.QualityWeight : 1.0 / ok.Count;
                f.Weights[v.View] = Math.Round(w / (wSum > 0 ? wSum : 1.0), 4);
                for (int k = 0; k < K; k++)
                {
                    double p = v.Probabilities[k] * 100.0;
                    pooled[k] += w * p / wSum;
                    mean[k] += p / ok.Count;
                    if (p > max[k]) { max[k] = p; maxFrom[k] = v.View; }
                }
            }

            int top = 0;
            for (int k = 0; k < K; k++)
            {
                string name = ClassPalette.Names[k];
                f.PooledPercent[name] = Math.Round(pooled[k], 2);
                f.MeanPercent[name] = Math.Round(mean[k], 2);
                f.MaxPercent[name] = Math.Round(max[k], 2);
                f.MaxFromView[name] = maxFrom[k];
                f.Pooled[k] = (float)(pooled[k] / 100.0);
                if (pooled[k] > pooled[top]) top = k;
            }
            f.TopClass = ClassPalette.Names[top];
            f.TopClassPercent = Math.Round(pooled[top], 2);

            foreach (ViewOutcome v in ok) f.PerViewTopClass[v.View] = v.PredictedClass;
            f.ViewsAgree = f.PerViewTopClass.Values.Distinct().Count() == 1;

            double spread = 0.0;
            for (int k = 0; k < K; k++)
            {
                double lo = ok.Min(v => v.Probabilities[k]) * 100.0;
                double hi = ok.Max(v => v.Probabilities[k]) * 100.0;
                spread = Math.Max(spread, hi - lo);
            }
            f.MaxClassSpreadPoints = Math.Round(spread, 1);
            f.Disagreement = ok.Count > 1 && (!f.ViewsAgree || spread >= DisagreementSpread);

            int normalIdx = Array.IndexOf(ClassPalette.Names, "Normal");
            for (int k = 0; k < K; k++)
            {
                if (k == normalIdx) continue;
                if (max[k] >= SideSpecificMin && pooled[k] < SideSpecificMin && ok.Count > 1)
                {
                    f.SideSpecific.Add(new SideSpecificNote
                    {
                        Class = ClassPalette.Names[k],
                        View = maxFrom[k],
                        ViewScorePercent = Math.Round(max[k], 1),
                        PooledScorePercent = Math.Round(pooled[k], 1)
                    });
                }
            }

            if (ok.Count < viewsExpected)
                f.Notes.Add("Only " + ok.Count + " of " + viewsExpected + " views are available: the fused result is partial.");
            if (f.Disagreement)
                f.Notes.Add(f.ViewsAgree
                    ? "Views agree on the top class but a class score differs by " + f.MaxClassSpreadPoints.ToString("0") + " points between views."
                    : "Views disagree on the top class. Check each view before relying on the overall result.");
            foreach (SideSpecificNote n in f.SideSpecific)
                f.Notes.Add(n.Class + " scores " + n.ViewScorePercent.ToString("0") + "% in the " + n.View
                    + " view but only " + n.PooledScorePercent.ToString("0") + "% overall: the overall pool dilutes a one-sided finding.");

            return f;
        }
    }

    // ------------------------------------------------------------- session ---

    public sealed class SessionIndex
    {
        [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
        [JsonPropertyName("session_id")] public string SessionId { get; set; }
        [JsonPropertyName("patient_id")] public string PatientId { get; set; }
        [JsonPropertyName("started_utc")] public string StartedUtc { get; set; }
        [JsonPropertyName("updated_utc")] public string UpdatedUtc { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }              // in_progress | complete | incomplete
        [JsonPropertyName("view_naming")] public string ViewNaming { get; set; } =
            "Left = left side of the patient's face shown to the camera; Right = right side shown.";
        [JsonPropertyName("views")] public List<ViewOutcome> Views { get; set; } = new List<ViewOutcome>();
        [JsonPropertyName("fusion")] public FusedResult Fusion { get; set; }
        [JsonPropertyName("attribution_note")] public string AttributionNote { get; set; } =
            "Grad-CAM++ images are model attribution maps, not lesion segmentation or maps of diseased tissue.";
        [JsonPropertyName("review_note")] public string ReviewNote { get; set; } =
            "Pending Review: AI output is not ground truth and is never added to training data automatically.";
    }

    public sealed class MultiViewSession
    {
        private static readonly JsonSerializerOptions JsonOut = new JsonSerializerOptions { WriteIndented = true };

        public string SessionId { get; }
        public string PatientId { get; }
        public DateTime StartedUtc { get; }
        public string Folder { get; }

        public List<ViewOutcome> Outcomes { get; } = new List<ViewOutcome>();

        /// <summary>The view the patient is being guided through now (Any when the sequence is finished).</summary>
        public ScanView Current { get; private set; } = ScanView.Front;

        public bool IsComplete => Current == ScanView.Any;
        public FusedResult Fusion { get; private set; }

        public MultiViewSession(string patientId)
        {
            SessionId = Guid.NewGuid().ToString("N");
            PatientId = string.IsNullOrWhiteSpace(patientId) ? "unassigned" : patientId.Trim();
            StartedUtc = DateTime.UtcNow;

            DateTime local = StartedUtc.ToLocalTime();
            string name = "Session_" + local.ToString("HHmmss") + "_" + Slug(PatientId) + "_" + SessionId.Substring(0, 8);
            Folder = Path.Combine(CaptureArchive.RootDirectory, local.ToString("yyyy-MM-dd"), name);
            Directory.CreateDirectory(Folder);
            SaveIndex("in_progress");
        }

        private static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                if (sb.Length >= 24) break;
            }
            string r = sb.ToString().Trim('-');
            return r.Length == 0 ? "patient" : r;
        }

        /// <summary>Stamp a capture request so the archive files it under this session and view.</summary>
        public void Stamp(CaptureRequest req, ScanView view)
        {
            req.SessionId = SessionId;
            req.SessionFolder = Folder;
            req.PatientId = PatientId;
            req.View = view;
        }

        /// <summary>
        /// Record the finished analysis of the current view and move to the first view that
        /// still has no usable result (a failed view is asked for again).
        /// Returns the next view (Any when every view has a result).
        /// </summary>
        public ScanView AddResult(ScanResult r)
        {
            ViewOutcome o = Summarise(r, Current);
            Outcomes.RemoveAll(x => x.View == o.View);       // a re-taken view replaces the earlier one
            Outcomes.Add(o);

            Current = NextMissing();
            Fusion = Current == ScanView.Any ? ViewFusion.Fuse(Outcomes, ScanViews.Sequence.Length) : null;

            SaveIndex(IsComplete ? "complete" : "in_progress");
            return Current;
        }

        /// <summary>True when this view has a usable (ONNX-scored) capture.</summary>
        public bool HasView(ScanView v) => Outcomes.Any(x => x.View == ScanViews.Name(v) && x.Ok);

        private ScanView NextMissing()
        {
            foreach (ScanView v in ScanViews.Sequence)
                if (!HasView(v)) return v;
            return ScanView.Any;
        }

        /// <summary>
        /// Controlled retake of one view (Image Review): the next capture replaces it.
        /// Registration and the other views are kept.
        /// </summary>
        public void Retake(ScanView v)
        {
            if (Array.IndexOf(ScanViews.Sequence, v) < 0) return;
            Current = v;
            Fusion = null;
            SaveIndex("in_progress");
        }

        /// <summary>The patient stopped early: keep whatever was captured.</summary>
        public void Abort()
        {
            if (Outcomes.Count > 0) Fusion = ViewFusion.Fuse(Outcomes, ScanViews.Sequence.Length);
            SaveIndex(IsComplete ? "complete" : "incomplete");
        }

        private ViewOutcome Summarise(ScanResult r, ScanView view)
        {
            var o = new ViewOutcome { View = ScanViews.Name(view) };
            if (r == null) { o.Ok = false; o.Error = "no result"; return o; }

            o.Ok = r.Ok && r.Request != null && r.Request.Onnx != null;
            o.Error = r.Error;
            o.AbsoluteFolder = r.Folder;
            if (!string.IsNullOrEmpty(r.Folder) && r.Folder.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
                o.Folder = r.Folder.Substring(Folder.Length).TrimStart('\\', '/');

            if (r.Request != null)
            {
                o.CaptureId = r.Request.CaptureId;
                o.FrameId = r.Request.FrameId;
                o.CapturedUtc = r.Request.CapturedUtc.ToString("o");
                o.Trigger = r.Request.Trigger;
                o.PoseGateVerified = r.Request.PoseVerified;
                if (r.Request.Pose != null && r.Request.Pose.FaceFound) o.YawRatio = Math.Round(r.Request.Pose.YawRatio, 3);
                o.QualityWeight = ViewFusion.QualityWeight(r.Request.Quality, r.Request.PoseVerified);

                PredictionResult onnx = r.Request.Onnx;
                if (onnx != null)
                {
                    o.PredictedClass = onnx.PredictedClass;
                    o.ConfidencePercent = Math.Round(onnx.Confidence * 100.0, 2);
                    for (int k = 0; k < ClassPalette.ClassCount && k < onnx.Probabilities.Length; k++)
                    {
                        o.Probabilities[k] = onnx.Probabilities[k];
                        o.ScoresPercent[ClassPalette.Names[k]] = Math.Round(onnx.Probabilities[k] * 100.0, 3);
                    }
                }
            }

            o.HasGradCamMaps = r.Maps != null;
            if (r.Maps != null && r.Maps.Similarity != null) o.ClassMapSimilarityLevel = r.Maps.Similarity.Level;
            return o;
        }

        public void SaveIndex(string status)
        {
            try
            {
                var idx = new SessionIndex
                {
                    SessionId = SessionId,
                    PatientId = PatientId,
                    StartedUtc = StartedUtc.ToString("o"),
                    UpdatedUtc = DateTime.UtcNow.ToString("o"),
                    Status = status,
                    Views = Outcomes.OrderBy(o => Array.IndexOf(ScanViews.Sequence, ScanViews.Parse(o.View))).ToList(),
                    Fusion = Fusion
                };
                File.WriteAllText(Path.Combine(Folder, "session.json"), JsonSerializer.Serialize(idx, JsonOut));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SESSION] " + ex.Message);
            }
        }

        public static SessionIndex Load(string sessionFolder)
        {
            string p = Path.Combine(sessionFolder, "session.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<SessionIndex>(File.ReadAllText(p)) : null;
        }
    }
}
