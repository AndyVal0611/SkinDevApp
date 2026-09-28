using System;
using OpenCvSharp;

namespace SkinDevApp.Imaging
{
    /// <summary>
    /// Blends a raw Grad-CAM++ heatmap onto a video frame, client-side.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// In live mode the service returns only the raw 8-bit heatmap, not a
    /// rendered overlay (saves a colormap pass + full PNG encode per request,
    /// the single biggest cost at a 2s refresh rate). The camera preview
    /// still runs at full frame rate, so every one of those frames needs the
    /// heatmap composited onto it locally.
    ///
    /// MUST MATCH gradcam_service.py :: make_overlay() exactly:
    ///     color = COLORMAP_JET(heat)
    ///     a     = alpha * heat^gamma        (per pixel)
    ///     out   = frame*(1-a) + color*a
    /// </summary>
    public static class HeatmapRenderer
    {
        public const double DefaultAlpha = 0.55;
        public const double DefaultGamma = 1.25;

        /// <summary>
        /// frameBgr + heat8u -> blended BGR Mat. Caller disposes the result.
        /// heat8u may be any size - it is resized to match the frame.
        /// </summary>
        public static Mat Blend(
            Mat frameBgr,
            Mat heat8u,
            double alpha = DefaultAlpha,
            double gamma = DefaultGamma)
        {
            if (frameBgr == null || frameBgr.Empty())
                throw new ArgumentException("Empty frame.", nameof(frameBgr));

            if (heat8u == null || heat8u.Empty())
                return frameBgr.Clone();

            using (Mat heatResized = new Mat())
            {
                if (heat8u.Width != frameBgr.Width || heat8u.Height != frameBgr.Height)
                {
                    Cv2.Resize(
                        heat8u, heatResized,
                        new Size(frameBgr.Width, frameBgr.Height),
                        interpolation: InterpolationFlags.Cubic);
                }
                else
                {
                    heat8u.CopyTo(heatResized);
                }

                using (Mat color = new Mat())
                using (Mat heatF = new Mat())
                using (Mat alphaF = new Mat())
                using (Mat alpha3 = new Mat())
                using (Mat inv3 = new Mat())
                using (Mat frameF = new Mat())
                using (Mat colorF = new Mat())
                using (Mat sum = new Mat())
                {
                    Cv2.ApplyColorMap(heatResized, color, ColormapTypes.Jet);

                    // per-pixel alpha = alpha * (heat/255)^gamma
                    heatResized.ConvertTo(heatF, MatType.CV_32FC1, 1.0 / 255.0);
                    Cv2.Pow(heatF, gamma, alphaF);
                    Cv2.Multiply(alphaF, new Scalar(alpha), alphaF);

                    Cv2.Merge(new[] { alphaF, alphaF, alphaF }, alpha3);
                    Cv2.Subtract(new Scalar(1.0, 1.0, 1.0), alpha3, inv3);

                    frameBgr.ConvertTo(frameF, MatType.CV_32FC3);
                    color.ConvertTo(colorF, MatType.CV_32FC3);

                    Cv2.Multiply(frameF, inv3, frameF);
                    Cv2.Multiply(colorF, alpha3, colorF);
                    Cv2.Add(frameF, colorF, sum);

                    Mat result = new Mat();
                    sum.ConvertTo(result, MatType.CV_8UC3);
                    return result;
                }
            }
        }

        /// <summary>
        /// Fades the heatmap out as it ages, so a stalled service becomes
        /// visibly stale rather than silently showing an old result forever.
        /// </summary>
        public static double AgeAdjustedAlpha(
            TimeSpan age,
            double baseAlpha = DefaultAlpha,
            double fadeAfterSeconds = 6.0)
        {
            if (age.TotalSeconds <= fadeAfterSeconds) return baseAlpha;

            double over = age.TotalSeconds - fadeAfterSeconds;
            double factor = Math.Max(0.25, 1.0 - over / fadeAfterSeconds);

            return baseAlpha * factor;
        }
    }
}