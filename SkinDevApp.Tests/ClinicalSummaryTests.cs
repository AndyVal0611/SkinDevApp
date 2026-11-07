using System.Collections.Generic;
using System.Linq;
using SkinDevApp.Data;
using SkinDevApp.V6;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>
    /// The clinician-facing reading of the final model (ClinicalSummary) shared by every screen and printout: the three outcomes,
    /// per-condition status and certainty, facial regions from the marked areas, review reasons, and the scan-level reading.
    /// </summary>
    public class ClinicalSummaryTests
    {
        private static UnifiedAssessment Make(string state, double[] p, bool[] fired = null, string[] flags = null, int w = 100, int h = 100)
        {
            var a = new UnifiedAssessment { State = state, FrameWidth = w, FrameHeight = h };
            a.Conditions = V6Conditions.Names.Select((n, i) => new ConditionAssessment
            {
                Condition = n, CalibratedProbability = p[i], Threshold = 0.4,
                Status = p[i] >= 0.4 ? ConditionStatus.Present : ConditionStatus.Absent, AbsenceSupported = p[i] < 0.4, DetectorAvailable = true,
                DetectorFired = fired != null && fired[i]
            }).ToArray();
            if (flags != null) a.Flags.AddRange(flags);
            return a;
        }

        private static readonly double[] Face = { 0.2, 0.1, 0.6, 0.8 };       // x, y, w, h (normalised)

        [Fact]
        public void Three_clear_outcomes()
        {
            ClinicalSummary det = ClinicalSummary.From(Make(V6States.FindingsSupported, new[] { 0.9, 0.1, 0.05 }));
            Assert.Equal(ClinicalCategory.ConditionDetected, det.Category); Assert.Equal("Condition detected: Acne", det.Title);
            Assert.Equal("Detected", det.Findings[0].Status); Assert.Equal("High", det.Findings[0].Certainty);
            Assert.Equal("Not detected", det.Findings[1].Status); Assert.Equal("High", det.Findings[1].Certainty);

            ClinicalSummary none = ClinicalSummary.From(Make(V6States.NoFindingsSupported, new[] { 0.2, 0.15, 0.05 }));
            Assert.Equal(ClinicalCategory.NoConditionDetected, none.Category); Assert.Equal("No condition detected", none.Title);
            Assert.Contains("does not rule out", none.Explanation);
            Assert.Equal("Moderate", none.Findings[0].Certainty);

            ClinicalSummary unsure = ClinicalSummary.From(Make(V6States.UncertainReviewRequired, new[] { 0.65, 0.1, 0.05 }, flags: new[] { "cnn_yolo_conflict_Eczema" }));
            Assert.Equal(ClinicalCategory.NeedsReview, unsure.Category); Assert.Equal("Acne detected · needs review", unsure.Title);
            Assert.Equal("Moderate", unsure.Findings[0].Certainty);
            Assert.Contains(unsure.ReviewReasons, r => r.StartsWith("Eczema: the whole-face reading and the lesion detector disagree"));

            Assert.Equal(ClinicalCategory.Retake, ClinicalSummary.From(Make(V6States.RecaptureRequired, new[] { 0.1, 0.1, 0.1 })).Category);
            Assert.Equal(ClinicalCategory.NotCompleted, ClinicalSummary.From(UnifiedAssessment.Failed("x")).Category);
            Assert.Equal(ClinicalCategory.NotCompleted, ClinicalSummary.From(null).Category);
        }

        [Fact]
        public void A_detector_mark_the_model_did_not_confirm_is_possible_and_needs_review()
        {
            UnifiedAssessment a = Make(V6States.NoFindingsSupported, new[] { 0.1, 0.1, 0.2 }, fired: new[] { false, false, true });
            ClinicalSummary s = ClinicalSummary.From(a);
            Assert.Equal("Possible", s.Findings[2].Status); Assert.True(s.Findings[2].NeedsReview);
            Assert.Equal(ClinicalCategory.NeedsReview, s.Category); Assert.Equal("Needs dermatologist review", s.Title);
            Assert.Contains(s.ReviewReasons, r => r.StartsWith("Eczema: a lesion detector marked an area"));
            Assert.Equal("Possible · review needed", ClinicalSummary.FindingLine(s.Findings[2]));
        }

        [Fact]
        public void Hyperpigmentation_marked_by_its_detector_is_detected_with_the_source_named()
        {
            UnifiedAssessment a = Make(V6States.UncertainReviewRequired, new[] { 0.1, 0.2, 0.05 }, fired: new[] { false, true, false }, flags: new[] { "cnn_yolo_conflict_Hyperpigmentation" });
            a.Boxes.Add(new V6Box { Condition = "Hyperpigmentation", X0 = 20, Y0 = 45, X1 = 30, Y1 = 55, Confidence = 0.31f, Deployed = true });
            ClinicalSummary s = ClinicalSummary.From(a, Face);
            ClinicalFinding h = s.Findings[1];
            Assert.Equal("Detected", h.Status); Assert.True(h.ByDetector); Assert.False(h.NeedsReview);
            Assert.Equal("Low", h.Certainty);                                                  // detector confidence 0.31: never more than moderate
            Assert.Equal(ClinicalCategory.ConditionDetected, s.Category); Assert.Equal("Condition detected: Hyperpigmentation", s.Title);
            Assert.Empty(s.ReviewReasons);                                                     // the conflict is the reason it was detector-led, not a separate doubt
            Assert.Contains(s.Notes, n => n.StartsWith("Hyperpigmentation was found by its lesion detector"));
            Assert.Equal("Detected by the lesion detector · low certainty · right cheek", ClinicalSummary.FindingLine(h));

            // with acne also present and eczema only possible: two conditions detected, still needs review because of eczema
            UnifiedAssessment b = Make(V6States.UncertainReviewRequired, new[] { 0.7, 0.2, 0.1 }, fired: new[] { true, true, true });
            b.Boxes.Add(new V6Box { Condition = "Hyperpigmentation", X0 = 40, Y0 = 40, X1 = 60, Y1 = 60, Confidence = 0.6f, Deployed = true });
            ClinicalSummary t = ClinicalSummary.From(b, Face);
            Assert.Equal("Acne and Hyperpigmentation detected · needs review", t.Title); Assert.Equal("Moderate", t.Findings[1].Certainty);
            Assert.Equal("Possible", t.Findings[2].Status);
        }

        [Fact]
        public void Facial_regions_are_named_from_the_marked_areas_on_the_subjects_own_side()
        {
            Assert.Equal("forehead", ClinicalSummary.RegionOf(0.5, 0.15, Face));
            Assert.Equal("nose", ClinicalSummary.RegionOf(0.5, 0.5, Face));
            Assert.Equal("mouth / chin", ClinicalSummary.RegionOf(0.5, 0.8, Face));
            Assert.Equal("right cheek", ClinicalSummary.RegionOf(0.25, 0.5, Face));          // image left = the subject's right (camera not mirrored)
            Assert.Equal("left cheek", ClinicalSummary.RegionOf(0.75, 0.5, Face));
            Assert.Null(ClinicalSummary.RegionOf(0.01, 0.99, Face));                         // far outside the face: not named

            UnifiedAssessment a = Make(V6States.FindingsSupported, new[] { 0.9, 0.1, 0.05 });
            a.Boxes.Add(new V6Box { Condition = "Acne", X0 = 20, Y0 = 45, X1 = 30, Y1 = 55, Confidence = 0.6f, Deployed = true });
            a.Boxes.Add(new V6Box { Condition = "Acne", X0 = 45, Y0 = 10, X1 = 55, Y1 = 20, Confidence = 0.5f, Deployed = true });
            ClinicalSummary s = ClinicalSummary.From(a, Face);
            Assert.Equal(new[] { "forehead", "right cheek" }, s.Findings[0].Regions);
            Assert.Equal("Detected · high certainty · forehead, right cheek", ClinicalSummary.FindingLine(s.Findings[0]));
            Assert.Equal("Areas marked: forehead, right cheek (Acne)", V6SessionSummary.AreasLine(s));
        }

        [Fact]
        public void Scan_reading_takes_the_strongest_finding_and_any_review_need_across_photographs()
        {
            var views = new List<KeyValuePair<string, ClinicalSummary>>
            {
                new KeyValuePair<string, ClinicalSummary>("Front", ClinicalSummary.From(Make(V6States.FindingsSupported, new[] { 0.9, 0.1, 0.05 }))),
                new KeyValuePair<string, ClinicalSummary>("Left side", ClinicalSummary.From(Make(V6States.NoFindingsSupported, new[] { 0.1, 0.1, 0.05 }))),
                new KeyValuePair<string, ClinicalSummary>("Right side", ClinicalSummary.From(Make(V6States.FindingsSupported, new[] { 0.7, 0.1, 0.05 })))
            };
            ClinicalSummary s = ClinicalSummary.ForScan(views);
            Assert.Equal(ClinicalCategory.ConditionDetected, s.Category); Assert.Equal("Condition detected: Acne", s.Title);
            Assert.Equal("High", s.Findings[0].Certainty);

            views.Add(new KeyValuePair<string, ClinicalSummary>("Extra", ClinicalSummary.From(Make(V6States.RecaptureRequired, new[] { 0.1, 0.1, 0.1 }))));
            Assert.Equal(ClinicalCategory.NeedsReview, ClinicalSummary.ForScan(views).Category);
        }

        [Fact]
        public void Review_status_follows_the_dermatologist_record()
        {
            Assert.Equal(ReviewStatus.Verified, ReviewStatus.FromValidation("Completed"));
            Assert.Equal(ReviewStatus.FurtherReview, ReviewStatus.FromValidation("Further review"));
            Assert.Null(ReviewStatus.FromValidation("Pending"));                              // a draft does not change the status
            Assert.Equal(ReviewStatus.Pending, ReviewStatus.Normalise(null));
            Assert.Equal("Unassigned scans", ReviewStatus.FolderFor(ReviewStatus.Verified, false));
            Assert.Equal("Verified", ReviewStatus.FolderFor(ReviewStatus.Verified, true));
        }
    }
}
