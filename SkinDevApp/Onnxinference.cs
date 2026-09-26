using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace SkinDevApp.AI
{
    /// <summary>
    /// Fast CPU classifier backed by precisionskin.onnx.
    ///
    /// PREPROCESSING - must stay byte-identical to gradcam_service.py:
    ///     BGR -> RGB -> resize 224x224 (bilinear) -> float32 -> (p / 127.5) - 1.0
    ///
    /// Any deviation here (0..1 scaling, ImageNet mean/std, a different resize
    /// filter, a centre crop) will silently change predictions and make the
    /// Grad-CAM overlay disagree with the displayed class.
    /// </summary>
    public sealed class OnnxInference : IDisposable
    {
        public const int ImageSize = 224;

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private bool _disposed;

        public string InputName => _inputName;
        public string OutputName => _outputName;

        public OnnxInference(string modelPath, int intraOpThreads = 2)
        {
            if (!System.IO.File.Exists(modelPath))
                throw new System.IO.FileNotFoundException("ONNX model not found.", modelPath);

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = intraOpThreads,
                InterOpNumThreads = 1
            };

            _session = new InferenceSession(modelPath, options);

            // Read the real names from the graph instead of hard-coding them.
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();

            Debug.WriteLine($"[ONNX] input='{_inputName}' output='{_outputName}'");

            Warmup();
        }

        /// <summary>
        /// First inference allocates arenas and is 5-10x slower. Burn it at
        /// startup so the user's first Analyze click is not the slow one.
        /// </summary>
        private void Warmup()
        {
            var dummy = new DenseTensor<float>(new[] { 1, ImageSize, ImageSize, 3 });
            using var _ = _session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(_inputName, dummy) });
        }

        /// <summary>
        /// EXACT preprocessing. Public so validation tooling can reuse it.
        /// </summary>
        public static DenseTensor<float> Preprocess(Mat frameBgr)
        {
            if (frameBgr is null || frameBgr.Empty())
                throw new ArgumentException("Empty frame supplied to Preprocess.");

            using var rgb = new Mat();
            Cv2.CvtColor(frameBgr, rgb, ColorConversionCodes.BGR2RGB);

            using var resized = new Mat();
            Cv2.Resize(
                rgb,
                resized,
                new Size(ImageSize, ImageSize),
                interpolation: InterpolationFlags.Linear);   // == cv2.INTER_LINEAR

            using var float32 = new Mat();
            // alpha = 1/127.5, beta = -1.0  =>  (pixel / 127.5) - 1.0
            resized.ConvertTo(float32, MatType.CV_32FC3, 1.0 / 127.5, -1.0);

            var tensor = new DenseTensor<float>(new[] { 1, ImageSize, ImageSize, 3 });

            var buffer = new float[ImageSize * ImageSize * 3];
            System.Runtime.InteropServices.Marshal.Copy(
                float32.Data, buffer, 0, buffer.Length);

            buffer.CopyTo(tensor.Buffer.Span);

            return tensor;
        }

        public PredictionResult Predict(Mat frameBgr)
        {
            // .NET Framework 4.7.2 has no ObjectDisposedException.ThrowIf helper.
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxInference));

            var tensor = Preprocess(frameBgr);

            var sw = Stopwatch.StartNew();

            using var results = _session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });

            sw.Stop();

            var probs = results
                .First(r => r.Name == _outputName)
                .AsEnumerable<float>()
                .ToArray();

            if (probs.Length != PredictionResult.ClassNames.Length)
                throw new InvalidOperationException(
                    $"ONNX returned {probs.Length} outputs, expected " +
                    $"{PredictionResult.ClassNames.Length}. Wrong model file?");

            int best = 0;
            for (int i = 1; i < probs.Length; i++)
                if (probs[i] > probs[best]) best = i;

            return new PredictionResult
            {
                PredictedIndex = best,
                Confidence = probs[best],
                Probabilities = probs,
                InferenceMs = sw.Elapsed.TotalMilliseconds
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _session.Dispose();
            _disposed = true;
        }
    }
}