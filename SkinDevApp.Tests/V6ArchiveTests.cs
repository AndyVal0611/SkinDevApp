using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using SkinDevApp.V6;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>Phase 4 tests: quality mapping, saving/reading the sealed V6 assessment next to a scan, and the drawn marked areas.</summary>
    public class V6ArchiveTests
    {
        private static UnifiedAssessment Sample(string state = V6States.FindingsSupported)
        {
            var a = new UnifiedAssessment { State = state, RunId = "open_run02", FeatureSchemaVersion = "astra-fusion/1", FrameWidth = 200, FrameHeight = 100, OodScore = 3.5, InferenceMs = 12.5 };
            a.Conditions = V6Conditions.Names.Select((n, i) => new ConditionAssessment
            {
                Condition = n, FusionLogit = i - 1.0, CalibratedProbability = 0.2 + 0.3 * i, Threshold = 0.4, Status = i == 1 ? ConditionStatus.Present : ConditionStatus.Absent,
                AbsenceSupported = i != 1, DetectorAvailable = true, DetectorFired = i == 1, GlobalOnlyProbability = 0.3
            }).ToArray();
            a.Flags.Add("cnn_yolo_conflict_Acne"); a.Reasons.Add("unresolved flags: cnn_yolo_conflict_Acne");
            a.Boxes.Add(new V6Box { Condition = "Hyperpigmentation", Confidence = 0.72f, X0 = 60, Y0 = 30, X1 = 120, Y1 = 80, Deployed = true });
            a.ModelSha256["v6_global_features_mobilenetv2.onnx"] = new string('a', 64);
            return a;
        }

        private static string TempDir() { string d = Path.Combine(Path.GetTempPath(), "v6arch_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        // ------------------------------------------------------------------ quality mapping
        [Fact]
        public void Quality_good_frame_is_assessed_with_nothing_blocking_and_says_what_was_not_checked()
        {
            QualityInput q = V6QualityMapper.From(true, true, true, 120, 0.01, 0.01, 640, 480);
            Assert.True(q.Assessed); Assert.Empty(q.Blocking); Assert.Contains("not checked", q.Coverage);
        }

        [Fact]
        public void Quality_each_problem_maps_to_its_blocking_flag()
        {
            Assert.Contains("blur", V6QualityMapper.From(true, true, false, 120, 0, 0, 640, 480).Blocking);
            Assert.Contains("underexposed", V6QualityMapper.From(true, true, true, 30, 0, 0, 640, 480).Blocking);
            Assert.Contains("underexposed", V6QualityMapper.From(true, true, true, 120, 0.5, 0, 640, 480).Blocking);
            Assert.Contains("overexposed", V6QualityMapper.From(true, true, true, 230, 0, 0, 640, 480).Blocking);
            Assert.Contains("overexposed", V6QualityMapper.From(true, true, true, 120, 0, 0.3, 640, 480).Blocking);
            Assert.Contains("low_resolution", V6QualityMapper.From(true, true, true, 120, 0, 0, 200, 480).Blocking);
            Assert.Contains("face_not_found_or_wrong_crop", V6QualityMapper.From(true, false, true, 120, 0, 0, 640, 480).Blocking);
            Assert.DoesNotContain("face_not_found_or_wrong_crop", V6QualityMapper.From(false, false, true, 120, 0, 0, 640, 480).Blocking);   // no face detector: cannot be claimed either way
        }

        [Fact]
        public void A_missing_face_blocks_a_live_capture_but_is_only_a_warning_for_an_upload_and_still_prevents_normal()
        {
            QualityInput live = V6QualityMapper.From(true, false, true, 120, 0, 0, 640, 480);
            Assert.Contains("face_not_found_or_wrong_crop", live.Blocking); Assert.Empty(live.Warnings);
            QualityInput up = V6QualityMapper.From(true, false, true, 120, 0, 0, 640, 480, faceBlocking: false);
            Assert.Empty(up.Blocking); Assert.Equal(new[] { "face_not_found" }, up.Warnings.ToArray());
            // everything else still blocks an upload
            Assert.Contains("blur", V6QualityMapper.From(true, false, false, 120, 0, 0, 640, 480, faceBlocking: false).Blocking);
            // the warning raises a review flag: the photograph is not retaken, but it can never be called Normal
            double[] none = { 0.05, 0.05, 0.05 }, thr = { 0.42, 0.37, 0.27 }; var all = new[] { true, true, true };
            List<string> flags = V6Decision.Flags(none, thr, none, thr, all, new[] { false, false, false }, 0.0, false, up);
            Assert.Equal(new[] { "quality_warning:face_not_found" }, flags.ToArray());
            DecisionResult d = V6Decision.Decide(none, thr, 0.0, all, flags, false, up);
            Assert.Equal(V6States.UncertainReviewRequired, d.State);
            var a = new UnifiedAssessment { State = d.State }; a.Flags.AddRange(flags);
            Assert.Contains(V6Text.FlagLines(a), l => l.StartsWith("No face was found in this image"));
        }

        [Fact]
        public void A_blocking_quality_problem_gives_recapture_not_a_result()
        {
            QualityInput q = V6QualityMapper.From(true, true, false, 120, 0, 0, 640, 480);
            DecisionResult d = V6Decision.Decide(new[] { 0.9, 0.9, 0.9 }, new[] { 0.42, 0.37, 0.27 }, 0.0, new[] { true, true, true }, new string[0], false, q);
            Assert.Equal(V6States.RecaptureRequired, d.State);
            Assert.False(string.IsNullOrEmpty(V6QualityMapper.Advice("blur")));
        }

        // ------------------------------------------------------------------ sealed archive
        [Fact]
        public void Saved_assessment_round_trips_with_every_field_the_results_page_needs()
        {
            string dir = TempDir();
            try
            {
                UnifiedAssessment a = Sample();
                using (Mat frame = new Mat(100, 200, MatType.CV_8UC3, new Scalar(30, 30, 30))) V6Archive.Write(dir, a, frame, null);
                Assert.True(File.Exists(Path.Combine(dir, V6Archive.FileName))); Assert.True(File.Exists(Path.Combine(dir, V6Archive.MarkedFileName)));
                UnifiedAssessment b = V6Archive.Read(dir, out string problem);
                Assert.Null(problem); Assert.NotNull(b);
                Assert.Equal(a.State, b.State); Assert.Equal(a.Flags, b.Flags); Assert.Equal(a.RunId, b.RunId); Assert.Equal(a.FrameWidth, b.FrameWidth);
                Assert.Equal(3, b.Conditions.Length);
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(a.Conditions[i].Condition, b.Conditions[i].Condition); Assert.Equal(a.Conditions[i].Status, b.Conditions[i].Status);
                    Assert.Equal(a.Conditions[i].CalibratedProbability, b.Conditions[i].CalibratedProbability, 12); Assert.Equal(a.Conditions[i].AbsenceSupported, b.Conditions[i].AbsenceSupported);
                    Assert.Equal(a.Conditions[i].Verdict, b.Conditions[i].Verdict);
                }
                Assert.Single(b.Boxes); Assert.Equal("Hyperpigmentation", b.Boxes[0].Condition); Assert.Equal(0.72f, b.Boxes[0].Confidence);
                Assert.Equal(V6States.ReviewStatusPending, b.ReviewStatus); Assert.True(b.RequiresDermatologistReview); Assert.False(b.ClinicalValidation);
                Assert.Equal(a.ModelSha256, b.ModelSha256);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void A_changed_or_missing_assessment_file_is_never_shown()
        {
            string dir = TempDir();
            try
            {
                Assert.Null(V6Archive.Read(dir, out string none)); Assert.Contains("no V6 assessment", none);
                V6Archive.Write(dir, Sample(V6States.UncertainReviewRequired), null, null);
                string path = Path.Combine(dir, V6Archive.FileName);
                File.WriteAllText(path, File.ReadAllText(path).Replace(V6States.UncertainReviewRequired, V6States.NoFindingsSupported));   // someone flips the result to Normal
                Assert.Null(V6Archive.Read(dir, out string tampered)); Assert.Contains("seal", tampered);
                File.WriteAllText(path, "{ not json");
                Assert.Null(V6Archive.Read(dir, out string broken)); Assert.Contains("could not be read", broken);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Marked_areas_are_drawn_inside_the_face_region_only_and_do_not_change_the_assessment()
        {
            UnifiedAssessment a = Sample();                                       // one box at x 60-120, y 30-80
            using (Mat frame = new Mat(100, 200, MatType.CV_8UC3, new Scalar(30, 30, 30)))
            {
                using (Mat inside = V6Archive.DrawMarkedAreas(frame, a, new Rect(40, 10, 100, 80)))
                using (Mat outside = V6Archive.DrawMarkedAreas(frame, a, new Rect(150, 0, 20, 20)))
                using (Mat nofilter = V6Archive.DrawMarkedAreas(frame, a, null))
                {
                    Assert.True(Cv2.Norm(inside, frame, NormTypes.L1) > 0);            // box drawn
                    Assert.Equal(0.0, Cv2.Norm(outside, frame, NormTypes.L1));         // box centre is outside that face region: not drawn
                    Assert.True(Cv2.Norm(nofilter, frame, NormTypes.L1) > 0);          // no face known: drawn
                }
                Assert.Single(a.Boxes);                                                 // drawing never edits the assessment
            }
        }
    }
}
