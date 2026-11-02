using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.V6;
using Xunit;
using Xunit.Abstractions;

namespace SkinDevApp.Tests
{
    /// <summary>
    /// The full V6 variant (Global + YOLO + ROI MobileNetV2) against the Python reference (v6/deployment/v6_full_variant_golden.json, made by
    /// v6/tools/make_full_golden.py from the notebook's own build_roi_candidates / crop_roi / roi features). Same 13 real photographs as the global_yolo parity tests.
    /// </summary>
    [Collection("V6Flag")]
    public class V6FullVariantTests
    {
        private readonly ITestOutputHelper _out;
        public V6FullVariantTests(ITestOutputHelper output) { _out = output; }

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
            foreach (string c in new[] { "acne", "hyperpigmentation", "eczema" })
            {
                var sp = new LesionDetector(Path.Combine(dir, "lesion_detector_" + c + ".onnx"), Path.Combine(dir, "lesion_detector_" + c + ".json"));
                for (int k = 2; k <= 3; k++) { string extra = Path.Combine(dir, "lesion_detector_" + c + "_m" + k + ".onnx"); if (File.Exists(extra)) sp.Peers.Add(new LesionDetector(extra, Path.Combine(dir, "lesion_detector_" + c + ".json"))); }
                list.Add(sp);
            }
            return list;
        });

        private sealed class G { public string ImageId, RelPath, Sha256; public int Regions; public double[] PFull; public bool[] Decisions; }

        private static double[] D(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

        private static List<G> Golden(out double[] thresholds)
        {
            JsonElement root = JsonDocument.Parse(File.ReadAllText(Path.Combine(DeployDir, "v6_full_variant_golden.json"))).RootElement;
            thresholds = D(root.GetProperty("thresholds"));
            return root.GetProperty("photos").EnumerateArray().Select(p => new G
            {
                ImageId = p.GetProperty("image_id").GetString(), RelPath = p.GetProperty("rel_path").GetString(), Sha256 = p.GetProperty("sha256").GetString(),
                Regions = p.GetProperty("n_regions").GetInt32(), PFull = D(p.GetProperty("calibrated_full")), Decisions = p.GetProperty("decisions").EnumerateArray().Select(x => x.GetBoolean()).ToArray()
            }).ToList();
        }

        private static Mat Load(G p)
        {
            string path = Path.Combine(PhotoRoot(), p.RelPath);
            using (var sha = SHA256.Create()) using (FileStream fs = File.OpenRead(path))
                Assert.Equal(p.Sha256, BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant());
            Mat m = Cv2.ImRead(path, ImreadModes.Color);
            Assert.False(m.Empty(), "could not read " + path);
            return m;
        }

        [Fact]
        public void Full_variant_matches_the_python_reference_on_real_photos()
        {
            List<G> gold = Golden(out double[] thr);
            Assert.True(V6Inference.TryCreate(DeployDir, ExportDir(), new LesionEngineDetectorSource(() => Detectors.Value), out V6Inference svc, out string err, "full"), err);
            double worst = 0; int flips = 0; var times = new List<double>();
            using (svc)
                foreach (G p in gold)
                    using (Mat m = Load(p))
                    {
                        UnifiedAssessment a = svc.Assess(m, QualityInput.Ok());
                        Assert.Equal("full", a.Variant);
                        times.Add(a.InferenceMs);
                        for (int j = 0; j < 3; j++)
                        {
                            double diff = Math.Abs(a.Conditions[j].CalibratedProbability - p.PFull[j]); worst = Math.Max(worst, diff);
                            Assert.True(diff <= 3e-3, $"{p.ImageId} {V6Conditions.Names[j]}: C# {a.Conditions[j].CalibratedProbability:0.0000} vs Python {p.PFull[j]:0.0000}");
                            Assert.Equal(thr[j], a.Conditions[j].Threshold, 9);
                            if ((a.Conditions[j].CalibratedProbability >= thr[j]) != p.Decisions[j]) flips++;
                        }
                    }
            _out.WriteLine($"full variant: max |C# - Python| calibrated = {worst:0.000000}; decision flips = {flips}/{gold.Count * 3}; ms/photo median {times.OrderBy(x => x).ElementAt(times.Count / 2):0} max {times.Max():0}");
            Assert.True(flips <= 1, "decision flips against the Python reference: " + flips);
        }

        [Fact]
        public void Default_variant_is_global_yolo_and_full_is_an_explicit_choice()
        {
            Assert.True(V6Inference.TryCreate(DeployDir, ExportDir(), new LesionEngineDetectorSource(() => Detectors.Value), out V6Inference a, out string e1), e1);
            using (a) Assert.Equal("global_yolo", a.Variant);
            Assert.False(V6Inference.TryCreate(DeployDir, ExportDir(), null, out V6Inference b, out string e2, "nonsense"));
            Assert.Contains("unknown V6 variant", e2);
        }
    }
}
