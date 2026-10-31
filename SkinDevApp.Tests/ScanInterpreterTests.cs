using System.Collections.Generic;
using System.Linq;
using SkinDevApp.Data;
using SkinDevApp.Printing;
using SkinDevApp.Views;
using Xunit;

namespace SkinDevApp.Tests
{
    public class ScanInterpreterTests
    {
        // order: Acne, Hyperpigmentation, Eczema, Normal
        private static readonly double[] ReportedCase = { 42.8, 0.5, 6.4, 50.3 };

        private static readonly bool[] AcneAndPigmentDetectors = { true, true, false };   // no eczema detector (pilot class)
        private static readonly bool[] AllDetectors = { true, true, true };
        private static readonly bool[] NoDetectors = { false, false, false };

        private static List<LesionFinding> Boxes(params (string cls, double conf)[] b) =>
            b.Select(x => new LesionFinding { ClassName = x.cls, Confidence = x.conf }).ToList();

        private static ScanInterpretation One(double[] p, List<LesionFinding> l, bool[] covered) => ScanInterpreter.Interpret(p, l, covered);

        private static ConditionEvidence Cond(ScanInterpretation i, string cls) => i.Conditions.Single(c => c.ClassName == cls);

        // ------------------------------------------------------------------ mixed ---

        [Fact]
        public void NormalTopWithBoxes_IsMixed_NotHealthy()
        {
            var r = One(ReportedCase, Boxes(("Acne", 0.47), ("Acne", 0.39), ("Hyperpigmentation", 0.75), ("Hyperpigmentation", 0.30)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.Equal("Mixed AI Findings — Review Recommended", r.Headline);
            Assert.True(r.ReviewRecommended);
            Assert.Equal(new[] { "Acne", "Hyperpigmentation" }, r.DetectedConditions);
            Assert.Contains("not shown as healthy skin", r.Detail);
        }

        [Fact]
        public void MixedAndUncertain_AreIndependent()
        {
            // reported case: top two are 7.5 points apart and the top match is only 50% (weak)
            var r = One(ReportedCase, Boxes(("Acne", 0.47)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.True(r.IsUncertain);
            Assert.True((r.Flags & UncertaintyFlags.CloseMargin) != 0);
            Assert.True((r.Flags & UncertaintyFlags.WeakMatch) != 0);
            Assert.StartsWith("Uncertainty:", r.UncertaintyLine);
            Assert.Equal(ScanInterpreter.MixedHeadline, r.Headline);   // the category headline is not replaced by the uncertainty
        }

        [Fact]
        public void MixedWithoutUncertainty_HasNoFlags()
        {
            var r = One(new[] { 3.0, 2.0, 5.0, 90.0 }, Boxes(("Acne", 0.6)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.Equal(UncertaintyFlags.None, r.Flags);
            Assert.Equal("", r.UncertaintyLine);
        }

        [Fact]
        public void ConditionTopWithDifferentConditionBoxes_IsMixed()
        {
            var r = One(new[] { 80.0, 2.0, 3.0, 15.0 }, Boxes(("Hyperpigmentation", 0.6)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
        }

        // ------------------------------------------------- multiple conditions ---

        [Fact]
        public void MultipleDetectedConditions_IsMixedEvenWhenTopIsOneOfThem_AndDoesNotClaimConfirmation()
        {
            var r = One(new[] { 80.0, 2.0, 3.0, 15.0 }, Boxes(("Acne", 0.6), ("Hyperpigmentation", 0.4), ("Eczema", 0.3)), AllDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.Equal(new[] { "Acne", "Hyperpigmentation", "Eczema" }, r.DetectedConditions);
            Assert.Contains("do not confirm", r.Detail);
        }

        [Fact]
        public void BoxCountNeverChangesTheOutcomeOrAppearsInTheText()
        {
            var one = One(ReportedCase, Boxes(("Acne", 0.5)), AcneAndPigmentDetectors);
            var many = One(ReportedCase, Boxes(Enumerable.Repeat(("Acne", 0.5), 37).ToArray()), AcneAndPigmentDetectors);
            Assert.Equal(one.Category, many.Category);
            Assert.Equal(one.Flags, many.Flags);
            Assert.Equal(InterpretationText.Full(one), InterpretationText.Full(many));
            Assert.DoesNotContain(InterpretationText.Full(many), l => l.Contains("37"));
        }

        [Fact]
        public void ConfidenceHigherThanTopScoreDoesNotMakeItDiagnostic()
        {
            var r = One(new[] { 10.0, 5.0, 5.0, 80.0 }, Boxes(("Acne", 0.99)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.DoesNotContain(InterpretationText.Full(r), l => l.ToLowerInvariant().Contains("confirmed diagnosis") && !l.ToLowerInvariant().Contains("not") && !l.ToLowerInvariant().Contains("do not"));
        }

        // ----------------------------------------- the three detector situations ---

        [Fact]
        public void StrongCondition_DetectorNotAvailable_IsClassificationOnly_NotAgreement()
        {
            var r = One(new[] { 2.0, 3.0, 85.0, 10.0 }, Boxes(), AcneAndPigmentDetectors);   // eczema top, no eczema detector
            Assert.Equal(InterpretationCategory.ClassificationOnly, r.Category);
            Assert.Equal(DetectorState.NotAvailable, Cond(r, "Eczema").State);
            Assert.Contains("available or run", r.Detail);
            Assert.NotEqual("Consistent AI Findings", r.Headline);
        }

        [Fact]
        public void StrongCondition_DetectorRanWithZeroBoxes_IsClassificationOnly_NotAgreement()
        {
            var r = One(new[] { 85.0, 3.0, 2.0, 10.0 }, Boxes(), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.ClassificationOnly, r.Category);
            Assert.Equal(DetectorState.NoQualifyingDetections, Cond(r, "Acne").State);
            Assert.Contains("no qualifying detections", r.Detail);
            Assert.Contains("neither confirms nor rules out", r.Detail);
        }

        [Fact]
        public void ConditionTopWithMatchingBoxes_IsConsistent_ButNotADiagnosis()
        {
            var r = One(new[] { 80.0, 2.0, 3.0, 15.0 }, Boxes(("Acne", 0.6)), AcneAndPigmentDetectors);
            Assert.Equal(InterpretationCategory.Consistent, r.Category);
            Assert.Equal(DetectorState.Detected, Cond(r, "Acne").State);
            Assert.Contains("not a confirmed diagnosis", r.Detail);
            Assert.False(r.ReviewRecommended);
        }

        [Fact]
        public void StrongNormal_NoBoxes_DetectorsRan_IsClassificationOnly_AndDoesNotConfirmHealth()
        {
            var r = One(new[] { 3.0, 2.0, 5.0, 90.0 }, Boxes(), AllDetectors);
            Assert.Equal(InterpretationCategory.ClassificationOnly, r.Category);
            Assert.Contains("does not confirm healthy skin", r.Detail);
        }

        [Fact]
        public void Normal_NoDetectorsAtAll_SaysThereIsNoDetectorEvidence()
        {
            var r = One(new[] { 3.0, 2.0, 5.0, 90.0 }, null, NoDetectors);
            Assert.Equal(InterpretationCategory.ClassificationOnly, r.Category);
            Assert.All(r.Conditions, c => Assert.Equal(DetectorState.NotAvailable, c.State));
            Assert.Contains("no detector evidence", r.Detail);
        }

        [Fact]
        public void DetectorLines_DistinguishUnavailableFromZeroBoxes()
        {
            var r = One(new[] { 3.0, 2.0, 5.0, 90.0 }, Boxes(), AcneAndPigmentDetectors);
            var lines = InterpretationText.DetectorLines(r);
            Assert.Equal("Acne: detector ran, no qualifying detections", lines[0]);
            Assert.Equal("Hyperpigmentation: detector ran, no qualifying detections", lines[1]);
            Assert.Equal("Eczema: no detector available or run", lines[2]);
        }

        // ---------------------------------------------------------- low confidence ---

        [Fact]
        public void LowConfidence_IsAFlag_NotACategory()
        {
            var r = One(new[] { 30.0, 25.0, 20.0, 25.0 }, Boxes(), AllDetectors);
            Assert.Equal(InterpretationCategory.ClassificationOnly, r.Category);
            Assert.True((r.Flags & UncertaintyFlags.LowConfidence) != 0);
            Assert.True(r.ReviewRecommended);
        }

        [Fact]
        public void LowConfidenceWithMatchingBoxes_StaysConsistentButUncertain()
        {
            var r = One(new[] { 38.0, 20.0, 22.0, 20.0 }, Boxes(("Acne", 0.5)), AllDetectors);
            Assert.Equal(InterpretationCategory.Consistent, r.Category);
            Assert.True(r.IsUncertain);
            Assert.True(r.ReviewRecommended);
        }

        [Fact]
        public void InvalidScores_AreUnavailable()
        {
            Assert.Equal(InterpretationCategory.Unavailable, One(null, Boxes(("Acne", 0.5)), AllDetectors).Category);
            Assert.Equal(InterpretationCategory.Unavailable, One(new[] { 1.0, 2.0 }, null, AllDetectors).Category);
            Assert.Equal(InterpretationCategory.Unavailable, One(new[] { double.NaN, 0, 0, 0 }, null, AllDetectors).Category);
        }

        // ------------------------------------------------------------ settings ---

        [Fact]
        public void Thresholds_AreCentralised_Documented_AndOverridable()
        {
            var d = InterpretationSettings.Default;
            Assert.Equal(40, d.LowConfidencePercent);
            Assert.Equal(60, d.AgreementPercent);
            Assert.Equal(10, d.CloseMarginPoints);
            Assert.Contains("not been validated against dermatologist-annotated data", InterpretationSettings.ProvisionalNote);

            var custom = InterpretationSettings.From(k => k == InterpretationSettings.KeyLowConfidence ? "50" : null);
            Assert.Equal(50, custom.LowConfidencePercent);
            Assert.Equal(60, custom.AgreementPercent);

            var r = ScanInterpreter.Interpret(new[] { 45.0, 5.0, 5.0, 45.0 }, new ViewEvidence(), custom);
            Assert.True((r.Flags & UncertaintyFlags.LowConfidence) != 0);
        }

        [Fact]
        public void InvalidOrInconsistentSettings_FallBackToDefaults()
        {
            Assert.Equal(40, InterpretationSettings.From(k => "abc").LowConfidencePercent);
            var bad = InterpretationSettings.From(k => k == InterpretationSettings.KeyAgreement ? "20" : null);   // agreement below low-confidence
            Assert.Equal(60, bad.AgreementPercent);
            Assert.Equal(40, bad.LowConfidencePercent);
        }

        [Fact]
        public void ProvisionalNote_IsAttachedToTheInterpretation()
        {
            Assert.Equal(InterpretationSettings.ProvisionalNote, One(ReportedCase, Boxes(), AllDetectors).ProvisionalNote);
        }

        // ------------------------------------------------------ scores untouched ---

        [Fact]
        public void InputScoresAndBoxesAreNeverModified()
        {
            var scores = (double[])ReportedCase.Clone();
            var boxes = Boxes(("Acne", 0.9), ("Hyperpigmentation", 0.8));
            One(scores, boxes, AllDetectors);
            Assert.Equal(ReportedCase, scores);
            Assert.Equal(2, boxes.Count);
            Assert.Equal(0.9, boxes[0].Confidence);
        }

        [Fact]
        public void ClassifierAndDetectorAreLabelledAsDifferentMeasures()
        {
            var r = One(ReportedCase, Boxes(("Acne", 0.47)), AcneAndPigmentDetectors);
            Assert.Contains("Image-level classification", r.ClassifierLine);
            Assert.Contains("Normal Skin 50.3%", r.ClassifierLine);
            Assert.Contains("Acne 42.8%", r.ClassifierLine);
            Assert.Contains("Eczema 6.4%", r.ClassifierLine);
            Assert.Contains("Hyperpigmentation 0.5%", r.ClassifierLine);
            var acne = InterpretationText.ConditionLine(Cond(r, "Acne"));
            Assert.Contains("detector confidence 0.47", acne);
            Assert.DoesNotContain("47%", acne);
        }

        [Fact]
        public void PooledViews_CombineDetectorEvidencePerCondition()
        {
            var views = new List<ViewEvidence>
            {
                new ViewEvidence { Lesions = Boxes(("Acne", 0.5)), Covered = AcneAndPigmentDetectors },
                new ViewEvidence { Lesions = Boxes(), Covered = AcneAndPigmentDetectors },
                new ViewEvidence { Lesions = Boxes(("Hyperpigmentation", 0.6)), Covered = AcneAndPigmentDetectors }
            };
            var r = ScanInterpreter.Interpret(ReportedCase, views);
            Assert.Equal(InterpretationCategory.Mixed, r.Category);
            Assert.Equal(1, Cond(r, "Acne").ViewsWithBoxes);
            Assert.Equal(3, Cond(r, "Acne").ViewsAssessed);
            Assert.Contains("in 1 of 3 photos", InterpretationText.ConditionLine(Cond(r, "Acne")));
        }

        // ------------------------------------- receipt / report / screen consistency ---

        private static CaptureViewRow View(string name, double[] p, string tag, params (string cls, double conf)[] boxes) => new CaptureViewRow
        {
            ViewType = name,
            QualityStatus = "Good",
            Scores = new ClassScoreRow { Acne = p[0], Hyperpigmentation = p[1], Eczema = p[2], Normal = p[3], PredictedClass = StudyText.Classes[System.Array.IndexOf(p, p.Max())], Confidence = p.Max() },
            Localization = tag == null ? null : new LocalizationRunRow { Status = "OK", ModelTag = tag },
            Lesions = boxes.Select(b => new LesionDetectionRow { ClassName = b.cls, ClassIndex = System.Array.IndexOf(StudyText.Classes, b.cls), Confidence = b.conf }).ToList()
        };

        private static SessionDetail Scan(string tag, params (string cls, double conf)[] boxes)
        {
            var d = new SessionDetail
            {
                Session = new ScanSessionRow { ScanMode = "Single", OverallPredictedClass = "Normal", OverallScore = 50.3, ViewsCompleted = 1, ViewsExpected = 1 }
            };
            d.Views.Add(View("Single", ReportedCase, tag, boxes));
            return d;
        }

        private static readonly string Specialists = "acne_v2 [Acne, 800 px] + pig_v1 [Hyperpigmentation, 640 px]";

        private static List<string> ReceiptText(ScanInterpretation i)
        {
            var w = new ReceiptWriter(42);
            ReceiptInterpretation.Append(w, i);
            return w.Lines.Select(l => l.Text).ToList();
        }

        [Fact]
        public void Receipt_ReportAndScreens_ShowTheSameInterpretation()
        {
            SessionDetail d = Scan(Specialists, ("Acne", 0.47), ("Hyperpigmentation", 0.75));
            ScanInterpretation i = SessionInterpretation.For(d);
            Assert.Equal(InterpretationCategory.Mixed, i.Category);

            // Results / patient record / review window: ViewReading carries the same interpretation text
            ViewReading reading = ClinicalText.Readings(d).Single();
            Assert.Equal(i.Headline, reading.Interpretation.Headline);
            Assert.Equal(i.Detail, reading.Interpretation.Detail);
            Assert.Contains(i.Detail, ClinicalText.Meaning(reading));

            // receipt
            List<string> receipt = ReceiptText(i);
            foreach (string line in InterpretationText.Summary(i)) Assert.Contains(line, receipt);
            foreach (string line in InterpretationText.DetectorLines(i)) Assert.Contains("  " + line, receipt);

            // A4 report
            ReportFindings report = ReportFindings.Build(d);
            foreach (string line in InterpretationText.Summary(i)) Assert.Contains("   " + line, report.Lines);
            foreach (string line in InterpretationText.DetectorLines(i)) Assert.Contains("   Detector - " + line, report.Lines);
            Assert.Contains(ScanInterpreter.MixedHeadline + ".", report.Flags);
            Assert.True(report.ReviewRecommended);
            Assert.Equal(i.Category, report.Interpretation.Category);
        }

        [Fact]
        public void Receipt_UnavailableDetector_ReadsNoDetector_NotNoneFound()
        {
            SessionDetail d = Scan(null);   // no localization run saved
            List<string> receipt = ReceiptText(SessionInterpretation.For(d));
            Assert.Contains("  Acne: no detector available or run", receipt);
            Assert.DoesNotContain(receipt, l => l.Contains("None found"));
            Assert.DoesNotContain(receipt, l => l.ToLowerInvariant().Contains("healthy"));
        }

        [Fact]
        public void Receipt_ZeroBoxes_ReadsNoQualifyingDetections()
        {
            List<string> receipt = ReceiptText(SessionInterpretation.For(Scan(Specialists)));
            Assert.Contains("  Acne: detector ran, no qualifying detections", receipt);
            Assert.Contains("  Eczema: no detector available or run", receipt);
        }

        [Fact]
        public void Receipt_IsPlainAsciiSafe_AfterRendering()
        {
            var lines = ReceiptText(SessionInterpretation.For(Scan(Specialists, ("Acne", 0.47)))).Select(t => new ReceiptLine(t)).ToList();
            foreach (RenderedLine r in ReceiptText_Render(lines)) Assert.All(r.Text, ch => Assert.InRange(ch, ' ', '~'));
        }

        private static List<RenderedLine> ReceiptText_Render(List<ReceiptLine> l) => SkinDevApp.Printing.ReceiptText.Render(l, 42, true);

        [Fact]
        public void DermatologistReview_NeverChangesTheAiInterpretationOrPrediction()
        {
            SessionDetail d = Scan(Specialists, ("Acne", 0.47));
            ScanInterpretation before = SessionInterpretation.For(d);
            string aiClass = d.Session.OverallPredictedClass;
            double[] scores = d.Views[0].Scores.AsArray();

            d.Validations.Add(new DermatologistValidation { DermatologistAssessment = "Acne vulgaris", AgreementWithAI = "Disagree", ValidationStatus = "Completed" });
            d.Evaluations.Add(new ResearcherEvaluation { ResearcherClassification = "Acne", AgreementWithAI = "Disagree" });
            ScanInterpretation after = SessionInterpretation.For(d);

            Assert.Equal(before.Category, after.Category);
            Assert.Equal(before.Headline, after.Headline);
            Assert.Equal(before.Detail, after.Detail);
            Assert.Equal(aiClass, d.Session.OverallPredictedClass);
            Assert.Equal(scores, d.Views[0].Scores.AsArray());
            Assert.Equal("Acne vulgaris", d.Validations[0].DermatologistAssessment);   // the clinical assessment is stored as its own record
        }

        [Fact]
        public void InterpretationDoesNotRemoveOrChangeStoredBoxes()
        {
            SessionDetail d = Scan(Specialists, ("Acne", 0.47), ("Acne", 0.39), ("Hyperpigmentation", 0.75));
            SessionInterpretation.For(d);
            ReportFindings.Build(d);
            Assert.Equal(3, d.Views[0].Lesions.Count);
            Assert.Equal(new[] { 0.47, 0.39, 0.75 }, d.Views[0].Lesions.Select(l => l.Confidence).ToArray());
        }

        [Fact]
        public void CoversReadsTheSavedModelTag()
        {
            Assert.True(SessionInterpretation.Covers("acne_v2 [Acne, 800 px]", "Acne"));
            Assert.False(SessionInterpretation.Covers("acne_v2 [Acne, 800 px]", "Eczema"));
            Assert.True(SessionInterpretation.Covers("lesion_detector_v1", "Eczema"));   // older 3-class detector
            Assert.False(SessionInterpretation.Covers(null, "Acne"));
        }
    }
}
