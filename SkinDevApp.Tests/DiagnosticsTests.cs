using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCvSharp;
using SkinDevApp.AI;
using Xunit;

namespace SkinDevApp.Tests
{
    /// <summary>
    /// Evidence tests for the CNN / YOLO disagreement investigation. They pin down: the C# preprocessing equals the notebook's
    /// (BGR->RGB, bilinear 224, (p/127.5)-1, NHWC); replaying one image is deterministic; and overlapping boxes are classified
    /// without being removed.
    /// </summary>
    public class DiagnosticsTests
    {
        // the exact synthetic image used by golden/make_golden.py
        private static Mat SyntheticBgr()
        {
            var m = new Mat(37, 53, MatType.CV_8UC3);
            for (int y = 0; y < 37; y++)
                for (int x = 0; x < 53; x++)
                    m.Set(y, x, new Vec3b((byte)((x * 7 + y * 3) % 256), (byte)((x * 5 + y * 11) % 256), (byte)((x * 13 + y * 17) % 256)));
            return m;
        }

        private static JsonElement Golden() =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", "preprocess_golden.json"))).RootElement;

        private static string ModelPath()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "SkinDevApp", "Model", "precisionskin.onnx");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // ------------------------------------------------- preprocessing equivalence ---

        [Fact]
        public void Preprocess_MatchesNotebookReference_ValuesLayoutAndShape()
        {
            using (Mat bgr = SyntheticBgr())
            {
                var t = OnnxInference.Preprocess(bgr);
                Assert.Equal(new[] { 1, 224, 224, 3 }, t.Dimensions.ToArray());       // NHWC
                float[] flat = t.ToArray();

                JsonElement g = Golden();
                int stride = g.GetProperty("sample_stride").GetInt32();
                double[] want = g.GetProperty("samples").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                for (int i = 0; i < want.Length; i++)
                    Assert.True(Math.Abs(flat[i * stride] - want[i]) < 2e-6, "sample " + i + ": " + flat[i * stride] + " vs " + want[i]);

                double[] means = g.GetProperty("channel_mean_rgb").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                for (int c = 0; c < 3; c++)
                    Assert.True(Math.Abs(Enumerable.Range(0, 224 * 224).Average(k => (double)flat[k * 3 + c]) - means[c]) < 1e-5, "channel " + c);
            }
        }

        [Fact]
        public void Preprocess_NormalisationIsExactly_Pixel_Over_127_5_Minus_1()
        {
            using (Mat bgr = SyntheticBgr())
            {
                float[] flat = OnnxInference.Preprocess(bgr).ToArray();
                Assert.InRange(flat.Min(), -1.000001f, 1.000001f);   // 255 * (1/127.5) - 1 is 1.00000012 in float32
                Assert.InRange(flat.Max(), -1.000001f, 1.000001f);
                // resize output is 8-bit, so every value must sit on the (p/127.5)-1 grid: no 0..1 scaling, no ImageNet mean/std
                Assert.All(flat.Take(5000), v => Assert.True(Math.Abs((v + 1.0) * 127.5 - Math.Round((v + 1.0) * 127.5)) < 1e-3));
            }
        }

        [Fact]
        public void Preprocess_ChannelOrderIsRgb_NotBgr()
        {
            using (var red = new Mat(10, 10, MatType.CV_8UC3, new Scalar(0, 0, 255)))     // OpenCV BGR: pure red
            {
                float[] flat = OnnxInference.Preprocess(red).ToArray();
                Assert.Equal(1.0f, flat[0], 5);     // first channel = R
                Assert.Equal(-1.0f, flat[1], 5);
                Assert.Equal(-1.0f, flat[2], 5);    // third channel = B
            }
        }

        [Fact]
        public void Preprocess_DoesNotKeepAspectRatio_LikeTheNotebookResize()
        {
            // the notebook's eval pipeline is A.Resize(224,224): a plain stretch, no letterbox and no crop. A wide image must fill the whole tensor.
            using (var wide = new Mat(20, 200, MatType.CV_8UC3, new Scalar(10, 200, 30)))
            {
                float[] flat = OnnxInference.Preprocess(wide).ToArray();
                Assert.All(Enumerable.Range(0, 224 * 224).Where(k => k % 997 == 0), k => Assert.Equal(flat[1], flat[k * 3 + 1], 4));
            }
        }

        // ------------------------------------------------------ deterministic replay ---

