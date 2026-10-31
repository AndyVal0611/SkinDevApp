// ============================================================================
// CnnDiagnostics.cs  -  namespace SkinDevApp.AI
//
// Non-destructive, deterministic REPLAY of the deployed classifier and lesion detectors on ONE image file.
//
//   SkinDevApp.exe --diagnose <image> [outDir] [repeats]
//
// It runs headless (no window, no camera, no database, no temporal stabilisation), writes its files into outDir
// (default: "diagnose_<image name>" next to the image) and exits. It reads the models from the Model folder and changes nothing:
// no scan record, patient record, setting or model is touched.
//
// Files written:
//   input_224_rgb.png              the exact 224x224 image the classifier sees, BEFORE normalisation (written as a normal PNG)
//   input_224_nhwc_float32.bin     the normalised tensor, little-endian float32, shape [1,224,224,3] (RGB, (p/127.5)-1)
//   cnn_diagnostic.json            model SHA-256, tensor checksum/statistics, preprocessing trace, N repeated raw probabilities,
//                                  detector files + hashes, final boxes and their pairwise overlap analysis
// shared\cnn_diagnose.py reads these, re-runs the same tensor through Python ONNX Runtime and compares.
//
// The path through the app is mirrored exactly: image -> (as the Upload button does) resize to 640 px wide -> OnnxInference.Preprocess -> ONNX.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCvSharp;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.AI
{
    public static class CnnDiagnostics
    {
        public const int WorkingWidth = 640;   // ClientDashboardForm.AnalysisWidth (upload) / LiveGradCamController working width

        /// <summary>Handles "--diagnose ..." on the command line. Returns true when it ran (the caller should then exit without opening a window).</summary>
        public static bool TryRunFromArgs(string[] args)
        {
            if (args == null) return false;
            int at = Array.FindIndex(args, a => string.Equals(a, "--diagnose", StringComparison.OrdinalIgnoreCase));
            if (at < 0) return false;
            string image = at + 1 < args.Length ? args[at + 1] : null;
            string outDir = at + 2 < args.Length ? args[at + 2] : null;
            int repeats = at + 3 < args.Length && int.TryParse(args[at + 3], out int r) && r > 0 ? r : 5;
            try
            {
                if (string.IsNullOrEmpty(image) || !File.Exists(image)) throw new FileNotFoundException("Usage: SkinDevApp.exe --diagnose <image> [outDir] [repeats]", image);
                Run(image, outDir, repeats);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "lumyvue_diagnose_error.txt"), ex.ToString()); } catch { }
            }
            return true;
        }

        public static string Run(string imagePath, string outDir, int repeats)
        {
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(imagePath)), "diagnose_" + Path.GetFileNameWithoutExtension(imagePath));
            Directory.CreateDirectory(outDir);

            var steps = new List<string>();
            using (Mat src = Cv2.ImRead(imagePath, ImreadModes.Color))
            {
                if (src.Empty()) throw new InvalidDataException("Could not decode " + imagePath);
                steps.Add("decode: " + src.Width + "x" + src.Height + ", " + src.Channels() + " channels, 8-bit, BGR order (OpenCV)");

                // exactly what ScanPipeline.BuildUploadRequest does with an uploaded image
                using (Mat working = LiveGradCamController.ResizeToWidth(src, WorkingWidth))
                {
                    steps.Add("working frame: ResizeToWidth(" + WorkingWidth + ") bilinear (INTER_LINEAR) -> " + working.Width + "x" + working.Height +
                              (src.Width <= WorkingWidth ? " (not larger than 640 px: unchanged)" : "") + "; no face crop, no centre crop, aspect ratio kept");
                    steps.Add("OnnxInference.Preprocess: BGR->RGB, Resize " + OnnxInference.ImageSize + "x" + OnnxInference.ImageSize +
                              " bilinear (INTER_LINEAR, aspect ratio NOT kept), float32, (p/127.5)-1.0, layout NHWC [1,224,224,3]");

                    // ---- the exact 224 image and tensor ------------------------------------------------
                    var tensor = OnnxInference.Preprocess(working);
                    float[] flat = tensor.ToArray();
                    byte[] bytes = new byte[flat.Length * 4];
                    Buffer.BlockCopy(flat, 0, bytes, 0, bytes.Length);
                    File.WriteAllBytes(Path.Combine(outDir, "input_224_nhwc_float32.bin"), bytes);

                    using (Mat rgb224 = new Mat(OnnxInference.ImageSize, OnnxInference.ImageSize, MatType.CV_8UC3))
                    using (Mat bgr224 = new Mat())
                    {
                        // un-normalise the tensor itself (so the PNG is what the tensor encodes, not a second resize)
                        var px = new byte[flat.Length];
                        for (int i = 0; i < flat.Length; i++) px[i] = (byte)Math.Max(0, Math.Min(255, Math.Round((flat[i] + 1.0) * 127.5)));
                        System.Runtime.InteropServices.Marshal.Copy(px, 0, rgb224.Data, px.Length);
                        Cv2.CvtColor(rgb224, bgr224, ColorConversionCodes.RGB2BGR);
                        Cv2.ImWrite(Path.Combine(outDir, "input_224_rgb.png"), bgr224);
                    }

                    // ---- deterministic replay of the classifier --------------------------------------------
                    AiEngine.EnsureLoaded();
                    var runs = new List<float[]>();
                    for (int i = 0; i < repeats; i++) runs.Add(AiEngine.Predict(working).Probabilities);
                    bool bitwise = runs.All(r => r.SequenceEqual(runs[0]));
                    double maxSpread = runs.Max(r => r.Select((v, k) => Math.Abs((double)v - runs[0][k])).Max());

                    // ---- lesion detectors on the same frame (no face filter: there is no camera frame quality here) ---
                    var det = new Dictionary<string, object>();
                    try
                    {
                        double[] thr = SkinDevApp.Data.ScanSettings.Current.DetectorThresholds();
                        LesionDetectionSet a = LesionEngine.Detect(working, thr);
                        LesionDetectionSet b = LesionEngine.Detect(working, thr);
                        bool same = a.Boxes.Count == b.Boxes.Count && a.Boxes.Zip(b.Boxes, (x, y) => x.ClassIndex == y.ClassIndex && x.Confidence == y.Confidence && x.X0 == y.X0 && x.Y0 == y.Y0 && x.X1 == y.X1 && x.Y1 == y.Y1).All(t => t);
                        det["thresholds_used(Acne,Hyperpigmentation,Eczema)"] = a.Thresholds;
                        det["model_tag"] = a.ModelTag;
                        det["model_sha256"] = a.ModelSha256;
                        det["frame"] = new { a.FrameWidth, a.FrameHeight };
                        det["repeat_identical"] = same;
                        det["boxes"] = a.Boxes.Select((x, i) => new { index = i, cls = x.ClassName, confidence = x.Confidence, x0 = x.X0, y0 = x.Y0, x1 = x.X1, y1 = x.Y1 }).ToList();
                        det["pairs"] = DetectionDiagnostics.Analyse(a.Boxes).Select(p => new
                        {
                            cls = p.ClassName, a = p.A, b = p.B, iou = p.Iou, containment_of_smaller = p.ContainmentOfSmaller, relation = p.Relation.ToString()
                        }).ToList();
                        det["loaded_detectors"] = LesionEngine.LoadedDetectors().Select(d => new
                        {
                            file = d.ModelFile, sha256 = d.ModelSha256, imgsz = d.ImageSize, classes = d.PaletteClasses.Select(i => ClassPalette.Names[i]).ToArray(), ensemble_size = d.EnsembleSize
                        }).ToList();
                    }
                    catch (Exception dex) { det["error"] = dex.GetType().Name + ": " + dex.Message; }

                    float mn = flat.Min(), mx = flat.Max();
                    var report = new Dictionary<string, object>
                    {
                        ["image"] = Path.GetFullPath(imagePath),
                        ["image_sha256"] = Hex(SHA256.Create().ComputeHash(File.ReadAllBytes(imagePath))),
                        ["created_utc"] = DateTime.UtcNow.ToString("o"),
                        ["note"] = "Read-only replay. No stabilisation, no camera, no database, no model or threshold change.",
                        ["preprocessing_trace"] = steps,
                        ["classifier"] = new
                        {
                            model_path = AiEngine.ModelPath,
                            model_sha256 = Hex(SHA256.Create().ComputeHash(File.ReadAllBytes(AiEngine.ModelPath))),
                            class_order = PredictionResult.ClassNames,
                            tensor_shape = new[] { 1, OnnxInference.ImageSize, OnnxInference.ImageSize, 3 },
                            tensor_file = "input_224_nhwc_float32.bin",
                            tensor_sha256 = Hex(SHA256.Create().ComputeHash(bytes)),
                            tensor_min = mn, tensor_max = mx, tensor_mean = flat.Average(),
                            repeats = repeats,
                            repeats_bitwise_identical = bitwise,
                            repeats_max_abs_difference = maxSpread,
                            raw_probabilities = runs.Select(r => r.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList()
                        },
                        ["detectors"] = det
                    };
                    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    string path = Path.Combine(outDir, "cnn_diagnostic.json");
                    File.WriteAllText(path, json);
                    return path;
                }
            }
        }

        private static string Hex(byte[] h) => BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
    }
}
