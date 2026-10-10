// ============================================================================
// LesionDetector.cs  -  namespace SkinDevApp.AI
//
// Lesion LOCALIZATION: a YOLOv8 object detector (lesion_detector_v1.onnx) that proposes
// candidate lesion boxes for acne, hyperpigmentation and eczema on the SAME frozen frame
// that the classifier and Grad-CAM++ analyse.
//
// IMPORTANT - what this is and is not
//   * It is a SEPARATE model from the classifier. Its boxes are never used to alter, filter
//     or "improve" the Grad-CAM++ maps or the class scores (and vice versa).
//   * Research prototype, trained on public box-annotated datasets. It misses lesions and
//     draws some wrong boxes. Boxes are CANDIDATE locations, not a diagnosis, not a count.
//   * Eczema is a pilot class (smallest training set).
//
// PREPROCESSING (must match training / export):
//     BGR -> letterbox to SxS (scale to fit, grey 114 padding) -> RGB -> float32 / 255 -> NCHW
// OUTPUT of the ONNX file:  [1, 4 + numClasses, N]   rows: cx, cy, w, h (pixels of the SxS input),
//     then one score per class. There is no objectness row. Boxes are decoded, filtered by a
//     per-class confidence threshold and de-duplicated with per-class NMS.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.AI
{
    /// <summary>One candidate lesion box in pixels of the analysed frame.</summary>
    public sealed class LesionBox
    {
        /// <summary>Palette class index: 0 Acne, 1 Hyperpigmentation, 2 Eczema.</summary>
        public int ClassIndex { get; set; }
        public string ClassName { get; set; }
        public float Confidence { get; set; }
        public float X0 { get; set; }
        public float Y0 { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }

        public float Width => X1 - X0;
        public float Height => Y1 - Y0;
    }

    /// <summary>Everything the detector produced for one frame, plus how it was run.</summary>
    public sealed class LesionDetectionSet
    {
        public List<LesionBox> Boxes { get; set; } = new List<LesionBox>();
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public string ModelFile { get; set; }
        public string ModelSha256 { get; set; }
        public string ModelTag { get; set; }
        public int ImageSize { get; set; }
        public double LatencyMs { get; set; }
        /// <summary>Confidence thresholds used, by palette class index (Acne, Hyperpigmentation, Eczema).</summary>
        public double[] Thresholds { get; set; }

        public int Count(int classIndex) => Boxes.Count(b => b.ClassIndex == classIndex);

        public string Summary()
        {
            if (Boxes.Count == 0) return "No candidate lesion boxes above the thresholds";
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                int n = Count(i);
                if (n > 0) parts.Add(ClassPalette.Names[i] + " " + n);
            }
            return Boxes.Count + " candidate box" + (Boxes.Count == 1 ? "" : "es") + " (" + string.Join(", ", parts) + ")";
        }
    }

    public sealed class LesionDetector : IDisposable
    {
        public const int NumClasses = 3;                     // acne, hyperpigmentation, eczema (palette indices 0..2)
        private const int MaxBoxesPerClass = 60;

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly int _imageSize;
        private readonly float _nmsIou;
        private readonly int[] _paletteOf;                   // detector class id -> palette index
        public string ModelFile { get; }
        public string ModelSha256 { get; }
        public string ModelTag { get; }
        public int ImageSize => _imageSize;

        public LesionDetector(string onnxPath, string metaJsonPath, int intraOpThreads = 2)
        {
            if (!File.Exists(onnxPath))
                throw new FileNotFoundException("Lesion detector ONNX not found.", onnxPath);

            // ---- metadata written by the training notebook (classes, size, NMS IoU) ----
            string[] classes = { "acne", "hyperpigmentation", "eczema" };
            int size = 640;
            float iou = 0.45f;
            string tag = Path.GetFileNameWithoutExtension(onnxPath);
            if (!string.IsNullOrEmpty(metaJsonPath) && File.Exists(metaJsonPath))
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaJsonPath)))
                {
                    JsonElement r = doc.RootElement;
                    if (r.TryGetProperty("classes", out JsonElement c) && c.ValueKind == JsonValueKind.Array)
                        classes = c.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    if (r.TryGetProperty("imgsz", out JsonElement s) && s.TryGetInt32(out int sz)) size = sz;
                    if (r.TryGetProperty("nms_iou", out JsonElement n) && n.TryGetDouble(out double nv)) iou = (float)nv;
                }
            }

            _paletteOf = classes.Select(ClassPalette.IndexOf).ToArray();
            if (_paletteOf.Length != NumClasses || _paletteOf.Any(i => i < 0 || i >= NumClasses))
                throw new InvalidOperationException("Unexpected detector classes: " + string.Join(",", classes));

            _imageSize = size;
            _nmsIou = iou;
            ModelFile = Path.GetFileName(onnxPath);
            ModelTag = tag;

            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(onnxPath))
                ModelSha256 = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = intraOpThreads,
                InterOpNumThreads = 1
            };
            _session = new InferenceSession(onnxPath, options);
            _inputName = _session.InputMetadata.Keys.First();

            // First inference is slow (allocations): burn it now, not on the user's capture.
            using (var blank = new Mat(_imageSize, _imageSize, MatType.CV_8UC3, new Scalar(114, 114, 114)))
                Detect(blank, new[] { 0.99, 0.99, 0.99 });
        }

        /// <param name="thresholds">Confidence thresholds by PALETTE class index (Acne, Hyperpigmentation, Eczema).</param>
        public LesionDetectionSet Detect(Mat bgr, double[] thresholds)
        {
            if (bgr == null || bgr.Empty()) throw new ArgumentException("Empty frame.", nameof(bgr));
            var sw = Stopwatch.StartNew();

            int S = _imageSize, W = bgr.Width, H = bgr.Height;
            float scale = Math.Min((float)S / W, (float)S / H);
            int nw = Math.Max(1, (int)Math.Round(W * scale));
            int nh = Math.Max(1, (int)Math.Round(H * scale));
            int padX = (S - nw) / 2, padY = (S - nh) / 2;

            // ---- letterbox -> RGB -> float / 255 -> NCHW ----
            var data = new float[3 * S * S];
            using (Mat resized = new Mat())
            using (Mat canvas = new Mat(S, S, MatType.CV_8UC3, new Scalar(114, 114, 114)))
            using (Mat rgb = new Mat())
            using (Mat f = new Mat())
            {
                Cv2.Resize(bgr, resized, new Size(nw, nh), 0, 0, InterpolationFlags.Linear);
                using (Mat roi = new Mat(canvas, new Rect(padX, padY, nw, nh)))
                    resized.CopyTo(roi);
                Cv2.CvtColor(canvas, rgb, ColorConversionCodes.BGR2RGB);
                rgb.ConvertTo(f, MatType.CV_32FC3, 1.0 / 255.0);

                Mat[] ch = Cv2.Split(f);
                try
                {
                    for (int c = 0; c < 3; c++)
                        Marshal.Copy(ch[c].Data, data, c * S * S, S * S);
                }
                finally { foreach (Mat m in ch) m.Dispose(); }
            }

            var tensor = new DenseTensor<float>(data, new[] { 1, 3, S, S });
            float[] o;
            int rows, n;
            using (var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) }))
            {
                Tensor<float> t = results.First().AsTensor<float>();
                rows = t.Dimensions[1];
                n = t.Dimensions[2];
                o = t.ToArray();
            }
            if (rows != 4 + NumClasses)
                throw new InvalidOperationException("Unexpected detector output: " + rows + " rows (expected " + (4 + NumClasses) + ").");

            double[] thr = thresholds ?? new[] { 0.20, 0.25, 0.25 };
            var perClass = new List<LesionBox>[NumClasses];
            for (int i = 0; i < NumClasses; i++) perClass[i] = new List<LesionBox>();

            for (int i = 0; i < n; i++)
            {
                int best = 0;
                float bs = o[(4) * n + i];
                for (int c = 1; c < NumClasses; c++)
                {
                    float sc = o[(4 + c) * n + i];
                    if (sc > bs) { bs = sc; best = c; }
                }
                int pal = _paletteOf[best];
                if (bs < thr[pal]) continue;

                float cx = o[0 * n + i], cy = o[1 * n + i], bw = o[2 * n + i], bh = o[3 * n + i];
                float x0 = (cx - bw / 2f - padX) / scale, y0 = (cy - bh / 2f - padY) / scale;
                float x1 = (cx + bw / 2f - padX) / scale, y1 = (cy + bh / 2f - padY) / scale;
                x0 = Math.Max(0, Math.Min(W - 1, x0)); x1 = Math.Max(0, Math.Min(W - 1, x1));
                y0 = Math.Max(0, Math.Min(H - 1, y0)); y1 = Math.Max(0, Math.Min(H - 1, y1));
                if (x1 - x0 < 2 || y1 - y0 < 2) continue;

                perClass[pal].Add(new LesionBox
                {
                    ClassIndex = pal,
                    ClassName = ClassPalette.Names[pal],
                    Confidence = bs,
                    X0 = x0, Y0 = y0, X1 = x1, Y1 = y1
                });
            }

            var set = new LesionDetectionSet
            {
                FrameWidth = W,
                FrameHeight = H,
                ModelFile = ModelFile,
                ModelSha256 = ModelSha256,
                ModelTag = ModelTag,
                ImageSize = S,
                Thresholds = new[] { thr[0], thr[1], thr[2] }
            };
            for (int c = 0; c < NumClasses; c++)
                set.Boxes.AddRange(Nms(perClass[c]));
            set.Boxes = set.Boxes.OrderBy(b => b.ClassIndex).ThenByDescending(b => b.Confidence).ToList();

            sw.Stop();
            set.LatencyMs = sw.Elapsed.TotalMilliseconds;
            return set;
        }

        private List<LesionBox> Nms(List<LesionBox> boxes)
        {
            var keep = new List<LesionBox>();
            foreach (LesionBox b in boxes.OrderByDescending(x => x.Confidence))
            {
                bool dup = false;
                foreach (LesionBox k in keep)
                    if (Iou(b, k) > _nmsIou) { dup = true; break; }
                if (dup) continue;
                keep.Add(b);
                if (keep.Count >= MaxBoxesPerClass) break;
            }
            return keep;
        }

        private static float Iou(LesionBox a, LesionBox b)
        {
            float iw = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
            float ih = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
            if (iw <= 0 || ih <= 0) return 0f;
            float inter = iw * ih;
            float u = a.Width * a.Height + b.Width * b.Height - inter;
            return u > 0 ? inter / u : 0f;
        }

        public void Dispose() { _session?.Dispose(); }
    }

    /// <summary>Application-wide holder for the lesion detector (same pattern as AiEngine).</summary>
    public static class LesionEngine
    {
        public static readonly string ModelPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "lesion_detector_v1.onnx");
        public static readonly string MetaPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "lesion_detector_v1.json");

        private static readonly object _lock = new object();
        private static LesionDetector _detector;
        private static Exception _loadError;

        public static bool IsAvailable { get { lock (_lock) { return _detector != null; } } }
        public static string LoadErrorMessage { get { lock (_lock) { return _loadError?.Message; } } }

        /// <summary>Safe to call repeatedly and from a background thread.</summary>
        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_detector != null || _loadError != null) return;
                try { _detector = new LesionDetector(ModelPath, MetaPath); }
                catch (Exception ex) { _loadError = ex; }
            }
        }

        /// <summary>Runs the detector. Throws if the model is unavailable (callers catch: a scan never fails because of it).</summary>
        public static LesionDetectionSet Detect(Mat bgr, double[] thresholds)
        {
            EnsureLoaded();
            LesionDetector d;
            lock (_lock) { d = _detector; }
            if (d == null) throw new InvalidOperationException(_loadError != null ? _loadError.Message : "Lesion detector not loaded.");
            return d.Detect(bgr, thresholds);
        }
    }

    /// <summary>Draws the candidate boxes in the class colours (Acne red, Hyperpigmentation blue, Eczema yellow/orange).</summary>
    public static class LesionOverlay
    {
        public static Mat Draw(Mat frameBgr, LesionDetectionSet set, bool labels = true)
        {
            Mat o = frameBgr.Clone();
            if (set == null) return o;

            int longSide = Math.Max(o.Width, o.Height);
            int th = Math.Max(2, longSide / 600);                         // thin, exact outline: the box edge is the detector's edge, not a fat marker
            double fs = Math.Max(0.4, longSide / 1100.0);
            bool writeLabels = labels && set.Boxes.Count <= 12;

            // weakest first, so the most confident boxes are never hidden under weaker ones
            foreach (LesionBox b in set.Boxes.OrderBy(x => x.Confidence))
            {
                Scalar col = ClassPalette.ColorsBgr[b.ClassIndex];
                var p0 = new Point((int)Math.Round(b.X0), (int)Math.Round(b.Y0));
                var p1 = new Point((int)Math.Round(b.X1), (int)Math.Round(b.Y1));
                Cv2.Rectangle(o, p0, p1, Scalar.White, th + 2, LineTypes.AntiAlias);     // thin white halo: readable on any skin tone
                Cv2.Rectangle(o, p0, p1, col, th, LineTypes.AntiAlias);
                if (writeLabels)
                {
                    string txt = b.ClassName + " " + b.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    var sz = Cv2.GetTextSize(txt, HersheyFonts.HersheySimplex, fs, 1, out int baseline);
                    int ty = Math.Max(sz.Height + 2, p0.Y - 3);
                    Cv2.Rectangle(o, new Point(p0.X, ty - sz.Height - 3), new Point(p0.X + sz.Width + 4, ty + 2), col, -1);
                    Cv2.PutText(o, txt, new Point(p0.X + 2, ty - 1), HersheyFonts.HersheySimplex, fs, Scalar.White, 1, LineTypes.AntiAlias);
                }
            }

            if (labels && set.Boxes.Count > 0)                           // count legend (the per-box labels are skipped when there are many boxes)
            {
                int y = 6;
                foreach (var grp in set.Boxes.GroupBy(x => x.ClassIndex).OrderBy(g => g.Key))
                {
                    string txt = ClassPalette.Names[grp.Key] + " " + grp.Count() + (grp.Count() == 1 ? " box" : " boxes");
                    var sz = Cv2.GetTextSize(txt, HersheyFonts.HersheySimplex, fs, 1, out int bl);
                    Cv2.Rectangle(o, new Point(6, y), new Point(6 + sz.Width + 8, y + sz.Height + 8), ClassPalette.ColorsBgr[grp.Key], -1);
                    Cv2.PutText(o, txt, new Point(10, y + sz.Height + 3), HersheyFonts.HersheySimplex, fs, Scalar.White, 1, LineTypes.AntiAlias);
                    y += sz.Height + 12;
                }
            }
            return o;
        }
    }
}
