using System.Linq;
using OpenCvSharp;
using SkinDevApp.V6;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>The V6 evidence heat map on the live preview: region-limited blending, scaling to the video frame, fade with age and motion.</summary>
    public class V6LiveOverlayTests
    {
        private static UnifiedAssessment Sample()
        {
            var a = new UnifiedAssessment { State = V6States.UncertainReviewRequired };
            a.Conditions = V6Conditions.Names.Select((n, i) => new ConditionAssessment { Condition = n, CalibratedProbability = new[] { 0.1, 0.6, 0.2 }[i], Threshold = 0.4, Status = i == 1 ? ConditionStatus.Present : ConditionStatus.Absent }).ToArray();
            a.Pool.Add(new V6Box { Condition = "Hyperpigmentation", Confidence = 0.8f, X0 = 60, Y0 = 40, X1 = 140, Y1 = 100, Deployed = true });
            return a;
        }

        [Fact]
        public void Prepare_uses_the_lead_condition_and_finds_the_region_with_evidence()
        {
            using (V6LiveOverlayState s = V6LiveOverlay.Prepare(Sample(), new Size(220, 160), null))
            {
                Assert.NotNull(s); Assert.Equal("Hyperpigmentation", s.Condition); Assert.Equal(1, s.ConditionIndex);
                Assert.True(s.Roi.Width > 0 && s.Roi.Height > 0); Assert.True(s.Roi.Width < 220 && s.Roi.Height < 160);        // only part of the frame
                Assert.True(s.Roi.Contains(new Point(100, 70)));                                                               // the box centre is inside it
            }
            var none = Sample(); none.Pool.Clear();
            Assert.Null(V6LiveOverlay.Prepare(none, new Size(220, 160), null));                                               // no evidence: nothing to draw
            Assert.Null(V6LiveOverlay.Prepare(null, new Size(220, 160), null));
        }

        [Fact]
        public void Render_tints_only_inside_the_evidence_region_and_scales_to_the_video_frame()
        {
            using (V6LiveOverlayState s = V6LiveOverlay.Prepare(Sample(), new Size(220, 160), null))
            using (Mat live = new Mat(480, 660, MatType.CV_8UC3, new Scalar(90, 90, 90)))          // a frame 3x the working size
            using (Mat outp = V6LiveOverlay.Render(live, s, 1.0))
            {
                Assert.Equal(live.Size(), outp.Size());
                Assert.True(outp.At<Vec3b>(210, 300).Item0 > 90);                                  // blue tint at the scaled box centre (BGR index 0)
                Assert.Equal(live.At<Vec3b>(20, 20), outp.At<Vec3b>(20, 20));                      // untouched far from the evidence
                Assert.Equal(90, live.At<Vec3b>(210, 300).Item0);                                  // the camera frame itself is never modified
            }
        }

        [Fact]
        public void Render_returns_nothing_when_the_fade_is_negligible()
        {
            using (V6LiveOverlayState s = V6LiveOverlay.Prepare(Sample(), new Size(220, 160), null))
            using (Mat live = new Mat(160, 220, MatType.CV_8UC3, new Scalar(90, 90, 90)))
            {
                Assert.Null(V6LiveOverlay.Render(live, s, 0.0)); Assert.Null(V6LiveOverlay.Render(live, s, 0.01)); Assert.Null(V6LiveOverlay.Render(null, s, 1.0)); Assert.Null(V6LiveOverlay.Render(live, null, 1.0));
                using (Mat half = V6LiveOverlay.Render(live, s, 0.5)) using (Mat full = V6LiveOverlay.Render(live, s, 1.0))
                    Assert.True(full.At<Vec3b>(70, 100).Item0 > half.At<Vec3b>(70, 100).Item0);    // a lower factor is fainter
            }
        }

        [Fact]
        public void Fade_follows_age_and_motion_like_the_v5_live_heat_map()
        {
            Assert.Equal(1.0, V6LiveOverlay.Factor(0.0, 8.0, 0.0, 0.1), 6);
            Assert.Equal(1.0, V6LiveOverlay.Factor(4.0, 8.0, 0.0, 0.1), 6);                        // full strength for the first half of its life
            Assert.Equal(0.5, V6LiveOverlay.Factor(6.0, 8.0, 0.0, 0.1), 6);                        // then fades
            Assert.Equal(0.0, V6LiveOverlay.Factor(8.0, 8.0, 0.0, 0.1), 6); Assert.Equal(0.0, V6LiveOverlay.Factor(20.0, 8.0, 0.0, 0.1), 6);
            Assert.Equal(0.0, V6LiveOverlay.Factor(0.0, 8.0, 0.1, 0.1), 6);                        // the face moved too much since the reading: hidden
            Assert.True(V6LiveOverlay.Factor(0.0, 8.0, 0.07, 0.1) < 1.0 && V6LiveOverlay.Factor(0.0, 8.0, 0.07, 0.1) > 0.0);
        }
    }
}
