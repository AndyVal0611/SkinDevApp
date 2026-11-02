using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Data;
using SkinDevApp.V6;
using Xunit;
using Xunit.Abstractions;

namespace SkinDevApp.Tests
{
    /// <summary>
    /// Phase 5: C# against the Python reference on REAL photographs (v6/deployment/v6_real_photo_golden.json, made by v6/tools/make_parity_golden.py).
    /// The reference was itself checked against the detections the fusion model was trained on (same box counts, zero difference on all 13 photographs).
    /// Covers: the unchanged V5 classifier, detector candidate pools and image-coordinate mapping (letterbox, square and non-square frames), the 21-number evidence,
    /// the fusion logits, calibration, flags, states and supporting boxes; and the flag-off / historical-scan behaviour of the session loader.
    /// Photographs are NOT copied into the repository: they are read from LUMYVUE_PARITY_PHOTOS or Downloads\PrecisionSkin\Classes and verified by SHA-256.
    /// </summary>
    [Collection("V6Flag")]
    public class V6RealPhotoParityTests
    {
        private readonly ITestOutputHelper _out;
        public V6RealPhotoParityTests(ITestOutputHelper output) { _out = output; }

        // ------------------------------------------------------------------ locations
        private static string Up(string relative)
        {
            string d = AppContext.BaseDirectory;
            while (d != null) { string p = Path.Combine(d, relative); if (File.Exists(p) || Directory.Exists(p)) return p; d = Path.GetDirectoryName(d); }
            throw new FileNotFoundException("not found above " + AppContext.BaseDirectory + ": " + relative);
        }

        private static string AppModelDir() => Path.GetDirectoryName(Up(Path.Combine("SkinDevApp", "Model", "lesion_detector_acne.onnx")));
        private static string ExportDir() => Up(Path.Combine("v6", "astra_runs", "open_run02", "export"));
        private static string DeployDir => Path.Combine(AppContext.BaseDirectory, "v6deploy");

        private static string PhotoRoot()
        {
            string root = Environment.GetEnvironmentVariable("LUMYVUE_PARITY_PHOTOS");
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PrecisionSkin", "Classes");
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Parity photographs not found. Set LUMYVUE_PARITY_PHOTOS to the PrecisionSkin\\Classes folder. Looked in: " + root);
            return root;
        }

        private static readonly Lazy<List<LesionDetector>> Detectors = new Lazy<List<LesionDetector>>(() =>
        {
            string dir = AppModelDir(); var list = new List<LesionDetector>();
            foreach (string c in new[] { "acne", "hyperpigmentation", "eczema" })      // the same files LesionEngine loads, in the same way (primary + _m2 + _m3 peers)
            {
                var sp = new LesionDetector(Path.Combine(dir, "lesion_detector_" + c + ".onnx"), Path.Combine(dir, "lesion_detector_" + c + ".json"));
                for (int k = 2; k <= 3; k++) { string extra = Path.Combine(dir, "lesion_detector_" + c + "_m" + k + ".onnx"); if (File.Exists(extra)) sp.Peers.Add(new LesionDetector(extra, Path.Combine(dir, "lesion_detector_" + c + ".json"))); }
                list.Add(sp);
            }
            return list;
        });

        // ------------------------------------------------------------------ golden
        private sealed class GBox { public double Conf, X0, Y0, X1, Y1, RelArea, Cx, Cy; public bool Deployed; }
        private sealed class Photo
        {
            public string Kind, ImageId, RelPath, Sha256; public int Width, Height;
            public double[] V5, Logits, DetEv, LogitsYolo, PYolo, POnly; public GBox[][] Pools; public string[] Flags, Decisions; public string State;
        }

