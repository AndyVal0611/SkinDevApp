// ============================================================================
// V6Inference.cs  -  namespace SkinDevApp.V6
//
// The V6 inference service: ONE frame in, ONE structured UnifiedAssessment out.
//
//   frame -> Global MobileNetV2 (embedding + logits)           ┐
//   frame -> 3 YOLOv8 ensembles (candidate pool, unfiltered)   ┴-> det_ev (21) -> global_yolo FUSION (trained) -> Platt -> thresholds -> decision
//
// The trained fusion model is the decision mechanism. There is no voting, no probability averaging and no model forced to agree with
// another. A missing detector is "unavailable", never "no detection". Any failure becomes INFERENCE_ERROR, never a Normal-looking result.
//
// This class does not touch the V5 classifier (AiEngine / OnnxInference.Predict) and is not called by anything unless the feature flag
// V6FeatureFlag.IsEnabled is switched on by a later phase. EXPERIMENTAL, development-stage, not clinically validated.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SkinDevApp.AI;

namespace SkinDevApp.V6
{
    /// <summary>Supplies the detector candidate pools for a frame. Replaceable in tests.</summary>
    public interface IDetectorSource
    {
        /// <summary>One pool per condition in palette order (Acne, Hyperpigmentation, Eczema). Never throws for a single failed detector.</summary>
        ConditionPool[] Collect(Mat frameBgr, V6Config cfg);
    }

    /// <summary>Uses the three specialist ensembles that LesionEngine already loads, WITHOUT the deployed threshold (pool down to 0.05) and WITHOUT the face filter.</summary>
    public sealed class LesionEngineDetectorSource : IDetectorSource
    {
        private readonly Func<List<LesionDetector>> _loader;

        /// <param name="loader">Optional: supplies the detectors instead of LesionEngine (the parity tests load them from the repository Model folder).</param>
        public LesionEngineDetectorSource(Func<List<LesionDetector>> loader = null) { _loader = loader; }

        public ConditionPool[] Collect(Mat bgr, V6Config cfg)
        {
            var pools = new ConditionPool[V6Conditions.Count];
            for (int j = 0; j < pools.Length; j++)
                pools[j] = new ConditionPool { Condition = V6Conditions.Names[j], Available = false, DeployedThreshold = cfg.DeployedThresholds[j], Error = "detector not installed" };
            List<LesionDetector> loaded;
            try { if (_loader == null) LesionEngine.EnsureLoaded(); loaded = _loader != null ? _loader() : LesionEngine.LoadedDetectors(); }
            catch (Exception ex) { foreach (ConditionPool p in pools) p.Error = "detector load failed: " + ex.Message; return pools; }

            double[] low = { cfg.PoolMinConf, cfg.PoolMinConf, cfg.PoolMinConf };           // pool threshold, not the deployed one
            var cache = new Dictionary<LesionDetector, LesionDetectionSet>();
            var failed = new Dictionary<LesionDetector, string>();
            for (int j = 0; j < pools.Length; j++)
            {
                LesionDetector sp = loaded.Where(d => d.PaletteClasses.Contains(j)).OrderBy(d => d.PaletteClasses.Length).FirstOrDefault();   // a specialist beats a 3-class model
                if (sp == null) continue;
                try
                {
                    if (failed.ContainsKey(sp)) throw new InvalidOperationException(failed[sp]);
                    if (!cache.TryGetValue(sp, out LesionDetectionSet set)) { set = sp.Detect(bgr, low); cache[sp] = set; }
                    IEnumerable<PixelBox> boxes = set.Boxes.Where(b => b.ClassIndex == j).Select(b => new PixelBox { Conf = b.Confidence, X0 = b.X0, Y0 = b.Y0, X1 = b.X1, Y1 = b.Y1 });
                    pools[j] = DetectorEvidence.FromPixelBoxes(V6Conditions.Names[j], true, boxes, bgr.Width, bgr.Height, cfg.DeployedThresholds[j], cfg.PoolMinConf, cfg.MaxBoxesPerCondition);
                }
                catch (Exception ex)
                {
                    failed[sp] = ex.Message;
                    pools[j] = new ConditionPool { Condition = V6Conditions.Names[j], Available = false, DeployedThreshold = cfg.DeployedThresholds[j], Error = ex.GetType().Name + ": " + ex.Message };
                }
            }
            return pools;
        }
    }

    public sealed class V6Inference : IDisposable
    {
        private readonly V6Config _cfg;
        private readonly OodModel _ood;
        private readonly IDetectorSource _detectors;
        private readonly InferenceSession _global, _fusion, _fusionGlobalOnly;
        private readonly string _globalInput;
        private readonly Dictionary<string, string> _sha;
        private bool _disposed;

        public V6Config Config => _cfg;

