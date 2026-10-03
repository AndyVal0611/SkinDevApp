// ============================================================================
// FrameQuality.cs  (NEW)  -  namespace SkinDevApp.Imaging
//
// 1. FaceGuideLayout   - where the on-screen oval is, in frame-normalised
//                        coordinates. The XAML guide and the analyser share it.
// 2. FrameQualityAnalyzer - per-tick face position + image-quality check that
//                        produces the guidance text ("Move closer", ...).
// 3. MotionMeter       - frame-to-frame motion score (also used to decide when
//                        a Grad-CAM heatmap is still spatially valid).
//
// IMPORTANT - what this is for:
//   * The Haar face box is a POSITIONING AID and a REFERENCE FRAME for naming
//     regions. It is NOT a gate on classification: the classifier always sees
//     the whole frame. It only gates AUTO-CAPTURE (see ScanStateMachine), and
//     manual Snap / Upload always work.
//   * If the cascade file is missing the analyser reports GuidanceState.Unavailable
//     and auto-capture falls back to "no face requirement".
// ============================================================================

using System;
using System.IO;
using OpenCvSharp;

namespace SkinDevApp.Imaging
{
    public enum GuidanceState
    {
        Unavailable = 0,
        NoFace,
        MoveCloser,
        MoveBack,
        CenterFace,
        KeepStill,
        ImproveLighting,
        ReduceGlare,
        Ready
    }

    /// <summary>Oval the user is asked to fit their face into (frame-normalised).</summary>
    public static class FaceGuideLayout
    {
        public const double CenterX = 0.50;
        public const double CenterY = 0.46;
        public const double RadiusX = 0.24;     // of frame width
        public const double RadiusY = 0.33;     // of frame height

        // Face-box width as a fraction of the frame width.
        public const double MinFaceWidth = 0.34;   // below -> "Move closer"
        public const double MaxFaceWidth = 0.66;   // above -> "Move back"

        public const double MaxOffsetX = 0.11;
        public const double MaxOffsetY = 0.13;
    }

    public sealed class FrameQuality
    {
        public bool CascadeAvailable { get; set; }
        public bool FaceFound { get; set; }

        /// <summary>Face box in WORKING-frame pixels (reference frame only).</summary>
        public Rect? FaceBox { get; set; }

        /// <summary>Face box in frame-normalised coordinates (x,y,w,h).</summary>
        public double FaceX { get; set; }
        public double FaceY { get; set; }
        public double FaceW { get; set; }
        public double FaceH { get; set; }

        public double Sharpness { get; set; }          // Laplacian variance
        public double Brightness { get; set; }         // mean grey 0..255
        public double UnderExposedFraction { get; set; }
        public double OverExposedFraction { get; set; }

        public bool SharpEnough { get; set; }
        public bool ExposureOk { get; set; }

        /// <summary>Sharp + well exposed (independent of face position).</summary>
        public bool QualityOk => SharpEnough && ExposureOk;

        public GuidanceState Guidance { get; set; } = GuidanceState.Unavailable;
        public string GuidanceText { get; set; } = "";

        public bool Ready => Guidance == GuidanceState.Ready;
    }

    public sealed class FrameQualityAnalyzer : IDisposable
    {
        private CascadeClassifier _cascade;
        private readonly object _lock = new object();

        // Tunables
        public double MinSharpness { get; set; } = 25.0;
        public double MaxUnderExposed { get; set; } = 0.35;   // share of pixels < 20
        public double MaxOverExposed { get; set; } = 0.15;    // share of pixels > 240
        public double MinBrightness { get; set; } = 45.0;
        public double MaxBrightness { get; set; } = 215.0;

        /// <summary>Width the frame is shrunk to for face detection (speed).</summary>
        public int DetectionWidth { get; set; } = 320;

        /// <summary>Ticks a lost face box is reused before the face counts as gone.</summary>
        public int GraceTicks { get; set; } = 3;

        private bool _haveBox;            // _lx.._lh hold the last (smoothed) normalised face box
        private double _lx, _ly, _lw, _lh;
        private int _missCount;

        public bool CascadeAvailable => _cascade != null;

        public FrameQualityAnalyzer(string cascadePath = null)
        {
            string path = cascadePath ?? FindCascade();

            if (path != null)
            {
                try
                {
                    var c = new CascadeClassifier(path);
                    if (!c.Empty()) _cascade = c;
                    else c.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[QUALITY] cascade load failed: " + ex.Message);
                }
            }
        }

