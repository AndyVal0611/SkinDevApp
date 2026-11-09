using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkinDevApp.Data;
using SkinDevApp.Printing;
using SkinDevApp.V6;
using SkinDevApp.Views;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>
    /// Unification tests: the wording, receipt block, report findings and agreement suggestion that replace the V5 classifier + separate detector text
    /// when the V6 flag is on, and the guarantee that with the flag off (or for a scan saved without V6) nothing changes.
    /// </summary>
    [Collection("V6Flag")]
    public class V6SurfacesTests
    {
        private static UnifiedAssessment Make(string state, string[] present, string[] flags = null, bool allDetectors = true)
        {
            var a = new UnifiedAssessment { State = state, RunId = "open_run02" };
            a.Conditions = V6Conditions.Names.Select((n, i) => new ConditionAssessment
            {
                Condition = n, CalibratedProbability = present.Contains(n) ? 0.8 : 0.1, Threshold = 0.4, Status = present.Contains(n) ? ConditionStatus.Present : ConditionStatus.Absent,
                AbsenceSupported = !present.Contains(n) && allDetectors, DetectorAvailable = allDetectors, DetectorFired = present.Contains(n)
            }).ToArray();
            if (flags != null) a.Flags.AddRange(flags);
            foreach (string p in present) a.Boxes.Add(new V6Box { Condition = p, Confidence = 0.7f, X0 = 1, Y0 = 1, X1 = 9, Y1 = 9, Deployed = true });
            return a;
        }

        private static Dictionary<string, UnifiedAssessment> Map(params KeyValuePair<string, UnifiedAssessment>[] items) => items.ToDictionary(k => k.Key, k => k.Value);
        private static KeyValuePair<string, UnifiedAssessment> Kv(string k, UnifiedAssessment a) => new KeyValuePair<string, UnifiedAssessment>(k, a);

        // ------------------------------------------------------------------ wording and summary
        [Fact]
        public void Condition_lines_and_state_headlines_are_plain_and_cautious()
        {
            UnifiedAssessment a = Make(V6States.FindingsSupported, new[] { "Hyperpigmentation" });
            Assert.Equal("Finding supported: Hyperpigmentation", V6Text.StateHeadline(a));
            Assert.Contains("Hyperpigmentation: Present (AI support 80% (decision limit 40%); detector marked an area)", a.Conditions.Select(V6Text.ConditionLine));
            Assert.Contains("Acne: Not found (AI support 10% (decision limit 40%); detector ran: nothing marked)", a.Conditions.Select(V6Text.ConditionLine));
            Assert.Equal("Uncertain: review recommended", V6Text.StateHeadline(Make(V6States.UncertainReviewRequired, new string[0])));
            Assert.Equal("The analysis could not be completed", V6Text.StateHeadline(UnifiedAssessment.Failed("x")));
            Assert.Contains("dermatologist must confirm", V6Text.Disclaimer); Assert.Contains("dermatologist must confirm", V6Text.DisclaimerShort);
        }

        [Fact]
        public void Flags_are_explained_in_plain_language_and_an_unavailable_detector_is_stated()
        {
            UnifiedAssessment a = Make(V6States.UncertainReviewRequired, new string[0], new[] { "cnn_yolo_conflict_Acne", "ood", "quality_blur" }, allDetectors: false);
            a.Reasons.Add("detector unavailable for Eczema (detector not installed)");
            a.Reasons.Add("blocking quality: blur");                                     // the plain advice already covers it: the raw reason must not be repeated
            string[] lines = V6Text.FlagLines(a).ToArray();
            Assert.DoesNotContain(lines, l => l.ToLowerInvariant().StartsWith("blocking quality"));
            Assert.Contains(lines, l => l.Contains("For Acne, the whole-image model and the lesion detector point different ways"));
            Assert.Contains(lines, l => l.Contains("unlike the images the system was trained on"));
            Assert.Contains(lines, l => l.Contains("retake") || l.Contains("refocus"));
            Assert.Contains(lines, l => l.StartsWith("Detector unavailable for Eczema"));
        }

        [Fact]
        public void Review_notes_that_repeat_are_grouped_into_one_sentence_and_close_calls_show_a_decimal()
        {
            UnifiedAssessment a = Make(V6States.UncertainReviewRequired, new string[0], new[] { "cnn_yolo_conflict_Acne", "cnn_yolo_conflict_Hyperpigmentation", "cnn_yolo_conflict_Eczema", "near_threshold_Acne", "near_threshold_Eczema" });
            string[] lines = V6Text.FlagLines(a).ToArray();
            Assert.Equal("For Acne, Hyperpigmentation and Eczema, the whole-image model and the lesion detector point different ways.", lines[0]);
            Assert.Equal("Acne and Eczema are close to the decision limit.", lines[1]); Assert.Equal(2, lines.Length);
            var c = new ConditionAssessment { Condition = "Acne", CalibratedProbability = 0.4213, Threshold = 0.42, Status = ConditionStatus.Present, DetectorAvailable = true };
            Assert.Equal("AI support 42.1% (decision limit 42.0%)", V6Text.SupportText(c));                       // 42% against 42% would look like a tie
            c.CalibratedProbability = 0.80; Assert.Equal("AI support 80% (decision limit 42%)", V6Text.SupportText(c));
        }

        [Fact]
        public void Session_summary_reports_each_photograph_and_gives_one_clinical_reading()
        {
            var map = Map(Kv("Front", Make(V6States.FindingsSupported, new[] { "Hyperpigmentation" })), Kv("Left", Make(V6States.UncertainReviewRequired, new string[0], new[] { "cnn_yolo_conflict_Eczema" })),
                          Kv("Right", Make(V6States.FindingsSupported, new[] { "Hyperpigmentation" })));
            V6SessionSummary s = V6SessionSummary.From(map, single: false);
            Assert.False(s.Single); Assert.Equal(new[] { "Front", "Left", "Right" }, s.Views.Select(v => v.View).ToArray());
            Assert.Equal(ClinicalCategory.NeedsReview, s.Clinical.Category);                                    // one photograph needs a closer look: the scan needs review
            Assert.Equal("Hyperpigmentation detected · needs review", s.Headline);
            Assert.True(s.ReviewRecommended); Assert.Equal(new[] { "Hyperpigmentation" }, s.PresentUnion); Assert.False(s.AllNoFindings);
            Assert.Contains(s.Notes, n => n.Contains("do not all show the same findings"));
            Assert.Equal("Needs review: Hyperpigmentation", s.ShortLabel);
            Assert.Equal("Left side", s.Views[1].Label); Assert.Single(s.Views[1].FlagLines);
            Assert.Contains("Eczema: the findings are inconclusive and require clinical review (Left side).", s.Clinical.ReviewReasons);
        }

        [Fact]
        public void Single_photograph_summary_uses_the_clinical_title_and_a_short_receipt_label()
        {
            V6SessionSummary none = V6SessionSummary.From(Map(Kv("Single", Make(V6States.NoFindingsSupported, new string[0]))), true);
            Assert.Equal("No condition detected", none.ShortLabel); Assert.True(none.AllNoFindings);
            V6SessionSummary two = V6SessionSummary.From(Map(Kv("Single", Make(V6States.MultipleFindingsSupported, new[] { "Acne", "Eczema" }))), true);
            Assert.Equal("Condition detected: Acne and Eczema", two.Headline); Assert.Equal("Condition detected: Acne, Eczema", two.ShortLabel); Assert.False(two.ReviewRecommended);
            Assert.Equal("Needs review", V6SessionSummary.From(Map(Kv("Single", Make(V6States.UncertainReviewRequired, new string[0]))), true).ShortLabel);
            Assert.Equal("Analysis not completed", V6SessionSummary.From(Map(Kv("Single", UnifiedAssessment.Failed("x"))), true).ShortLabel);
        }

        // ------------------------------------------------------------------ receipt
        [Fact]
        public void Receipt_block_states_the_clinical_result_and_has_no_v5_classification_lines()
        {
            V6SessionSummary s = V6SessionSummary.From(Map(Kv("Single", Make(V6States.FindingsSupported, new[] { "Hyperpigmentation" }, allDetectors: true))), true);
            foreach (bool final in new[] { false, true })
            {
                var w = new ReceiptWriter(32); V6ReceiptText.Append(w, s, final);
                string text = string.Join("\n", w.Lines.Select(l => l.Text));
                Assert.Contains(final ? "AI SCREENING RESULT (ORIGINAL)" : "AI SCREENING RESULT", text); Assert.Contains("CONDITION DETECTED", text);
                Assert.Contains("Condition detected: Hyperpigmentation", text); Assert.Contains("Detected, high certainty", text); Assert.Contains("Not detected, high certainty", text);
                Assert.Contains("Verification: Pending verification", text); Assert.Contains("Not a diagnosis", text);
                Assert.DoesNotContain("xperimental", text); Assert.DoesNotContain("Most resembles", text); Assert.DoesNotContain("similarity scores", text);
                Assert.Equal(final, text.Contains("Shown exactly as the AI saved it"));
            }
        }

        // ------------------------------------------------------------------ report findings
        private static SessionDetail Session(string mode, string folder) => new SessionDetail
        {
            Session = new ScanSessionRow { ScanMode = mode },
            Views = new List<CaptureViewRow> { new CaptureViewRow { ViewType = "Single", RecordFolder = folder } }
        };

        [Fact]
        public void Report_findings_use_the_final_model_result_when_the_flag_is_on_and_do_not_when_it_is_off()
        {
            Func<bool> keep = V6FeatureFlag.Provider; string dir = Path.Combine(Path.GetTempPath(), "v6rep_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                V6Archive.Write(dir, Make(V6States.UncertainReviewRequired, new[] { "Acne" }, new[] { "cnn_yolo_conflict_Eczema" }), null, null);
                V6FeatureFlag.Provider = () => true;
                ReportFindings f = ReportFindings.Build(Session("Single", dir));
                Assert.NotNull(f.V6); Assert.True(f.HasResult); Assert.Empty(f.Evidence);                                   // no Grad-CAM++ attribution pages beside the final model's result
                Assert.Contains(f.Lines, l => l.StartsWith("Screening result: Acne detected · needs review"));
                Assert.Contains(f.Lines, l => l.StartsWith("Review status: Pending verification"));
                Assert.Contains(f.Lines, l => l.Contains("Detected: Acne")); Assert.Contains(f.Lines, l => l.Contains("A dermatologist must confirm this result"));
                Assert.True(f.ReviewRecommended); Assert.Contains(f.Flags, x => x.Contains("Eczema: the findings are inconclusive"));
                Assert.Null(f.Interpretation);                                                                              // the V5 interpretation is not built
                V6FeatureFlag.Provider = () => false;
                ReportFindings off = ReportFindings.Build(Session("Single", dir));
                Assert.Null(off.V6); Assert.False(off.HasResult); Assert.Contains("No view produced a usable AI result", off.Lines[0]);   // original behaviour
            }
            finally { V6FeatureFlag.Provider = keep; Directory.Delete(dir, true); }
        }

        // ------------------------------------------------------------------ researcher agreement suggestion
        [Fact]
        public void Agreement_suggestion_compares_the_reviewer_with_the_v6_assessment()
        {
            V6SessionSummary acne = V6SessionSummary.From(Map(Kv("Single", Make(V6States.FindingsSupported, new[] { "Acne" }))), true);
            V6SessionSummary both = V6SessionSummary.From(Map(Kv("Single", Make(V6States.MultipleFindingsSupported, new[] { "Acne", "Eczema" }))), true);
            V6SessionSummary normal = V6SessionSummary.From(Map(Kv("Single", Make(V6States.NoFindingsSupported, new string[0]))), true);
            V6SessionSummary unsure = V6SessionSummary.From(Map(Kv("Single", Make(V6States.UncertainReviewRequired, new string[0]))), true);
            Assert.Null(V6Agreement.Suggest(new string[0], acne));
            Assert.Equal("Agree", V6Agreement.Suggest(new[] { "Acne" }, acne)); Assert.Equal("Disagree", V6Agreement.Suggest(new[] { "Eczema" }, acne));
            Assert.Equal("Partially agree", V6Agreement.Suggest(new[] { "Acne" }, both)); Assert.Equal("Agree", V6Agreement.Suggest(new[] { "Eczema", "Acne" }, both));
            Assert.Equal("Agree", V6Agreement.Suggest(new[] { "Normal" }, normal)); Assert.Equal("Disagree", V6Agreement.Suggest(new[] { "Acne" }, normal));
            Assert.Equal("Disagree", V6Agreement.Suggest(new[] { "Normal" }, acne));
            Assert.Equal("Uncertain", V6Agreement.Suggest(new[] { "Acne" }, unsure));                                       // the AI did not decide: nothing to agree with
            Assert.Equal("Uncertain", V6Agreement.Suggest(new[] { "Other / uncertain" }, acne)); Assert.Equal("Uncertain", V6Agreement.Suggest(new[] { "Unusable image" }, normal));
        }

        [Fact]
        public void Summary_is_null_when_the_flag_is_off_or_the_scan_has_no_v6_files()
        {
            Func<bool> keep = V6FeatureFlag.Provider; string dir = Path.Combine(Path.GetTempPath(), "v6sum_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                V6Archive.Write(dir, Make(V6States.FindingsSupported, new[] { "Acne" }), null, null);
                V6FeatureFlag.Provider = () => false; Assert.Null(V6SessionSummary.For(Session("Single", dir)));            // flag off: every screen shows what it always showed
                V6FeatureFlag.Provider = () => true;
                string empty = Path.Combine(dir, "older_scan"); Directory.CreateDirectory(empty);
                Assert.Null(V6SessionSummary.For(Session("Single", empty)));                                                // older scan, no V6 file: unchanged
                Assert.NotNull(V6SessionSummary.For(Session("Single", dir)));
            }
            finally { V6FeatureFlag.Provider = keep; Directory.Delete(dir, true); }
        }
    }
}

namespace SkinDevApp.Tests
{
    public class V6DetectorVerdictTests
    {
        [Xunit.Theory]
        [Xunit.InlineData(true, "possible", "Possible")]
        [Xunit.InlineData(false, "absent", "Not found")]
        public void AbsentWithDetectorMark_IsPossible_NotFound(bool fired, string verdict, string text)
        {
            var c = new SkinDevApp.V6.ConditionAssessment { Condition = "Hyperpigmentation", Status = SkinDevApp.V6.ConditionStatus.Absent, AbsenceSupported = true, DetectorAvailable = true, DetectorFired = fired };
            Xunit.Assert.Equal(verdict, c.Verdict);
            Xunit.Assert.Equal(text, SkinDevApp.V6.V6Text.VerdictText(c));
        }
    }
}
