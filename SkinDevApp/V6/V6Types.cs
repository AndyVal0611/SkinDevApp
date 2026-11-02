// ============================================================================
// V6Types.cs  -  namespace SkinDevApp.V6
//
// Data types of the V6 unified assessment (Global MobileNetV2 + three YOLOv8 ensembles -> trained `global_yolo` fusion).
// Pure data: no WPF, no database, no OpenCV, no ONNX Runtime. See v6/deployment/V6_EVIDENCE_SCHEMA_SPEC.md.
//
// EXPERIMENTAL, development-stage. Every assessment is PENDING_DERMATOLOGIST_REVIEW and is never a clinical validation.
// The V5 classifier is not touched by anything in this folder.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.V6
{
    public static class V6Conditions
    {
        /// <summary>Order used by the fusion model, the calibration and the evidence vector.</summary>
        public static readonly string[] Names = { "Acne", "Hyperpigmentation", "Eczema" };
        public const int Count = 3;
        public static int IndexOf(string name) => Array.IndexOf(Names, name);
    }

    public static class V6States
    {
        public const string FindingsSupported = "FINDINGS_SUPPORTED";
        public const string MultipleFindingsSupported = "MULTIPLE_FINDINGS_SUPPORTED";
        public const string NoFindingsSupported = "NO_FINDINGS_SUPPORTED";
        public const string UncertainReviewRequired = "UNCERTAIN_REVIEW_REQUIRED";
        public const string RecaptureRequired = "RECAPTURE_REQUIRED";
        public const string OutOfScopeReviewRequired = "OUT_OF_SCOPE_REVIEW_REQUIRED";
        public const string InferenceError = "INFERENCE_ERROR";

        public const string ReviewStatusPending = "PENDING_DERMATOLOGIST_REVIEW";
    }

    public enum ConditionStatus { Present, Absent, Uncertain }

    /// <summary>One detector box in pixels of the analysed frame (candidate pool, confidence &gt;= pool minimum).</summary>
    public sealed class PixelBox
    {
        public double Conf;
        public double X0, Y0, X1, Y1;
    }

    /// <summary>Box reduced to the numbers the fusion uses (identical to the training cache after rounding).</summary>
    public sealed class EvidenceBox
    {
        public double Conf;
        public bool Deployed;
        public double RelArea;      // box area / (frame W * H), 6 decimals
        public double Cx, Cy;       // centre as a fraction of the frame, 4 decimals
    }

    /// <summary>Detector output for one condition. Available=false means the detector is missing or failed: that is NOT "no detection".</summary>
    public sealed class ConditionPool
    {
        public string Condition;
        public bool Available;
        public List<EvidenceBox> Boxes = new List<EvidenceBox>();
        public List<PixelBox> PixelBoxes = new List<PixelBox>();      // same boxes in frame pixels (for drawing); empty when built from derived values
        public double DeployedThreshold;
        public string Error;
    }

    /// <summary>A box shown as supporting localisation (deployed boxes only). Never a lesion count and never a diagnosis.</summary>
    public sealed class V6Box
    {
        public string Condition;
        public float Confidence;
        public float X0, Y0, X1, Y1;
        public bool Deployed;
    }

    /// <summary>What the capture/quality layer tells the V6 service. Normal can only be asserted when Assessed is true and nothing blocks.</summary>
    public sealed class QualityInput
    {
        public bool Assessed;
        public List<string> Blocking = new List<string>();
        public List<string> Warnings = new List<string>();
        /// <summary>What was and was not checked (e.g. "resolution, sharpness, exposure, face; glare/white balance/compression not checked").</summary>
        public string Coverage = "";

        public static QualityInput Ok() => new QualityInput { Assessed = true };
        public static QualityInput NotAssessed() => new QualityInput { Assessed = false };
    }

    public sealed class ConditionAssessment
    {
        public string Condition;
        public double FusionLogit;
        public double CalibratedProbability;
        public double Threshold;
        public ConditionStatus Status;
        public bool AbsenceSupported;           // 'Absent' counts only when this condition's detector was available
        public bool DetectorAvailable;
        public bool DetectorFired;              // at least one deployed box (information, never used as the decision)
        public double GlobalOnlyProbability;    // calibrated Global-only fusion, used only for the conflict flag

        /// <summary>Wording-safe verdict: an 'Absent' without detector coverage is reported as uncertain.</summary>
        public string Verdict =>
            Status == ConditionStatus.Present ? "present" :
            Status == ConditionStatus.Absent && AbsenceSupported ? "absent" : "uncertain";
    }

    /// <summary>The one structured V6 result.</summary>
    public sealed class UnifiedAssessment
    {
        public string State = V6States.InferenceError;
        public ConditionAssessment[] Conditions = new ConditionAssessment[0];
        public List<string> Flags = new List<string>();
        public List<string> Reasons = new List<string>();
        public List<V6Box> Boxes = new List<V6Box>();

        /// <summary>Normal is derived, never a model output: true only for NO_FINDINGS_SUPPORTED.</summary>
        public bool NormalSupported => State == V6States.NoFindingsSupported;

        public bool ReviewRequired =>
            State == V6States.UncertainReviewRequired || State == V6States.RecaptureRequired ||
            State == V6States.OutOfScopeReviewRequired || State == V6States.InferenceError;

        public string ReviewStatus = V6States.ReviewStatusPending;
        public bool RequiresDermatologistReview = true;
        public bool ClinicalValidation = false;

        public int FrameWidth, FrameHeight;
        public double OodScore;
        public bool OodFlag;
        public double InferenceMs;
        public string RunId, FeatureSchemaVersion, Variant = "global_yolo";
        public Dictionary<string, string> ModelSha256 = new Dictionary<string, string>();
        public string Error;

        public IEnumerable<string> PresentConditions => Conditions.Where(c => c.Status == ConditionStatus.Present).Select(c => c.Condition);

        public static UnifiedAssessment Failed(string error, V6Config cfg = null)
        {
            var a = new UnifiedAssessment { State = V6States.InferenceError, Error = error };
            a.Reasons.Add("error: " + error);
            if (cfg != null) { a.RunId = cfg.RunId; a.FeatureSchemaVersion = cfg.FeatureSchemaVersion; }
            return a;
        }
    }
}
