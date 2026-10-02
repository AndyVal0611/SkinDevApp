// ============================================================================
// CaptureRecord.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// Everything saved with a capture (record.json), plus the in-memory request /
// result types that travel through the scan flow.
//
// Honest-metadata rules baked in:
//   * review_status starts as "PendingReview": the AI output is NOT ground truth
//   * training_use is "never_automatic": captures are never added to a training set
//   * scores are labelled "Model Class Scores" (softmax over four mutually
//     exclusive classes) - not independent disease probabilities
//   * nothing here is a diagnosis
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    // ------------------------------------------------------------------ travel

    /// <summary>The exact frame + the numbers that justified capturing it.</summary>
    public sealed class CaptureRequest : IDisposable
    {
        public string CaptureId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
        public string Trigger { get; set; } = "auto";      // auto | manual | upload
        public long FrameId { get; set; }

        /// <summary>Frame exactly as captured (full resolution, BGR). Saved as original.png.</summary>
        public Mat Original { get; set; }

        /// <summary>The frame that was analysed (resized to the working width).</summary>
        public Mat Working { get; set; }

        public PredictionResult Onnx { get; set; }
        public StabilizedResult Stable { get; set; }
        public FrameQuality Quality { get; set; }
        public StabilityMetrics Stability { get; set; }

        public void Dispose()
        {
            Original?.Dispose(); Original = null;
            Working?.Dispose(); Working = null;
        }
    }

    public sealed class ScanResult : IDisposable
    {
        public bool Ok { get; set; }
        public string Error { get; set; }

        /// <summary>True when the Grad-CAM service delivered the four class maps.</summary>
        public bool HasMaps => Maps != null;

        public CaptureRequest Request { get; set; }
        public ClassHeatmapSet Maps { get; set; }
        public CaptureRecord Record { get; set; }
        public string Folder { get; set; }

        public void Dispose()
        {
            Request?.Dispose(); Request = null;
            Maps?.Dispose(); Maps = null;
        }
    }

    // ------------------------------------------------------------- record.json

    public sealed class SetupProfile
    {
        [JsonPropertyName("kiosk_id")] public string KioskId { get; set; } = "unspecified";
        [JsonPropertyName("camera_model")] public string CameraModel { get; set; } = "unspecified";
        [JsonPropertyName("camera_resolution")] public string CameraResolution { get; set; } = "unspecified";
        [JsonPropertyName("camera_distance_cm")] public string CameraDistanceCm { get; set; } = "unspecified";
        [JsonPropertyName("camera_height_cm")] public string CameraHeightCm { get; set; } = "unspecified";
        [JsonPropertyName("chin_rest_marker")] public string ChinRestMarker { get; set; } = "unspecified";
        [JsonPropertyName("lighting")] public string Lighting { get; set; } = "unspecified";
        [JsonPropertyName("background")] public string Background { get; set; } = "unspecified";
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";

        /// <summary>
        /// Reads setup_profile.json next to the app (optional). See
        /// README_UPGRADE.md for the template. Missing file -> "unspecified".
        /// </summary>
        public static SetupProfile Load()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setup_profile.json");
                if (File.Exists(path))
                {
                    var p = JsonSerializer.Deserialize<SetupProfile>(File.ReadAllText(path));
                    if (p != null) return p;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SetupProfile] " + ex.Message);
            }
            return new SetupProfile();
        }
    }

    public sealed class ModelSection
    {
        [JsonPropertyName("run_name")] public string RunName { get; set; }
        [JsonPropertyName("onnx_sha256")] public string OnnxSha256 { get; set; }
        [JsonPropertyName("keras_sha256")] public string KerasSha256 { get; set; }
        [JsonPropertyName("service_sha256")] public string ServiceSha256 { get; set; }
        [JsonPropertyName("service_version_reported")] public string ServiceVersionReported { get; set; }
        [JsonPropertyName("model_version_source")] public string Source { get; set; }
        [JsonPropertyName("classes")] public string[] Classes { get; set; }
        [JsonPropertyName("preprocessing")] public string Preprocessing { get; set; } =
            "BGR->RGB, resize 224x224 bilinear, float32, (p/127.5)-1.0, NHWC";
    }

    public sealed class InputSection
    {
        [JsonPropertyName("original_width")] public int OriginalWidth { get; set; }
        [JsonPropertyName("original_height")] public int OriginalHeight { get; set; }
        [JsonPropertyName("analysed_width")] public int AnalysedWidth { get; set; }
        [JsonPropertyName("analysed_height")] public int AnalysedHeight { get; set; }
        [JsonPropertyName("model_input")] public string ModelInput { get; set; } = "224x224";
        [JsonPropertyName("frame_crop")] public string FrameCrop { get; set; } = "";
        [JsonPropertyName("camera_mirrored")] public bool CameraMirrored { get; set; }
    }

    public sealed class ScoresSection
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "Model Class Scores";
        [JsonPropertyName("note")] public string Note { get; set; } =
            "Softmax over four mutually exclusive classes. Not independent disease probabilities. Not a diagnosis.";
        [JsonPropertyName("onnx_percent")] public Dictionary<string, double> OnnxPercent { get; set; }
        [JsonPropertyName("keras_percent")] public Dictionary<string, double> KerasPercent { get; set; }
        [JsonPropertyName("smoothed_percent")] public Dictionary<string, double> SmoothedPercent { get; set; }
    }

    public sealed class PrimarySection
    {
        [JsonPropertyName("onnx_top_class")] public string OnnxTopClass { get; set; }
        [JsonPropertyName("onnx_confidence_percent")] public double OnnxConfidencePercent { get; set; }
        [JsonPropertyName("displayed_class")] public string DisplayedClass { get; set; }
        [JsonPropertyName("displayed_confidence_percent")] public double DisplayedConfidencePercent { get; set; }
        [JsonPropertyName("runner_up_class")] public string RunnerUpClass { get; set; }
        [JsonPropertyName("margin_percent")] public double MarginPercent { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } =
            "The model's top-scoring class for this image. It is a screening aid, not a diagnosis.";
    }

    public sealed class ConsistencySection
    {
        [JsonPropertyName("keras_available")] public bool KerasAvailable { get; set; }
        [JsonPropertyName("onnx_keras_same_top_class")] public bool? SameTopClass { get; set; }
        [JsonPropertyName("max_abs_probability_difference")] public double? MaxAbsDifference { get; set; }
        [JsonPropertyName("warning")] public string Warning { get; set; }
    }

    public sealed class StabilitySection
    {
        [JsonPropertyName("trigger")] public string Trigger { get; set; }
        [JsonPropertyName("displayed_class")] public string DisplayedClass { get; set; }
        [JsonPropertyName("frames_held_stable")] public int FramesHeldStable { get; set; }
        [JsonPropertyName("stable_frames_required")] public int StableFramesRequired { get; set; }
        [JsonPropertyName("window_ticks")] public int WindowTicks { get; set; }
        [JsonPropertyName("raw_prediction_consistency")] public double Consistency { get; set; }
        [JsonPropertyName("motion_mean")] public double MotionMean { get; set; }
        [JsonPropertyName("motion_max")] public double MotionMax { get; set; }
        [JsonPropertyName("hold_ms")] public double HoldMs { get; set; }
        [JsonPropertyName("ema_alpha")] public double EmaAlpha { get; set; }
        [JsonPropertyName("confidence_threshold")] public double ConfidenceThreshold { get; set; }
        [JsonPropertyName("hysteresis_margin")] public double HysteresisMargin { get; set; }
        [JsonPropertyName("meaning")] public string Meaning { get; set; } =
            "Shows the prediction was temporally consistent when captured. It does not show the prediction is correct.";
    }

    public sealed class QualitySection
    {
        [JsonPropertyName("sharpness_laplacian_var")] public double Sharpness { get; set; }
        [JsonPropertyName("brightness_mean")] public double Brightness { get; set; }
        [JsonPropertyName("under_exposed_fraction")] public double UnderExposed { get; set; }
        [JsonPropertyName("over_exposed_fraction")] public double OverExposed { get; set; }
        [JsonPropertyName("face_box_found")] public bool FaceFound { get; set; }
        [JsonPropertyName("face_box_normalised")] public double[] FaceBox { get; set; }
        [JsonPropertyName("guidance")] public string Guidance { get; set; }
    }

    public sealed class MapSection
    {
        [JsonPropertyName("class")] public string Class { get; set; }
        [JsonPropertyName("probability_percent")] public double ProbabilityPercent { get; set; }
        [JsonPropertyName("raw_peak")] public double RawPeak { get; set; }
        [JsonPropertyName("relative_strength")] public double RelativeStrength { get; set; }
        [JsonPropertyName("diffuse")] public bool Diffuse { get; set; }
        [JsonPropertyName("top_zone")] public string TopZone { get; set; }
        [JsonPropertyName("top_zone_share")] public double TopZoneShare { get; set; }
        [JsonPropertyName("zone_share")] public Dictionary<string, double> ZoneShare { get; set; }
        [JsonPropertyName("heatmap_file")] public string HeatmapFile { get; set; }
        [JsonPropertyName("overlay_file")] public string OverlayFile { get; set; }
        // v2.1 (additive): raw un-normalised CAM as a NumPy .npy (float32, H x W).
        [JsonPropertyName("raw_cam_file")] public string RawCamFile { get; set; }
        [JsonPropertyName("raw_cam_shape")] public int[] RawCamShape { get; set; }
    }

    public sealed class RegionSection
    {
        [JsonPropertyName("summary")] public string Summary { get; set; }
        [JsonPropertyName("class_explained")] public string ClassExplained { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } =
            "Approximate. A face box is used only as a reference frame. No region is forced when attribution is diffuse. Not lesion localisation.";
    }

    public sealed class CaptureRecord
    {
        [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = "1.0";
        [JsonPropertyName("capture_id")] public string CaptureId { get; set; }
        [JsonPropertyName("timestamp_utc")] public string TimestampUtc { get; set; }
        [JsonPropertyName("trigger")] public string Trigger { get; set; }
        [JsonPropertyName("review_status")] public string ReviewStatus { get; set; } = "PendingReview";
        [JsonPropertyName("training_use")] public string TrainingUse { get; set; } = "never_automatic";
        [JsonPropertyName("diagnostic_claim")] public string DiagnosticClaim { get; set; } = "none";

        [JsonPropertyName("model")] public ModelSection Model { get; set; }
        [JsonPropertyName("setup")] public SetupProfile Setup { get; set; }
        [JsonPropertyName("input")] public InputSection Input { get; set; }
        [JsonPropertyName("scores")] public ScoresSection Scores { get; set; }
        [JsonPropertyName("primary")] public PrimarySection Primary { get; set; }
        [JsonPropertyName("consistency")] public ConsistencySection Consistency { get; set; }
        [JsonPropertyName("stability")] public StabilitySection Stability { get; set; }
        [JsonPropertyName("quality")] public QualitySection Quality { get; set; }
        [JsonPropertyName("region")] public RegionSection Region { get; set; }
        [JsonPropertyName("class_maps")] public List<MapSection> ClassMaps { get; set; } = new List<MapSection>();
        // v2.1 (additive): how much the four class maps agree (raw CAMs). Null with an older service.
        [JsonPropertyName("class_map_similarity")] public ClassMapSimilarityDto ClassMapSimilarity { get; set; }
        [JsonPropertyName("gradcam")] public Dictionary<string, string> GradCam { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("files")] public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new List<string>();
    }

    public sealed class ReviewRecord
    {
        [JsonPropertyName("capture_id")] public string CaptureId { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "Reviewed";
        [JsonPropertyName("reviewed_utc")] public string ReviewedUtc { get; set; }
        [JsonPropertyName("reviewer")] public string Reviewer { get; set; }
        [JsonPropertyName("reviewer_label")] public string ReviewerLabel { get; set; }
        [JsonPropertyName("model_top_class")] public string ModelTopClass { get; set; }
        [JsonPropertyName("agrees_with_model")] public bool? AgreesWithModel { get; set; }
        [JsonPropertyName("comment")] public string Comment { get; set; }
        [JsonPropertyName("training_eligibility")] public string TrainingEligibility { get; set; } =
            "not_automatic - requires a separate, deliberate data-governance export";
    }
}
