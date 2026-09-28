// ============================================================================
// REFERENCE IMPLEMENTATION - compare against your existing C# classes.
// Assumes OpenCvSharp4 + Microsoft.ML.OnnxRuntime.
// Fully compatible with .NET Framework 4.x (.NET Standard 2.0).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace System.Runtime.CompilerServices
{
    // Fixes CS0518 for .NET Framework C# 9+ syntax support
    internal static class IsExternalInit { }
}

namespace PrecisionSkin.Reference
{
    public static class ClassMap
    {
        // Must equal labels.json / gradcam_service.py CLASS_NAMES, index for index.
        public static readonly string[] Names = { "Acne", "Hyperpigmentation", "Eczema", "Normal" };
    }

    // ---- 1. Preprocessing: byte-for-byte the training/service path -----------
    public static class Preprocessor
    {
        /// <param name="frameBgr">The frame as captured (BGR, 8-bit). NOT flipped,
        /// NOT cropped, NOT already resized. Same Mat is later sent to Grad-CAM.</param>
        public static DenseTensor<float> ToTensor(Mat frameBgr)
        {
            using (var rgb = new Mat())
            {
                Cv2.CvtColor(frameBgr, rgb, ColorConversionCodes.BGR2RGB);          // BGR -> RGB
                using (var small = new Mat())
                {
                    Cv2.Resize(rgb, small, new Size(224, 224), 0, 0, InterpolationFlags.Linear); // squash, bilinear
                    var t = new DenseTensor<float>(new[] { 1, 224, 224, 3 });           // NHWC, matches ONNX
                    for (int y = 0; y < 224; y++)
                    {
                        for (int x = 0; x < 224; x++)
                        {
                            var p = small.At<Vec3b>(y, x);                               // R,G,B after conversion
                            t[0, y, x, 0] = (p.Item0 / 127.5f) - 1.0f;
                            t[0, y, x, 1] = (p.Item1 / 127.5f) - 1.0f;
                            t[0, y, x, 2] = (p.Item2 / 127.5f) - 1.0f;
                        }
                    }
                    return t;
                }
            }
        }

        // Same 8 sample points as gradcam_service.FINGERPRINT_POINTS.
        static readonly (int r, int c, int k)[] Pts =
            { (0,0,0),(0,223,1),(112,112,2),(223,0,0),(223,223,1),(56,168,2),(168,56,0),(112,60,1) };

        /// Write this to JSON next to the source image path, then run
        /// tools/verify_parity.py --csharp-fingerprint <file>.
        public static object Fingerprint(DenseTensor<float> t, string file)
        {
            var s = new float[Pts.Length]; double sum = 0;
            for (int i = 0; i < Pts.Length; i++) s[i] = t[0, Pts[i].r, Pts[i].c, Pts[i].k];
            foreach (var v in t.ToArray()) sum += v;
            return new { file, samples = s, mean = sum / t.Length };
        }
    }

    // ---- 2. ONNX classification ---------------------------------------------
    public sealed class OnnxClassifier : IDisposable
    {
        readonly InferenceSession _s;
        readonly string _in, _out;
        public OnnxClassifier(string path)
        {
            _s = new InferenceSession(path);
            _in = _s.InputMetadata.Keys.First();      // "input_layer_1" - read it, don't hard-code
            _out = _s.OutputMetadata.Keys.First();    // "dense_2"
        }
        public float[] Classify(DenseTensor<float> t)
        {
            using (var r = _s.Run(new[] { NamedOnnxValue.CreateFromTensor(_in, t) }))
            {
                var p = r.First(v => v.Name == _out).AsEnumerable<float>().ToArray();
                // The model's last layer is softmax: p already sums to 1.
                // DO NOT apply Softmax again.
                return p;
            }
        }
        public void Dispose() => _s.Dispose();
    }

    // ---- 3. Latest-frame-wins Grad-CAM worker -------------------------------
    public sealed class StampedFrame
    {
        public long Id { get; }
        public Mat Bgr { get; }
        public DateTime CapturedUtc { get; }

        public StampedFrame(long id, Mat bgr, DateTime capturedUtc)
        {
            Id = id;
            Bgr = bgr;
            CapturedUtc = capturedUtc;
        }
    }

    public sealed class HeatResult
    {
        public long FrameId;                 // frame that was actually explained
        public Mat Heat01;                   // CV_32F [0..1], SAME size as that frame
        public DateTime CapturedUtc;
        public Mat SourceGray;               // small grey copy of that frame, for motion check
    }

    public sealed class LatestFrameGradCam : IDisposable
    {
        readonly object _gate = new object();
        StampedFrame _pending;               // single slot: newer frame REPLACES older one
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public HeatResult Latest { get; private set; }

        /// Camera thread calls this for every frame. O(1), never blocks, never queues.
        public void Offer(StampedFrame f)
        {
            lock (_gate) { _pending?.Bgr.Dispose(); _pending = f; }
        }

        public Task Start(Func<Mat, long, Task<(long id, Mat heat)>> callService) => Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                StampedFrame f; lock (_gate) { f = _pending; _pending = null; }
                if (f == null) { await Task.Delay(20); continue; }
                try
                {
                    var (id, heat) = await callService(f.Bgr, f.Id);
                    if (id != f.Id) { heat.Dispose(); continue; }               // echoed frame_id mismatch: discard
                    if (heat.Width != f.Bgr.Width || heat.Height != f.Bgr.Height)
                        Cv2.Resize(heat, heat, f.Bgr.Size(), 0, 0, InterpolationFlags.Cubic);
                    var g = new Mat(); Cv2.CvtColor(f.Bgr, g, ColorConversionCodes.BGR2GRAY);
                    Cv2.Resize(g, g, new Size(160, 90));
                    Latest = new HeatResult { FrameId = f.Id, Heat01 = heat, CapturedUtc = f.CapturedUtc, SourceGray = g };
                }
                finally { f.Bgr.Dispose(); }
            }
        });

        /// Decide whether the last heatmap may be drawn over the CURRENT preview frame.
        public double Opacity(Mat currentGray160x90, double maxAgeMs = 1500, double maxShiftFrac = 0.03)
        {
            var h = Latest; if (h == null) return 0;
            double age = (DateTime.UtcNow - h.CapturedUtc).TotalMilliseconds;
            if (age > maxAgeMs) return 0;
            using (var a = new Mat()) using (var b = new Mat())
            {
                h.SourceGray.ConvertTo(a, MatType.CV_32F); currentGray160x90.ConvertTo(b, MatType.CV_32F);
                var shift = Cv2.PhaseCorrelate(a, b, null, out _);                  // pixels at 160 px width
                double frac = Math.Sqrt(shift.X * shift.X + shift.Y * shift.Y) / 160.0;

                // Replaced Math.Clamp with .NET Framework compatible Math.Max/Math.Min
                double clampedValue = Math.Max(0.0, Math.Min(1.0, 1.0 - age / maxAgeMs));
                return frac >= maxShiftFrac ? 0 : clampedValue;
            }
        }

        public void Dispose() => _cts.Cancel();
    }
}