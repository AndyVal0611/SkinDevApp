// ============================================================================
// V6LiveOverlay.cs  -  namespace SkinDevApp.V6
//
// The V6 evidence heat map on the LIVE camera preview. The V6 assessment is computed on a 640-px working frame every few seconds; this class turns it into an
// opacity map once (Prepare) and then blends it onto every video frame cheaply (Render), only inside the region where there is evidence. The caller fades it
// with age and motion exactly like V5's live heat map. Pure OpenCV; no WPF, no threading.
// ============================================================================

using System;
using OpenCvSharp;

namespace SkinDevApp.V6
{
    public sealed class V6LiveOverlayState : IDisposable
    {
        public Mat Alpha;            // CV_32F, working-frame size
        public Rect Roi;             // bounding box (working-frame pixels) of the non-zero part of Alpha
        public Size WorkingSize;
        public int ConditionIndex;
        public string Condition;

        public void Dispose() { Alpha?.Dispose(); Alpha = null; }
    }

    public static class V6LiveOverlay
    {
        /// <summary>Opacity map of the lead condition (or the given one) from the assessment's candidate pool. Null when there is nothing to draw.</summary>
        public static V6LiveOverlayState Prepare(UnifiedAssessment a, Size workingSize, Rect? face, string condition = null)
        {
            string c = condition ?? V6Heatmaps.LeadCondition(a);
            if (a == null || c == null || workingSize.Width <= 0 || workingSize.Height <= 0) return null;
            using (Mat heat = V6Heatmaps.Heat(a, c, workingSize, face))
            {
                Mat alpha = V6Heatmaps.Alpha(heat);
                using (Mat mask = new Mat())
                {
                    Cv2.Threshold(alpha, mask, 0.01, 1.0, ThresholdTypes.Binary);
                    using (Mat m8 = new Mat()) { mask.ConvertTo(m8, MatType.CV_8UC1); if (Cv2.CountNonZero(m8) == 0) { alpha.Dispose(); return null; } Rect roi = Cv2.BoundingRect(m8); return new V6LiveOverlayState { Alpha = alpha, Roi = roi, WorkingSize = workingSize, ConditionIndex = V6Conditions.IndexOf(c), Condition = c }; }
                }
            }
        }

        /// <summary>
        /// The live frame with the evidence tint blended in (factor 0..1 = age and motion fade). Returns null when the region is empty or the factor is negligible.
        /// The caller disposes the returned Mat. Only the region that carries evidence is blended, so the cost does not depend on the frame size.
        /// </summary>
        public static Mat Render(Mat liveFrame, V6LiveOverlayState s, double factor)
        {
            if (liveFrame == null || liveFrame.Empty() || s == null || s.Alpha == null || factor < 0.02) return null;
            double sx = (double)liveFrame.Width / s.WorkingSize.Width, sy = (double)liveFrame.Height / s.WorkingSize.Height;
            int x0 = Math.Max(0, (int)Math.Floor(s.Roi.X * sx)), y0 = Math.Max(0, (int)Math.Floor(s.Roi.Y * sy));
            int x1 = Math.Min(liveFrame.Width, (int)Math.Ceiling((s.Roi.X + s.Roi.Width) * sx)), y1 = Math.Min(liveFrame.Height, (int)Math.Ceiling((s.Roi.Y + s.Roi.Height) * sy));
            var r = new Rect(x0, y0, x1 - x0, y1 - y0);
            if (r.Width < 2 || r.Height < 2) return null;

            Mat result = liveFrame.Clone();
            using (Mat aWork = new Mat(s.Alpha, s.Roi))
            using (Mat a = new Mat())
            using (Mat roi = new Mat(result, r))
            {
                Cv2.Resize(aWork, a, new Size(r.Width, r.Height), 0, 0, InterpolationFlags.Linear);
                Cv2.Multiply(a, Scalar.All(Math.Min(1.0, factor)), a);
                using (Mat blended = V6Heatmaps.Blend(roi, a, s.ConditionIndex)) blended.CopyTo(roi);
            }
            return result;
        }

        /// <summary>Fade used for the live preview: full strength while the reading is young and the face has not moved, then down to zero (same shape as V5's live heat map).</summary>
        public static double Factor(double ageSeconds, double maxAgeSeconds, double motion, double motionThreshold)
        {
            if (ageSeconds >= maxAgeSeconds) return 0.0;
            double half = maxAgeSeconds * 0.5;
            double ageFactor = ageSeconds <= half ? 1.0 : Math.Max(0.0, 1.0 - (ageSeconds - half) / half);
            double motionFactor = Math.Max(0.0, Math.Min(1.0, (motionThreshold - motion) / (motionThreshold * 0.5)));
            return ageFactor * motionFactor;
        }
    }
}