        public static string FindCascade()
        {
            const string file = "haarcascade_frontalface_default.xml";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(baseDir, file),
                Path.Combine(baseDir, "Assets", file),
                Path.Combine(baseDir, "models", file),
                Path.Combine(@"C:\PrecisionSkinV1\models", file)
            };

            foreach (string c in candidates)
                if (File.Exists(c)) return c;

            return null;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _haveBox = false;
                _missCount = 0;
            }
        }

        /// <summary>
        /// Analyse one WORKING-size BGR frame (e.g. 640 px wide). Call from ONE
        /// thread only (the fast loop).
        /// </summary>
        public FrameQuality Analyze(Mat bgr)
        {
            var q = new FrameQuality { CascadeAvailable = _cascade != null };

            if (bgr == null || bgr.Empty()) return q;

            int W = bgr.Width, H = bgr.Height;

            using (Mat gray = new Mat())
            {
                if (bgr.Channels() == 1) bgr.CopyTo(gray);
                else Cv2.CvtColor(bgr, gray, bgr.Channels() == 4
                    ? ColorConversionCodes.BGRA2GRAY
                    : ColorConversionCodes.BGR2GRAY);

                // ---- face detection (positioning aid only) ------------------
                if (_cascade != null)
                {
                    double s = Math.Min(1.0, (double)DetectionWidth / W);
                    using (Mat small = new Mat())
                    {
                        if (s < 1.0)
                            Cv2.Resize(gray, small, new Size((int)Math.Round(W * s), (int)Math.Round(H * s)),
                                       interpolation: InterpolationFlags.Area);
                        else
                            gray.CopyTo(small);

                        int minSide = Math.Max(40, Math.Min(small.Width, small.Height) / 6);

                        Rect[] faces = _cascade.DetectMultiScale(
                            small, 1.1, 5, HaarDetectionTypes.ScaleImage, new Size(minSide, minSide));

                        if (faces != null && faces.Length > 0)
                        {
                            Rect best = faces[0];
                            for (int i = 1; i < faces.Length; i++)
                                if (faces[i].Width * faces[i].Height > best.Width * best.Height)
                                    best = faces[i];

                            double nx = (double)best.X / small.Width;
                            double ny = (double)best.Y / small.Height;
                            double nw = (double)best.Width / small.Width;
                            double nh = (double)best.Height / small.Height;

                            // light smoothing so the guide text does not flicker
                            if (_haveBox && _missCount == 0)
                            {
                                const double a = 0.5;
                                nx = a * nx + (1 - a) * _lx;
                                ny = a * ny + (1 - a) * _ly;
                                nw = a * nw + (1 - a) * _lw;
                                nh = a * nh + (1 - a) * _lh;
                            }

                            _lx = nx; _ly = ny; _lw = nw; _lh = nh;
                            _haveBox = true;
                            _missCount = 0;
                        }
                        else if (_haveBox)
                        {
                            _missCount++;
                            if (_missCount > GraceTicks) _haveBox = false;
                        }
                    }
                }

                if (_haveBox)
                {
                    q.FaceFound = true;
                    q.FaceX = _lx; q.FaceY = _ly; q.FaceW = _lw; q.FaceH = _lh;
                    q.FaceBox = new Rect(
                        (int)Math.Round(_lx * W), (int)Math.Round(_ly * H),
                        (int)Math.Round(_lw * W), (int)Math.Round(_lh * H));
                }

                // ---- sharpness + exposure (face region if known, else centre) --
                Rect roi = q.FaceBox.HasValue
                    ? ClampRect(q.FaceBox.Value, W, H)
                    : new Rect(W / 4, H / 4, W / 2, H / 2);

                using (Mat region = new Mat(gray, roi))
                using (Mat lap = new Mat())
                using (Mat under = new Mat())
                using (Mat over = new Mat())
                {
                    Cv2.Laplacian(region, lap, MatType.CV_64F);
                    Scalar m, sd;
                    Cv2.MeanStdDev(lap, out m, out sd);
                    q.Sharpness = sd.Val0 * sd.Val0;

                    Scalar mean = Cv2.Mean(region);
                    q.Brightness = mean.Val0;

                    double total = Math.Max(1, region.Width * region.Height);
                    Cv2.InRange(region, new Scalar(0), new Scalar(20), under);
                    Cv2.InRange(region, new Scalar(241), new Scalar(255), over);
                    q.UnderExposedFraction = Cv2.CountNonZero(under) / total;
                    q.OverExposedFraction = Cv2.CountNonZero(over) / total;
                }

                q.SharpEnough = q.Sharpness >= MinSharpness;
                q.ExposureOk =
                    q.Brightness >= MinBrightness && q.Brightness <= MaxBrightness &&
                    q.UnderExposedFraction <= MaxUnderExposed &&
                    q.OverExposedFraction <= MaxOverExposed;
            }

            // ---- guidance ----------------------------------------------------
            if (_cascade == null)
            {
                q.Guidance = GuidanceState.Unavailable;
                q.GuidanceText = "Face guide unavailable";
            }
            else if (!q.FaceFound)
            {
                q.Guidance = GuidanceState.NoFace;
                q.GuidanceText = "Position your face in the oval";
            }
            else
            {
                double cx = q.FaceX + q.FaceW / 2.0;
                double cy = q.FaceY + q.FaceH / 2.0;

                if (q.FaceW < FaceGuideLayout.MinFaceWidth)
                {
                    q.Guidance = GuidanceState.MoveCloser;
                    q.GuidanceText = "Move closer";
                }
                else if (q.FaceW > FaceGuideLayout.MaxFaceWidth)
                {
                    q.Guidance = GuidanceState.MoveBack;
                    q.GuidanceText = "Move back";
                }
                else if (Math.Abs(cx - FaceGuideLayout.CenterX) > FaceGuideLayout.MaxOffsetX ||
                         Math.Abs(cy - FaceGuideLayout.CenterY) > FaceGuideLayout.MaxOffsetY)
                {
                    q.Guidance = GuidanceState.CenterFace;
                    q.GuidanceText = "Center your face";
                }
                else if (!q.SharpEnough)
                {
                    q.Guidance = GuidanceState.KeepStill;
                    q.GuidanceText = "Keep still";
                }
                else if (q.Brightness < MinBrightness || q.UnderExposedFraction > MaxUnderExposed)
                {
                    q.Guidance = GuidanceState.ImproveLighting;
                    q.GuidanceText = "Improve lighting";
                }
                else if (q.Brightness > MaxBrightness || q.OverExposedFraction > MaxOverExposed)
                {
                    q.Guidance = GuidanceState.ReduceGlare;
                    q.GuidanceText = "Reduce glare";
                }
                else
                {
                    q.Guidance = GuidanceState.Ready;
                    q.GuidanceText = "Ready";
                }
            }

            return q;
        }

        private static Rect ClampRect(Rect r, int w, int h)
        {
            int x = Math.Max(0, Math.Min(r.X, w - 2));
            int y = Math.Max(0, Math.Min(r.Y, h - 2));
            int rw = Math.Max(2, Math.Min(r.Width, w - x));
            int rh = Math.Max(2, Math.Min(r.Height, h - y));
            return new Rect(x, y, rw, rh);
        }

        public void Dispose()
        {
            _cascade?.Dispose();
            _cascade = null;
        }
    }

    /// <summary>
    /// Frame-to-frame / frame-to-heatmap-source motion score. Same maths the
    /// live overlay used before: 96x96 blurred grey thumbnails, mean removed
    /// (so auto-exposure drift is ignored), mean absolute difference.
    /// </summary>
    public static class MotionMeter
    {
        public static Mat MakeThumb(Mat bgr)
        {
            using (Mat gray = new Mat())
            {
                if (bgr.Channels() == 1) bgr.CopyTo(gray);
                else Cv2.CvtColor(bgr, gray, bgr.Channels() == 4
                    ? ColorConversionCodes.BGRA2GRAY
                    : ColorConversionCodes.BGR2GRAY);

                Mat small = new Mat();
                Cv2.Resize(gray, small, new Size(96, 96), interpolation: InterpolationFlags.Area);
                Cv2.GaussianBlur(small, small, new Size(5, 5), 0);
                return small;
            }
        }

        /// <summary>Mean-removed mean absolute difference of two thumbnails.</summary>
        public static double Between(Mat thumbA, Mat thumbB)
        {
            using (Mat a = new Mat())
            using (Mat b = new Mat())
            using (Mat d = new Mat())
            {
                thumbA.ConvertTo(a, MatType.CV_32FC1);
                thumbB.ConvertTo(b, MatType.CV_32FC1);

                Cv2.Subtract(a, new Scalar(Cv2.Mean(a).Val0), a);
                Cv2.Subtract(b, new Scalar(Cv2.Mean(b).Val0), b);

                Cv2.Absdiff(a, b, d);
                return Cv2.Mean(d).Val0;
            }
        }
    }
}