        private static double[] D(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

        private static List<Photo> Golden()
        {
            JsonElement root = JsonDocument.Parse(File.ReadAllText(Path.Combine(DeployDir, "v6_real_photo_golden.json"))).RootElement;
            var cacheCheck = root.GetProperty("replica_vs_training_cache");
            Assert.Equal(cacheCheck.GetProperty("photos").GetInt32(), cacheCheck.GetProperty("same_box_count_for_all_conditions").GetInt32());   // the reference reproduces the training-time detections
            Assert.True(cacheCheck.GetProperty("max_abs_diff_conf_cx_cy_area").GetDouble() < 1e-6);
            var list = new List<Photo>();
            foreach (JsonElement p in root.GetProperty("photos").EnumerateArray())
                list.Add(new Photo
                {
                    Kind = p.GetProperty("kind").GetString(), ImageId = p.GetProperty("image_id").GetString(), RelPath = p.GetProperty("rel_path").GetString(), Sha256 = p.GetProperty("sha256").GetString(),
                    Width = p.GetProperty("width").GetInt32(), Height = p.GetProperty("height").GetInt32(), V5 = D(p.GetProperty("v5_probs")), Logits = D(p.GetProperty("logits")), DetEv = D(p.GetProperty("det_ev")),
                    LogitsYolo = D(p.GetProperty("fusion_global_yolo_logits")), PYolo = D(p.GetProperty("calibrated_global_yolo")), POnly = D(p.GetProperty("calibrated_global_only")),
                    Flags = p.GetProperty("flags").EnumerateArray().Select(x => x.GetString()).ToArray(), State = p.GetProperty("state").GetString(), Decisions = p.GetProperty("decisions").EnumerateArray().Select(x => x.GetString()).ToArray(),
                    Pools = V6Conditions.Names.Select(c => p.GetProperty("pools").GetProperty(c).EnumerateArray().Select(b => new GBox
                    {
                        Conf = b.GetProperty("conf").GetDouble(), X0 = b.GetProperty("x0").GetDouble(), Y0 = b.GetProperty("y0").GetDouble(), X1 = b.GetProperty("x1").GetDouble(), Y1 = b.GetProperty("y1").GetDouble(),
                        RelArea = b.GetProperty("rel_area").GetDouble(), Cx = b.GetProperty("cx").GetDouble(), Cy = b.GetProperty("cy").GetDouble(), Deployed = b.GetProperty("deployed").GetBoolean()
                    }).ToArray()).ToArray()
                });
            return list;
        }

        private static Mat Load(Photo p)
        {
            string path = Path.Combine(PhotoRoot(), p.RelPath);
            using (var sha = SHA256.Create()) using (FileStream fs = File.OpenRead(path))
                Assert.Equal(p.Sha256, BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant());      // the photograph is the one the reference used
            Mat m = Cv2.ImRead(path, ImreadModes.Color);
            Assert.False(m.Empty(), "could not read " + path); Assert.Equal(p.Width, m.Width); Assert.Equal(p.Height, m.Height);
            return m;
        }

        // ------------------------------------------------------------------ V5 baseline unchanged
        [Fact]
        public void V5_classifier_output_is_unchanged_on_real_photos()
        {
            using (var v5 = new OnnxInference(Path.Combine(AppModelDir(), "precisionskin.onnx")))
                foreach (Photo p in Golden())
                    using (Mat m = Load(p))
                    {
                        float[] probs = v5.Predict(m).Probabilities;
                        for (int i = 0; i < 4; i++) Assert.True(Math.Abs(probs[i] - p.V5[i]) <= 1e-4, $"{p.ImageId}: V5 class {i} C# {probs[i]} vs Python {p.V5[i]}");
                    }
        }

        // ------------------------------------------------------------------ detector pools, coordinates and evidence
        [Fact]
        public void Detector_candidate_pools_and_image_coordinates_match_python_on_real_photos()
        {
            V6Config cfg = V6Config.Load(Path.Combine(DeployDir, "v6_deployment_config.json")); var src = new LesionEngineDetectorSource(() => Detectors.Value);
            List<Photo> photos = Golden();
            Assert.Contains(photos, p => p.Width != p.Height); Assert.Contains(photos, p => p.Width == p.Height);          // non-square AND square frames: letterbox padding on both axes
            int boxes = 0; double worstPx = 0, worstConf = 0;
            foreach (Photo p in photos)
                using (Mat m = Load(p))
                {
                    ConditionPool[] pools = src.Collect(m, cfg);
                    for (int j = 0; j < 3; j++)
                    {
                        Assert.True(pools[j].Available, $"{p.ImageId}: {V6Conditions.Names[j]} detector unavailable: {pools[j].Error}");
                        GBox[] g = p.Pools[j].OrderByDescending(b => b.Conf).ToArray(); List<EvidenceBox> e = pools[j].Boxes; List<PixelBox> px = pools[j].PixelBoxes;
                        Assert.True(g.Length == e.Count, $"{p.ImageId} {V6Conditions.Names[j]}: pool size C# {e.Count} vs Python {g.Length}");
                        for (int i = 0; i < g.Length; i++)
                        {
                            worstConf = Math.Max(worstConf, Math.Abs(g[i].Conf - e[i].Conf));
                            double dx = Math.Max(Math.Max(Math.Abs(g[i].X0 - px[i].X0), Math.Abs(g[i].X1 - px[i].X1)), Math.Max(Math.Abs(g[i].Y0 - px[i].Y0), Math.Abs(g[i].Y1 - px[i].Y1))); worstPx = Math.Max(worstPx, dx);
                            Assert.True(Math.Abs(g[i].Conf - e[i].Conf) <= 2e-3, $"{p.ImageId} {V6Conditions.Names[j]} box {i}: confidence");
                            Assert.True(dx <= 0.5, $"{p.ImageId} {V6Conditions.Names[j]} box {i}: coordinates differ by {dx} px");
                            Assert.True(Math.Abs(g[i].Cx - e[i].Cx) <= 2e-3 && Math.Abs(g[i].Cy - e[i].Cy) <= 2e-3, $"{p.ImageId} {V6Conditions.Names[j]} box {i}: centre as a fraction of the frame");
                            Assert.True(Math.Abs(g[i].RelArea - e[i].RelArea) <= 2e-3, $"{p.ImageId} {V6Conditions.Names[j]} box {i}: relative area");
                            Assert.Equal(g[i].Deployed, e[i].Deployed);
                            Assert.InRange(px[i].X0, 0, p.Width); Assert.InRange(px[i].X1, 0, p.Width); Assert.InRange(px[i].Y0, 0, p.Height); Assert.InRange(px[i].Y1, 0, p.Height);   // inside the frame
                            boxes++;
                        }
                        float[] ev = DetectorEvidence.Build(pools);
                        for (int k = 0; k < 21; k++) Assert.True(Math.Abs(ev[k] - p.DetEv[k]) <= 2e-3, $"{p.ImageId}: det_ev[{k}] C# {ev[k]} vs Python {p.DetEv[k]}");
                    }
                }
            _out.WriteLine($"{photos.Count} photographs, {boxes} candidate boxes compared; worst coordinate difference {worstPx:0.0000} px, worst confidence difference {worstConf:0.000000}");
            Assert.True(boxes > 50);
        }

        // ------------------------------------------------------------------ whole pipeline
        [Fact]
        public void End_to_end_assessment_matches_python_on_real_photos_and_reports_timing()
        {
            Assert.True(V6Inference.TryCreate(DeployDir, ExportDir(), new LesionEngineDetectorSource(() => Detectors.Value), out V6Inference svc, out string err), err);
            var times = new List<double>(); int states = 0;
            using (svc)
                foreach (Photo p in Golden())
                    using (Mat m = Load(p))
                    {
                        UnifiedAssessment a = svc.Assess(m, QualityInput.Ok()); times.Add(a.InferenceMs);
                        Assert.True(a.State != V6States.InferenceError, p.ImageId + ": " + a.Error);
                        for (int j = 0; j < 3; j++)
                        {
                            ConditionAssessment c = a.Conditions[j];
                            Assert.True(Math.Abs(c.FusionLogit - p.LogitsYolo[j]) <= 2e-3, $"{p.ImageId}: fusion logit {j} C# {c.FusionLogit} vs Python {p.LogitsYolo[j]}");
                            Assert.True(Math.Abs(c.CalibratedProbability - p.PYolo[j]) <= 2e-3, $"{p.ImageId}: calibrated {j}");
                            Assert.True(Math.Abs(c.GlobalOnlyProbability - p.POnly[j]) <= 2e-3, $"{p.ImageId}: global-only calibrated {j}");
                            Assert.Equal(p.Decisions[j], c.Status == ConditionStatus.Present ? "present" : c.Status == ConditionStatus.Absent ? "absent" : "uncertain");
                        }
                        Assert.Equal(p.Flags, a.Flags); Assert.Equal(p.State, a.State);
                        Assert.Equal(p.Pools.Sum(x => x.Count(b => b.Deployed)), a.Boxes.Count);                     // supporting boxes = deployed boxes
                        Assert.Equal(p.State == V6States.NoFindingsSupported, a.NormalSupported);
                        states++;
                    }
            _out.WriteLine($"{states} photographs: states, flags, decisions, probabilities and boxes agree with Python. Full V6 assessment time (CPU, 9 detector members + Global + 2 fusions): median {times.OrderBy(x => x).ElementAt(times.Count / 2):0} ms, max {times.Max():0} ms");
        }

        // ------------------------------------------------------------------ flag off / historical scans
        private static SessionDetail Session(params string[] folders) => new SessionDetail
        {
            Session = new ScanSessionRow(),
            Views = folders.Select((f, i) => new CaptureViewRow { ViewType = new[] { "Front", "Left", "Right" }[i], RecordFolder = f }).ToList()
        };

        private static string TempDir() { string d = Path.Combine(Path.GetTempPath(), "v6ses_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        private static UnifiedAssessment Tiny() => new UnifiedAssessment { State = V6States.UncertainReviewRequired, RunId = "r", Conditions = V6Conditions.Names.Select(n => new ConditionAssessment { Condition = n, CalibratedProbability = 0.3, Threshold = 0.4, Status = ConditionStatus.Uncertain }).ToArray() };

        [Fact]
        public void Session_loader_is_inert_when_the_flag_is_off_and_for_scans_saved_without_V6()
        {
            Func<bool> keep = V6FeatureFlag.Provider; string a = TempDir(), b = TempDir();
            try
            {
                V6Archive.Write(a, Tiny(), null, null);                                                        // a V6 file exists in view 1; view 2 has none (older scan)
                V6FeatureFlag.Provider = () => false;
                Assert.Null(V6SessionLoader.Load(Session(a, b), out List<string> p0)); Assert.Empty(p0);       // flag OFF: the Results page falls back to the original V5 display
                V6FeatureFlag.Provider = () => true;
                Assert.Null(V6SessionLoader.Load(Session(b), out List<string> p1)); Assert.Empty(p1);          // flag ON but nothing saved: historical scans keep showing what produced them
                Dictionary<string, UnifiedAssessment> map = V6SessionLoader.Load(Session(a, b), out List<string> p2);
                Assert.Single(map); Assert.True(map.ContainsKey("Front")); Assert.Empty(p2);
                Assert.Null(V6SessionLoader.MarkedAreasPath(new CaptureViewRow { RecordFolder = a }));         // no frame was given, so no picture was saved
            }
            finally { V6FeatureFlag.Provider = keep; Directory.Delete(a, true); Directory.Delete(b, true); }
        }

        [Fact]
        public void Session_loader_reports_a_tampered_file_and_does_not_show_it()
        {
            Func<bool> keep = V6FeatureFlag.Provider; string a = TempDir();
            try
            {
                V6FeatureFlag.Provider = () => true;
                V6Archive.Write(a, Tiny(), null, null);
                string path = Path.Combine(a, V6Archive.FileName);
                File.WriteAllText(path, File.ReadAllText(path).Replace(V6States.UncertainReviewRequired, V6States.NoFindingsSupported));
                Assert.Null(V6SessionLoader.Load(Session(a), out List<string> problems)); Assert.Single(problems); Assert.Contains("seal", problems[0]);
            }
            finally { V6FeatureFlag.Provider = keep; Directory.Delete(a, true); }
        }
    }
}