        [Fact]
        public void Preprocess_IsBitwiseDeterministic()
        {
            using (Mat bgr = SyntheticBgr())
                Assert.Equal(OnnxInference.Preprocess(bgr).ToArray(), OnnxInference.Preprocess(bgr).ToArray());
        }

        [Fact]
        public void ReplayOfOneImage_GivesIdenticalProbabilities_AndMatchesPythonOnnxRuntime()
        {
            string model = ModelPath();
            Assert.True(model != null, "Model/precisionskin.onnx not found");
            JsonElement g = Golden();

            string sha = BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(model))).Replace("-", "").ToLowerInvariant();
            Assert.Equal(g.GetProperty("model_sha256").GetString(), sha);                 // same weights as the Python run
            Assert.Equal("eea31c7fe89c97b3682119f935a554a94319502e340f8635388223d0446ac746", sha);   // the hash recorded in the saved scans

            using (var onnx = new OnnxInference(model))
            using (Mat bgr = SyntheticBgr())
            {
                float[] a = onnx.Predict(bgr).Probabilities;
                for (int i = 0; i < 4; i++) Assert.Equal(a, onnx.Predict(bgr).Probabilities);          // no temporal state, no randomness

                double[] want = g.GetProperty("probabilities_acne_hyper_eczema_normal").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                for (int c = 0; c < 4; c++) Assert.True(Math.Abs(a[c] - want[c]) < 1e-5, PredictionResult.ClassNames[c] + " " + a[c] + " vs " + want[c]);
            }
        }

        // ------------------------------------------------- overlapping detections ---

        private static LesionBox Box(int cls, float x0, float y0, float x1, float y1, float conf = 0.5f) =>
            new LesionBox { ClassIndex = cls, ClassName = new[] { "Acne", "Hyperpigmentation", "Eczema" }[cls], Confidence = conf, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

        [Fact]
        public void NestedBoxes_AreLabelledNested_NotDuplicate()
        {
            // the two hyperpigmentation boxes of the investigated scan (640 px frame)
            var boxes = new[] { Box(1, 166, 178, 526, 544, 0.411f), Box(1, 136, 189, 423, 359, 0.377f) };
            var pairs = DetectionDiagnostics.Analyse(boxes);
            Assert.Single(pairs);
            Assert.Equal(BoxRelation.Nested, pairs[0].Relation);
            Assert.InRange(pairs[0].Iou, 0.30, 0.34);
            Assert.InRange(pairs[0].ContainmentOfSmaller, 0.85, 0.95);
        }

        [Fact]
        public void NearIdenticalBoxes_AreLabelledDuplicate()
        {
            var pairs = DetectionDiagnostics.Analyse(new[] { Box(1, 100, 100, 200, 200), Box(1, 104, 102, 203, 199) });
            Assert.Equal(BoxRelation.Duplicate, pairs.Single().Relation);
        }

        [Fact]
        public void SeparateBoxes_AndBoxesOfDifferentClasses_AreNotPaired()
        {
            Assert.Empty(DetectionDiagnostics.Analyse(new[] { Box(1, 0, 0, 50, 50), Box(1, 100, 100, 150, 150) }));
            Assert.Empty(DetectionDiagnostics.Analyse(new[] { Box(0, 0, 0, 100, 100), Box(1, 0, 0, 100, 100) }));    // per-class, like the detector's NMS
        }

        [Fact]
        public void Analysis_NeverChangesTheBoxes()
        {
            var boxes = new[] { Box(1, 166, 178, 526, 544, 0.411f), Box(1, 136, 189, 423, 359, 0.377f) };
            DetectionDiagnostics.Analyse(boxes);
            Assert.Equal(2, boxes.Length);
            Assert.Equal(0.411f, boxes[0].Confidence);
            Assert.Equal(136f, boxes[1].X0);
        }

        [Fact]
        public void Iou_IsSymmetric_AndZeroWhenApart()
        {
            var a = Box(1, 0, 0, 10, 10); var b = Box(1, 5, 5, 15, 15); var c = Box(1, 50, 50, 60, 60);
            Assert.Equal(DetectionDiagnostics.Iou(a, b), DetectionDiagnostics.Iou(b, a), 9);
            Assert.Equal(25.0 / 175.0, DetectionDiagnostics.Iou(a, b), 9);
            Assert.Equal(0, DetectionDiagnostics.Iou(a, c));
        }
    }
}