        private static readonly string[] FusionInputs = { "g_emb", "g_logit", "g_extra", "det_ev", "roi_feat", "roi_mask" };
        private const int Roi = 12, RoiFeat = 83, EmbDim = 128;

        private V6Inference(V6Config cfg, OodModel ood, IDetectorSource det, InferenceSession g, InferenceSession f, InferenceSession fo, Dictionary<string, string> sha)
        {
            _cfg = cfg; _ood = ood; _detectors = det; _global = g; _fusion = f; _fusionGlobalOnly = fo; _sha = sha;
            _globalInput = g.InputMetadata.Keys.First();
        }

        /// <summary>Verifies every model hash against the deployment config and every tensor name before returning. Returns false with a reason instead of throwing.</summary>
        public static bool TryCreate(string deploymentDir, string modelDir, IDetectorSource detectors, out V6Inference service, out string error)
        {
            service = null; error = null;
            InferenceSession g = null, f = null, fo = null;
            try
            {
                V6Config cfg = V6Config.Load(Path.Combine(deploymentDir, "v6_deployment_config.json"));
                OodModel ood = OodModel.Load(Path.Combine(deploymentDir, cfg.OodFile));
                if (Math.Abs(ood.Threshold - cfg.OodThreshold) > 1e-9) throw new InvalidDataException("OOD threshold in the config and in the OOD file differ");
                var sha = new Dictionary<string, string>();
                foreach (ModelFileSpec m in new[] { cfg.GlobalFeatures, cfg.FusionGlobalYolo, cfg.FusionGlobalOnly })
                {
                    string p = Path.Combine(modelDir, m.File);
                    if (!File.Exists(p)) throw new FileNotFoundException("V6 model file not found: " + m.File, p);
                    string h = Sha256(p);
                    if (!string.Equals(h, m.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("V6 model hash mismatch for " + m.File + " (file is not the verified export)");
                    sha[m.File] = h;
                }
                var opt = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, IntraOpNumThreads = 2, InterOpNumThreads = 1 };
                g = new InferenceSession(Path.Combine(modelDir, cfg.GlobalFeatures.File), opt);
                f = new InferenceSession(Path.Combine(modelDir, cfg.FusionGlobalYolo.File), opt);
                fo = new InferenceSession(Path.Combine(modelDir, cfg.FusionGlobalOnly.File), opt);
                if (g.InputMetadata.Count != 1 || g.OutputMetadata.Count != 2) throw new InvalidDataException("Global model must have 1 input and 2 outputs (embedding, logits)");
                foreach (InferenceSession s in new[] { f, fo })
                    foreach (string n in FusionInputs)
                        if (!s.InputMetadata.ContainsKey(n)) throw new InvalidDataException("fusion model lacks input '" + n + "'");
                service = new V6Inference(cfg, ood, detectors ?? new LesionEngineDetectorSource(), g, f, fo, sha);
                return true;
            }
            catch (Exception ex)
            {
                g?.Dispose(); f?.Dispose(); fo?.Dispose();
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public UnifiedAssessment Assess(Mat frameBgr, QualityInput quality)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(V6Inference));
            var sw = Stopwatch.StartNew();
            try
            {
                if (frameBgr == null || frameBgr.Empty()) throw new ArgumentException("empty frame");

                // ---- Global MobileNetV2: embedding + 3 logits (same preprocessing as V5: BGR->RGB, 224 bilinear, (p/127.5)-1) ----
                float[] emb = null, logits = null;
                using (var res = _global.Run(new[] { NamedOnnxValue.CreateFromTensor(_globalInput, OnnxInference.Preprocess(frameBgr)) }))
                    foreach (DisposableNamedOnnxValue r in res)
                    {
                        float[] v = r.AsEnumerable<float>().ToArray();
                        if (v.Length == EmbDim) emb = v; else if (v.Length == V6Conditions.Count) logits = v;
                    }
                if (emb == null || logits == null) throw new InvalidDataException("Global model did not return a 128-d embedding and 3 logits");

                // ---- detector evidence (missing detector = unavailable, never "no detection") ----
                ConditionPool[] pools = _detectors.Collect(frameBgr, _cfg);
                float[] det = DetectorEvidence.Build(pools);

                // ---- trained global_yolo fusion (the decision), then calibration ----
                float[] gLogit = logits.Select(z => V6Decision.GlobalLogitInput(z)).ToArray();
                double[] zFused = RunFusion(_fusion, emb, gLogit, det);
                double[] zGlobalOnly = RunFusion(_fusionGlobalOnly, emb, gLogit, new float[DetectorEvidence.Total]);   // Global-only variant: detector evidence hidden; used for the conflict flag only
                double[] pFused = V6Decision.ApplyPlatt(zFused, _cfg.GlobalYolo.Platt);
                double[] pGlobalOnly = V6Decision.ApplyPlatt(zGlobalOnly, _cfg.GlobalOnly.Platt);

                double oodScore = _ood.Score(emb); bool oodFlag = _ood.IsOut(oodScore);
                bool[] avail = new bool[V6Conditions.Count], detected = new bool[V6Conditions.Count];
                for (int j = 0; j < V6Conditions.Count; j++) { avail[j] = det[j * DetectorEvidence.PerCondition] > 0.5f; detected[j] = det[j * DetectorEvidence.PerCondition + 1] > 0.5f; }

                List<string> flags = V6Decision.Flags(pFused, _cfg.GlobalYolo.Thresholds, pGlobalOnly, _cfg.GlobalOnly.Thresholds, avail, detected, _cfg.Band, oodFlag, quality);
                DecisionResult d = V6Decision.Decide(pFused, _cfg.GlobalYolo.Thresholds, _cfg.Band, avail, flags, oodFlag, quality);

                var a = new UnifiedAssessment
                {
                    State = d.State, Flags = d.Flags, Reasons = d.Reasons, FrameWidth = frameBgr.Width, FrameHeight = frameBgr.Height,
                    OodScore = oodScore, OodFlag = oodFlag, RunId = _cfg.RunId, FeatureSchemaVersion = _cfg.FeatureSchemaVersion,
                    ModelSha256 = new Dictionary<string, string>(_sha)
                };
                a.Conditions = Enumerable.Range(0, V6Conditions.Count).Select(j => new ConditionAssessment
                {
                    Condition = V6Conditions.Names[j], FusionLogit = zFused[j], CalibratedProbability = pFused[j], Threshold = _cfg.GlobalYolo.Thresholds[j],
                    Status = d.Status[j], AbsenceSupported = d.AbsenceSupported[j], DetectorAvailable = avail[j], DetectorFired = detected[j], GlobalOnlyProbability = pGlobalOnly[j]
                }).ToArray();
                for (int j = 0; j < pools.Length; j++)
                    if (pools[j] != null && pools[j].Available)
                        foreach (PixelBox b in pools[j].PixelBoxes)
                        {
                            bool deployed = b.Conf >= pools[j].DeployedThreshold;
                            var box = new V6Box { Condition = V6Conditions.Names[j], Confidence = (float)b.Conf, X0 = (float)b.X0, Y0 = (float)b.Y0, X1 = (float)b.X1, Y1 = (float)b.Y1, Deployed = deployed };
                            a.Pool.Add(box);
                            if (deployed) a.Boxes.Add(box);
                        }
                foreach (ConditionPool p in pools.Where(p => p != null && !p.Available))
                    a.Reasons.Add("detector unavailable for " + p.Condition + (p.Error != null ? " (" + p.Error + ")" : ""));
                a.InferenceMs = sw.Elapsed.TotalMilliseconds;
                return a;
            }
            catch (Exception ex)
            {
                UnifiedAssessment f = UnifiedAssessment.Failed(ex.GetType().Name + ": " + ex.Message, _cfg);
                f.InferenceMs = sw.Elapsed.TotalMilliseconds;
                return f;
            }
        }

        /// <summary>Runs one fusion model on one image's inputs (ROI inputs are zeros). Public so the parity tests can feed fixtures directly.</summary>
        public static double[] RunFusion(InferenceSession s, float[] emb, float[] gLogit, float[] det)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("g_emb", new DenseTensor<float>(emb, new[] { 1, EmbDim })),
                NamedOnnxValue.CreateFromTensor("g_logit", new DenseTensor<float>(gLogit, new[] { 1, V6Conditions.Count })),
                NamedOnnxValue.CreateFromTensor("g_extra", new DenseTensor<float>(new float[2], new[] { 1, 2 })),       // no softmax Normal in the multilabel Global model
                NamedOnnxValue.CreateFromTensor("det_ev", new DenseTensor<float>(det, new[] { 1, DetectorEvidence.Total })),
                NamedOnnxValue.CreateFromTensor("roi_feat", new DenseTensor<float>(new float[Roi * RoiFeat], new[] { 1, Roi, RoiFeat })),   // global_yolo has no ROI branch
                NamedOnnxValue.CreateFromTensor("roi_mask", new DenseTensor<float>(new float[Roi], new[] { 1, Roi }))
            };
            using (var res = s.Run(inputs))
            {
                float[] o = res.First().AsEnumerable<float>().ToArray();
                if (o.Length != V6Conditions.Count) throw new InvalidDataException("fusion returned " + o.Length + " outputs, expected 3");
                return o.Select(x => (double)x).ToArray();
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _global.Dispose(); _fusion.Dispose(); _fusionGlobalOnly.Dispose();
            _disposed = true;
        }
    }
}
