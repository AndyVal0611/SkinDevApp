// ============================================================================
// ViewPoseEstimator.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// Checks that the patient really is showing the requested view, instead of
// trusting the instruction text.
//
// Method: YuNet face detector (OpenCV, ONNX) gives five landmarks per face
// (both eyes, nose tip, mouth corners). The head-turn measure is
//
//     yaw_ratio = (nose_x - eye_midpoint_x) / inter_eye_distance
//
//   ~ 0      facing the camera
//   > 0      nose points toward the image RIGHT  (frames are not mirrored, so the
//            patient's RIGHT cheek is shown)       -> ScanView.Right
//   < 0      nose points toward the image LEFT   -> ScanView.Left
//
// It is a landmark ratio, not a calibrated angle in degrees. Roughly 0.12 is a
// few degrees off-axis, ~0.20 about 25-30 degrees, ~0.27 about 45 degrees.
// Thresholds are properties so they can be tuned on real kiosk data.
//
// The yaw value is median-smoothed over the last few ticks so one noisy
// landmark cannot flip a gate. If the model file is missing, Available is false
// and the pose gate is skipped (the scan then falls back to instruction-only).
//
// Call from ONE thread (the fast loop).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    public sealed class PoseReading
    {
        public bool Available { get; set; }
        public bool FaceFound { get; set; }
        public double YawRatio { get; set; }          // smoothed
        public double RawYawRatio { get; set; }
        public double RollDegrees { get; set; }
        public double Score { get; set; }
        public double InterEyePx { get; set; }

        /// <summary>Face box in WORKING-frame pixels.</summary>
        public Rect? Box { get; set; }

        /// <summary>The view the head is actually showing (Any = in between / unknown).</summary>
        public ScanView Observed { get; set; } = ScanView.Any;
    }

    public sealed class PoseVerdict
    {
        /// <summary>True when the pose gate is satisfied (or not applicable).</summary>
        public bool Ok { get; set; } = true;
        public bool Verified { get; set; }            // a real measurement backed Ok
        public bool FaceFound { get; set; }
        public bool Centered { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class ViewPoseEstimator : IDisposable
    {
        public const string ModelFile = "face_detection_yunet_2023mar.onnx";

        /// <summary>|yaw| below this counts as facing front.</summary>
        public double FrontMaxAbsYaw { get; set; } = 0.12;

        /// <summary>|yaw| at or above this counts as a side view.</summary>
        public double SideMinAbsYaw { get; set; } = 0.20;

        public int DetectionWidth { get; set; } = 320;
        public float ScoreThreshold { get; set; } = 0.6f;

        /// <summary>Allowed centre offset of the face box, as a share of frame size (side views).</summary>
        public double MaxCenterOffsetX { get; set; } = 0.18;
        public double MaxCenterOffsetY { get; set; } = 0.18;

        /// <summary>Face width limits as a share of frame width (side faces look narrower).</summary>
        public double MinFaceWidth { get; set; } = 0.22;
        public double MaxFaceWidth { get; set; } = 0.75;

        /// <summary>True if the frames handed to Estimate are mirrored (they are not, today).</summary>
        public bool CameraMirrored { get; set; } = false;

        private FaceDetectorYN _det;
        private string _modelPath;
        private int _detW, _detH;
        private readonly Queue<double> _yawHist = new Queue<double>();
        private const int HistLen = 5;
        private readonly object _lock = new object();

        public bool Available => _det != null;
        public string LoadError { get; private set; } = "";

        public ViewPoseEstimator(string modelPath = null)
        {
            try
            {
                string path = modelPath ?? FindModel();
                if (path == null) { LoadError = ModelFile + " not found next to the application."; return; }
                _modelPath = path;
                _det = FaceDetectorYN.Create(path, "", new Size(320, 320), ScoreThreshold, 0.3f, 5000);
                _detW = 320; _detH = 320;
            }
            catch (Exception ex)
            {
                LoadError = ex.GetType().Name + ": " + ex.Message;
                _det = null;
            }
        }

        public static string FindModel()
        {
            var dirs = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model")
            };
            foreach (string d in dirs.Where(x => !string.IsNullOrEmpty(x)))
            {
                string p = Path.Combine(d, ModelFile);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public void Reset()
        {
            lock (_lock) _yawHist.Clear();
        }

        /// <summary>Measure the head pose on one WORKING-size BGR frame.</summary>
        public PoseReading Estimate(Mat bgr)
        {
            var r = new PoseReading { Available = _det != null };
            if (_det == null || bgr == null || bgr.Empty()) return r;

            lock (_lock)
            {
                try
                {
                    double s = Math.Min(1.0, (double)DetectionWidth / bgr.Width);
                    int w = Math.Max(32, (int)Math.Round(bgr.Width * s));
                    int h = Math.Max(32, (int)Math.Round(bgr.Height * s));

                    using (Mat small = new Mat())
                    using (Mat faces = new Mat())
                    {
                        if (s < 1.0) Cv2.Resize(bgr, small, new Size(w, h), interpolation: InterpolationFlags.Area);
                        else bgr.CopyTo(small);

                        if (small.Channels() == 4) Cv2.CvtColor(small, small, ColorConversionCodes.BGRA2BGR);

                        if (w != _detW || h != _detH)
                        {
                            // This OpenCvSharp build has no SetInputSize: re-create the detector for a new size.
                            _det.Dispose();
                            _det = FaceDetectorYN.Create(_modelPath, "", new Size(w, h), ScoreThreshold, 0.3f, 5000);
                            _detW = w; _detH = h;
                        }

                        _det.Detect(small, faces);

                        if (faces.Empty() || faces.Rows == 0 || faces.Cols < 15)
                        {
                            _yawHist.Clear();
                            return r;
                        }

                        // largest face
                        int best = 0; float bestArea = -1f;
                        for (int i = 0; i < faces.Rows; i++)
                        {
                            float a = faces.At<float>(i, 2) * faces.At<float>(i, 3);
                            if (a > bestArea) { bestArea = a; best = i; }
                        }

                        float F(int c) { return faces.At<float>(best, c); }

                        double x = F(0), y = F(1), fw = F(2), fh = F(3);
                        double rex = F(4), rey = F(5), lex = F(6), ley = F(7), nx = F(8);
                        double score = F(14);

                        double d = Math.Sqrt((lex - rex) * (lex - rex) + (ley - rey) * (ley - rey));
                        if (d < 4.0) return r;                                    // degenerate landmarks

                        double mid = (rex + lex) / 2.0;
                        double yaw = (nx - mid) / d;
                        if (CameraMirrored) yaw = -yaw;

                        _yawHist.Enqueue(yaw);
                        while (_yawHist.Count > HistLen) _yawHist.Dequeue();
                        double[] sorted = _yawHist.OrderBy(v => v).ToArray();
                        double smooth = sorted[sorted.Length / 2];

                        double inv = 1.0 / s;
                        r.FaceFound = true;
                        r.Score = score;
                        r.RawYawRatio = yaw;
                        r.YawRatio = smooth;
                        r.InterEyePx = d * inv;
                        r.RollDegrees = Math.Atan2(ley - rey, lex - rex) * 180.0 / Math.PI;

                        int bx = (int)Math.Round(x * inv), by = (int)Math.Round(y * inv);
                        int bw = (int)Math.Round(fw * inv), bh = (int)Math.Round(fh * inv);
                        bx = Math.Max(0, bx); by = Math.Max(0, by);
                        bw = Math.Min(bw, bgr.Width - bx); bh = Math.Min(bh, bgr.Height - by);
                        if (bw > 0 && bh > 0) r.Box = new Rect(bx, by, bw, bh);

                        double ay = Math.Abs(smooth);
                        if (ay < FrontMaxAbsYaw) r.Observed = ScanView.Front;
                        else if (ay >= SideMinAbsYaw) r.Observed = smooth > 0 ? ScanView.Right : ScanView.Left;
                        else r.Observed = ScanView.Any;
                        return r;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[POSE] " + ex.Message);
                    return r;
                }
            }
        }

        /// <summary>
        /// Gate for the required view. For Front the existing Haar positioning gate still applies
        /// as well; for Left / Right this gate also supplies "face found / centred".
        /// </summary>
        public PoseVerdict Evaluate(ScanView required, PoseReading r, int frameW, int frameH)
        {
            var v = new PoseVerdict();

            if (required == ScanView.Any) { v.Ok = true; return v; }

            if (r == null || !r.Available)
            {
                v.Ok = true;                      // no model: instruction-only mode
                v.Verified = false;
                v.Message = "Pose check unavailable";
                return v;
            }

            if (!r.FaceFound)
            {
                v.Ok = false;
                v.Message = required == ScanView.Front
                    ? "Face the camera in the oval"
                    : "Keep your face visible while turning";
                return v;
            }

            v.FaceFound = true;

            // size + centring (the Haar front gate covers this for Front; we repeat it cheaply)
            bool sizeOk = true, centred = true;
            if (r.Box.HasValue && frameW > 0 && frameH > 0)
            {
                Rect b = r.Box.Value;
                double wFrac = (double)b.Width / frameW;
                double cx = (b.X + b.Width / 2.0) / frameW - FaceGuideLayout.CenterX;
                double cy = (b.Y + b.Height / 2.0) / frameH - FaceGuideLayout.CenterY;
                sizeOk = wFrac >= MinFaceWidth && wFrac <= MaxFaceWidth;
                centred = Math.Abs(cx) <= MaxCenterOffsetX && Math.Abs(cy) <= MaxCenterOffsetY;
                if (!sizeOk)
                {
                    v.Ok = false;
                    v.Message = wFrac < MinFaceWidth ? "Move closer" : "Move back";
                    return v;
                }
                if (!centred)
                {
                    v.Ok = false;
                    v.Message = "Centre your face in the oval";
                    return v;
                }
            }
            v.Centered = centred;

            double y = r.YawRatio;
            double ay = Math.Abs(y);

            if (required == ScanView.Front)
            {
                if (ay < FrontMaxAbsYaw) { v.Ok = true; v.Verified = true; v.Message = "Facing front"; return v; }
                v.Ok = false;
                v.Verified = true;
                v.Message = "Face the camera straight on";
                return v;
            }

            // side views
            ScanView seen = r.Observed;
            if (seen == required) { v.Ok = true; v.Verified = true; v.Message = ScanViews.Title(required) + " ✓"; return v; }

            v.Verified = true;
            v.Ok = false;
            if (seen == ScanView.Front || seen == ScanView.Any)
            {
                v.Message = required == ScanView.Left
                    ? "Turn a little more toward your right (show your left cheek)"
                    : "Turn a little more toward your left (show your right cheek)";
            }
            else
            {
                v.Message = required == ScanView.Left
                    ? "Wrong side: turn toward your right to show your LEFT cheek"
                    : "Wrong side: turn toward your left to show your RIGHT cheek";
            }
            return v;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _det?.Dispose();
                _det = null;
            }
        }
    }
}
