// ============================================================================
// StudyModels.cs  -  namespace SkinDevApp.Data
//
// Plain rows for the study database (StudyDatabase.cs). One class per table.
//
//   Participant -> many ScanSessions -> Front/Left/Right CaptureViews
//   CaptureView -> Prediction (AnalysisResult) + ClassScores + images + 4 AttributionMaps
//   ScanSession -> ResearcherEvaluations and DermatologistValidations (append-only,
//                  never overwrite the AI output)
//   Participant -> FitzpatrickAssessments (manual / dermatologist-assigned only; there is no AI skin-tone model)
// ============================================================================

using System.Collections.Generic;

namespace SkinDevApp.Data
{
    public static class StudyText
    {
        public const string PrototypeNotice =
            "PrecisionSkin is a research prototype intended to assist with automated skin-image classification. " +
            "It does not replace professional medical evaluation and does not provide a diagnosis.";

        public const string ScoresNote =
            "Model Class Scores are the four-class softmax output of the deployed classifier. " +
            "They are not independent disease probabilities.";

        public const string AttributionNote =
            "Grad-CAM++ maps show model attribution (which image areas influenced each class score). " +
            "They are not lesion segmentation.";

        public static readonly string[] Classes = { "Acne", "Hyperpigmentation", "Eczema", "Normal" };
        public static readonly string[] Views = { "Front", "Left", "Right" };
    }

