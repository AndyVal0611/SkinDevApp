using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using SkinDevApp.Imaging;
using SkinDevApp.V6;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>V6 heat maps (detector-evidence maps in the same form as V5's): strength, colour, face-region rule, files written, lead condition.</summary>
    public class V6HeatmapTests
    {
        private static UnifiedAssessment Sample()
        {
            var a = new UnifiedAssessment { State = V6States.UncertainReviewRequired };
            a.Conditions = V6Conditions.Names.Select((n, i) => new ConditionAssessment { Condition = n, CalibratedProbability = new[] { 0.1, 0.6, 0.2 }[i], Threshold = 0.4, Status = i == 1 ? ConditionStatus.Present : ConditionStatus.Absent }).ToArray();
            a.Pool.Add(new V6Box { Condition = "Hyperpigmentation", Confidence = 0.8f, X0 = 60, Y0 = 40, X1 = 140, Y1 = 100, Deployed = true });
            a.Pool.Add(new V6Box { Condition = "Hyperpigmentation", Confidence = 0.2f, X0 = 70, Y0 = 50, X1 = 130, Y1 = 90, Deployed = false });   // overlaps, weaker: must not add up
            a.Pool.Add(new V6Box { Condition = "Acne", Confidence = 0.5f, X0 = 10, Y0 = 10, X1 = 30, Y1 = 30, Deployed = true });
            return a;
        }

        private static Mat Frame() => new Mat(160, 220, MatType.CV_8UC3, new Scalar(90, 90, 90));

        [Fact]
        public void Heat_peaks_at_the_box_centre_with_the_detector_confidence_and_overlapping_boxes_never_add_up()
        {
            using (Mat heat = V6Heatmaps.Heat(Sample(), "Hyperpigmentation", new Size(220, 160), null))
            {
                Assert.Equal(0.8f, heat.At<float>(70, 100), 2);                    // centre of the strong box
                double max; Cv2.MinMaxLoc(heat, out _, out max); Assert.True(max <= 0.8 + 1e-6);   // the weak overlapping box did not raise it
                Assert.True(heat.At<float>(70, 100) > heat.At<float>(70, 60));     // strength falls off towards the edge
                Assert.Equal(0f, heat.At<float>(150, 10));                         // far from any box: nothing
            }
            using (Mat none = V6Heatmaps.Heat(Sample(), "Eczema", new Size(220, 160), null)) Assert.Equal(0.0, Cv2.Sum(none).Val0);   // a condition without boxes has an empty map
        }

        [Fact]
        public void Boxes_centred_outside_the_face_region_are_not_drawn()
        {
            using (Mat inFace = V6Heatmaps.Heat(Sample(), "Acne", new Size(220, 160), new Rect(0, 0, 40, 40)))
                Assert.True(Cv2.Sum(inFace).Val0 > 0);
            using (Mat outside = V6Heatmaps.Heat(Sample(), "Acne", new Size(220, 160), new Rect(150, 100, 60, 50)))
                Assert.Equal(0.0, Cv2.Sum(outside).Val0);
        }

        [Fact]
        public void Overlay_tints_only_where_there_is_evidence_with_the_condition_colour()
        {
            using (Mat f = Frame()) using (Mat heat = V6Heatmaps.Heat(Sample(), "Hyperpigmentation", new Size(220, 160), null)) using (Mat ov = V6Heatmaps.Overlay(f, heat, 1))
            {
                Vec3b centre = ov.At<Vec3b>(70, 100), corner = ov.At<Vec3b>(150, 10);
                Assert.Equal(f.At<Vec3b>(150, 10), corner);                       // untouched far from the evidence
                Assert.True(centre.Item0 > f.At<Vec3b>(70, 100).Item0);           // blue channel (BGR index 0) rises under the hyperpigmentation tint (blue)
            }
        }

        [Fact]
        public void Writing_the_maps_creates_the_same_set_of_files_per_condition_that_V5_saves_and_finds_them_again()
        {
            string dir = Path.Combine(Path.GetTempPath(), "v6heat_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                using (Mat f = Frame()) V6Archive.Write(dir, Sample(), f, null);
                foreach (string c in V6Conditions.Names)
                {
                    Assert.NotNull(V6Heatmaps.OverlayPath(dir, c)); Assert.NotNull(V6Heatmaps.CombinedPath(dir, c)); Assert.True(File.Exists(Path.Combine(dir, V6Heatmaps.HeatPrefix + c + ".png")));
                }
                Assert.NotNull(V6Heatmaps.AllPath(dir)); Assert.Null(V6Heatmaps.OverlayPath(dir, "Nonsense")); Assert.Null(V6Heatmaps.OverlayPath(null, "Acne"));
                using (Mat both = Cv2.ImRead(V6Heatmaps.CombinedPath(dir, "Hyperpigmentation"))) Assert.Equal(new Size(220, 160), both.Size());
                UnifiedAssessment back = V6Archive.Read(dir, out string problem);                 // the pool survives the sealed save, so the maps can be explained later
                Assert.Null(problem); Assert.Equal(3, back.Pool.Count);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Lead_condition_is_the_present_one_with_the_most_support_else_the_most_supported()
        {
            Assert.Equal("Hyperpigmentation", V6Heatmaps.LeadCondition(Sample()));
            UnifiedAssessment none = Sample(); foreach (ConditionAssessment c in none.Conditions) c.Status = ConditionStatus.Absent;
            Assert.Equal("Hyperpigmentation", V6Heatmaps.LeadCondition(none));                 // highest support (0.6)
            none.Conditions[2].CalibratedProbability = 0.9; Assert.Equal("Eczema", V6Heatmaps.LeadCondition(none));
            Assert.Null(V6Heatmaps.LeadCondition(UnifiedAssessment.Failed("x")));
        }
    }
}
