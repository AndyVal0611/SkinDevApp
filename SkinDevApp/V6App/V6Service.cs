// ============================================================================
// V6Service.cs  -  namespace SkinDevApp.V6   (application-coupled: lives outside the pure V6 folder)
//
// Application-wide holder for the V6 inference service, same pattern as AiEngine. Does NOTHING unless V6FeatureFlag.IsEnabled (default OFF).
// Loaded lazily on the first scan made with the flag on; a failure is remembered and reported, never thrown into a scan.
// The V5 classifier (AiEngine) is not touched.
// ============================================================================

using System;
using System.IO;
using OpenCvSharp;
using SkinDevApp.Data;
using SkinDevApp.Imaging;

namespace SkinDevApp.V6
{
    public static class V6Service
    {
        private static readonly object _lock = new object();
        private static V6Inference _svc;
        private static string _error;
        private static bool _tried;

        /// <summary>Folder next to the exe that holds the V6 deployment config, OOD file and the three ONNX models.</summary>
        public static string DeploymentDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "V6");

        public static string LastError { get { lock (_lock) return _error; } }

        public static bool IsReady { get { lock (_lock) return _svc != null; } }

        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_tried) return;
                _tried = true;
                string err;
                string variant = V6FeatureFlag.Variant;
                if (!V6Inference.TryCreate(DeploymentDir, DeploymentDir, null, out _svc, out err, variant))
                {
                    if (variant != "global_yolo" && V6Inference.TryCreate(DeploymentDir, DeploymentDir, null, out _svc, out string err2, "global_yolo"))
                        _error = null;                                       // the full variant could not load: keep working with global_yolo (the assessment records which variant ran)
                    else _error = err;
                }
            }
        }

        /// <summary>Returns null when the flag is off. Otherwise always returns an assessment (a failure is an INFERENCE_ERROR assessment, never a Normal-looking one).</summary>
        public static UnifiedAssessment Assess(Mat working, FrameQuality quality, bool isUpload = false)
        {
            if (!V6FeatureFlag.IsEnabled) return null;
            EnsureLoaded();
            V6Inference svc; string err;
            lock (_lock) { svc = _svc; err = _error; }
            if (svc == null) return UnifiedAssessment.Failed("V6 models are not available: " + (err ?? "not loaded"));
            return svc.Assess(working, ToQualityInput(quality, working, isUpload));
        }

        /// <summary>Uses the application's own frame-quality verdicts (see V6QualityMapper). No analyser result means quality is "not assessed", so Normal cannot be supported.</summary>
        public static QualityInput ToQualityInput(FrameQuality q, Mat working, bool isUpload = false)
        {
            if (q == null || working == null || working.Empty()) return QualityInput.NotAssessed();
            ScanSettings s = ScanSettings.Current;
            return V6QualityMapper.From(q.CascadeAvailable, q.FaceFound, q.SharpEnough, q.Brightness, q.UnderExposedFraction, q.OverExposedFraction,
                                        working.Width, working.Height, s.MinBrightness, s.MaxBrightness, faceBlocking: !isUpload);
        }
    }
}