    public sealed class Participant
    {
        public string ParticipantID { get; set; }
        public int Seq { get; set; }
        public string RegisteredAt { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string Sex { get; set; }
        public string Contact { get; set; }
        public string OperatorID { get; set; }
        public string Status { get; set; } = "Active";
        public string UpdatedAt { get; set; }

        public string FullName
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(FirstName)) parts.Add(FirstName.Trim());
                if (!string.IsNullOrWhiteSpace(MiddleName)) parts.Add(MiddleName.Trim());
                if (!string.IsNullOrWhiteSpace(LastName)) parts.Add(LastName.Trim());
                return parts.Count == 0 ? "(name not stored)" : string.Join(" ", parts);
            }
        }
    }

    public sealed class SkinProfile
    {
        public string ParticipantID { get; set; }
        public string GeneralSkinType { get; set; }
        public string FitzpatrickManual { get; set; }        // I..VI or "Not collected"
        public string FitzpatrickSource { get; set; }        // Self-reported | Researcher-assessed | Not collected
        public string Sensitivity { get; set; }
        public string Concerns { get; set; }                 // comma-separated
        public string ConcernOther { get; set; }
        public string Regions { get; set; }                  // comma-separated
        public string RegionOther { get; set; }
        public string ConcernDuration { get; set; }
        public string Cleanser { get; set; }
        public string Moisturizer { get; set; }
        public string Sunscreen { get; set; }
        public string AcneTreatment { get; set; }
        public string EczemaTreatment { get; set; }
        public string PigmentationTreatment { get; set; }
        public string OtherProducts { get; set; }
        public string RecentProcedures { get; set; }
        public string OtherResponses { get; set; }
        public string UpdatedAt { get; set; }
    }

    public sealed class Consent
    {
        public long ConsentID { get; set; }
        public string ParticipantID { get; set; }
        public bool ResearchInfoUnderstood { get; set; }
        public bool VoluntaryParticipation { get; set; }
        public bool ImageCaptureConsent { get; set; }
        public bool ImageProcessingConsent { get; set; }
        public bool FutureModelUseConsent { get; set; }
        public string Decision { get; set; }                 // Agreed | Declined
        public string RecordedAt { get; set; }
        public string OperatorID { get; set; }

        /// <summary>Everything required before image acquisition may start.</summary>
        public bool AllowsScanning =>
            Decision == "Agreed" && ResearchInfoUnderstood && VoluntaryParticipation &&
            ImageCaptureConsent && ImageProcessingConsent;
    }

    public sealed class ModelVersion
    {
        public long ModelVersionID { get; set; }
        public string ModelName { get; set; }
        public string Architecture { get; set; }
        public string RunName { get; set; }
        public string OnnxSha256 { get; set; }
        public string KerasSha256 { get; set; }
        public string ServiceSha256 { get; set; }
        public string ServiceVersion { get; set; }
        public string CreatedUtc { get; set; }
        public string FirstSeenAt { get; set; }

        public string ShortLabel =>
            (string.IsNullOrEmpty(RunName) ? "unknown" : RunName) + " / " + Short(OnnxSha256);

        public static string Short(string h) =>
            string.IsNullOrEmpty(h) ? "unknown" : (h.Length > 12 ? h.Substring(0, 12) : h);
    }

    public sealed class ScanSessionRow
    {
        public string ScanSessionID { get; set; }
        public string SessionLabel { get; set; }
        public string ParticipantID { get; set; }
        public string LegacyPatientLabel { get; set; }
        public string StartedAt { get; set; }
        public string CompletedAt { get; set; }
        public string OverallPredictedClass { get; set; }
        public double? OverallScore { get; set; }
        public string FusionMethod { get; set; }
        public bool ViewDisagreement { get; set; }
        public int ViewsCompleted { get; set; }
        public int ViewsExpected { get; set; }
        public string ScanStatus { get; set; }
        public bool Finalized { get; set; }
        public long? ModelVersionID { get; set; }
        public string SessionFolder { get; set; }
        public string OperatorID { get; set; }
        public string FusionJson { get; set; }
        public string ScanMode { get; set; }                  // MultiView | Single

        // filled by queries that join
        public string ModelLabel { get; set; }
        public string ValidationStatus { get; set; }

        public string DateText => StartedAt != null && StartedAt.Length >= 10 ? StartedAt.Substring(0, 10) : StartedAt;
        public string TimeText => StartedAt != null && StartedAt.Length >= 16 ? StartedAt.Substring(11, 5) : "";
        public string ScoreText => OverallScore.HasValue ? OverallScore.Value.ToString("0.0") + "%" : "-";
        public string CompletionText => ViewsCompleted + " / " + ViewsExpected;
        public string DisplayId => string.IsNullOrEmpty(SessionLabel) ? ScanSessionID : SessionLabel;
    }

    public sealed class CaptureViewRow
    {
        public string CaptureID { get; set; }
        public string ScanSessionID { get; set; }
        public string ViewType { get; set; }
        public long? FrameID { get; set; }
        public string CapturedAt { get; set; }
        public string Trigger { get; set; }
        public string OriginalImagePath { get; set; }
        public string AnalysedImagePath { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public double? Sharpness { get; set; }
        public double? Brightness { get; set; }
        public double? UnderExposed { get; set; }
        public double? OverExposed { get; set; }
        public double? Motion { get; set; }
        public double? PoseYaw { get; set; }
        public double? PosePitch { get; set; }
        public double? PoseRoll { get; set; }
        public bool PoseVerified { get; set; }
        public bool FaceFound { get; set; }
        public double? StabilityScore { get; set; }
        public double? QualityWeight { get; set; }
        public string QualityStatus { get; set; }
        public string RecordFolder { get; set; }

        public ClassScoreRow Scores { get; set; }
        public List<AttributionMapRow> Maps { get; set; } = new List<AttributionMapRow>();

        /// <summary>Lesion localization (separate detector). Null for scans saved before the feature existed.</summary>
        public LocalizationRunRow Localization { get; set; }
        public List<LesionDetectionRow> Lesions { get; set; } = new List<LesionDetectionRow>();
    }

    public sealed class LocalizationRunRow
    {
        public string CaptureID { get; set; }
        /// <summary>OK | NotRun | Failed</summary>
        public string Status { get; set; }
        public string ErrorText { get; set; }
        public string ModelFile { get; set; }
        public string ModelSha256 { get; set; }
        public string ModelTag { get; set; }
        public int ImageSize { get; set; }
        public string ThresholdsJson { get; set; }
        public double LatencyMs { get; set; }
        public int BoxCount { get; set; }
        public string OverlayPath { get; set; }
        public string CombinedPath { get; set; }
    }

    public sealed class LesionDetectionRow
    {
        public string ClassName { get; set; }
        public int ClassIndex { get; set; }
        public double Confidence { get; set; }
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
    }

    public sealed class ClassScoreRow
    {
        public string CaptureID { get; set; }
        public double Acne { get; set; }
        public double Hyperpigmentation { get; set; }
        public double Eczema { get; set; }
        public double Normal { get; set; }
        public string PredictedClass { get; set; }
        public double Confidence { get; set; }

        public double[] AsArray() => new[] { Acne, Hyperpigmentation, Eczema, Normal };
    }

    public sealed class AttributionMapRow
    {
        public string CaptureID { get; set; }
        public string TargetClass { get; set; }
        public int TargetClassIndex { get; set; }
        public string TargetLayer { get; set; }
        public string RawMapPath { get; set; }
        public string RenderedMapPath { get; set; }
        public string OverlayPath { get; set; }
        public double RelativeStrength { get; set; }
        public bool Diffuse { get; set; }
        public string TopZone { get; set; }
        public string GradCAMVersion { get; set; }
        public string SimilarityDebugMetadata { get; set; }
    }

    public sealed class ResearcherEvaluation
    {
        public long EvaluationID { get; set; }
        public string ScanSessionID { get; set; }
        public string ResearcherID { get; set; }
        public string ResearcherClassification { get; set; }  // comma-separated when several labels
        public string AgreementWithAI { get; set; }           // Agree | Disagree | Partially agree | Uncertain
        public string Notes { get; set; }
        public string FrontComment { get; set; }
        public string LeftComment { get; set; }
        public string RightComment { get; set; }
        public string EvaluationDate { get; set; }
    }

    public sealed class DermatologistValidation
    {
        public long ValidationID { get; set; }
        public string ScanSessionID { get; set; }
        public string DermatologistID { get; set; }
        public string ValidatorName { get; set; }
        public string ProfessionalRole { get; set; } = "Licensed Dermatologist";
        public string CredentialReference { get; set; }
        public string DermatologistAssessment { get; set; }
        public string AgreementWithAI { get; set; }
        public string AgreementWithResearcher { get; set; }
        public string Notes { get; set; }
        public string LocalizationRelevance { get; set; }     // Relevant | Partially relevant | Not relevant | No localization available | Unable to assess
        public string GradCamUsefulness { get; set; }         // Useful | Partially useful | Not useful | Not interpretable | Not available
        public string FrontComment { get; set; }
        public string LeftComment { get; set; }
        public string RightComment { get; set; }
        public string ValidationDate { get; set; }
        public string ValidationStatus { get; set; }          // Pending | Completed
    }

    public sealed class AuditEntry
    {
        public long AuditID { get; set; }
        public string At { get; set; }
        public string Actor { get; set; }
        public string Action { get; set; }
        public string Entity { get; set; }
        public string EntityID { get; set; }
        public string Details { get; set; }
    }

    /// <summary>Everything the record / results / report screens need for one scan.</summary>
    public sealed class SessionDetail
    {
        public ScanSessionRow Session { get; set; }
        public Participant Participant { get; set; }
        public ModelVersion Model { get; set; }
        public List<CaptureViewRow> Views { get; set; } = new List<CaptureViewRow>();
        public List<ResearcherEvaluation> Evaluations { get; set; } = new List<ResearcherEvaluation>();
        public List<DermatologistValidation> Validations { get; set; } = new List<DermatologistValidation>();
        public Scanning.FusedResult Fusion { get; set; }

        public CaptureViewRow View(string name) =>
            Views.Find(v => string.Equals(v.ViewType, name, System.StringComparison.OrdinalIgnoreCase));
    }

    public sealed class CountRow
    {
        public string Label { get; set; }
        public int Count { get; set; }
    }
}
