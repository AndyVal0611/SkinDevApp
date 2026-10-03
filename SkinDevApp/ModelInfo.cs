// ============================================================================
// ModelInfo.cs  (NEW)  -  namespace SkinDevApp.AI
//
// Identifies WHICH model produced a result, so every saved capture carries the
// model hash / version (record.json -> "model").
//
// Looks for model_version.json (written by notebook Cell 35) next to the app,
// then in .\models, then C:\PrecisionSkinV1\models, then in the folder named by
// the PRECISIONSKIN_MODEL_DIR environment variable. If it is missing, falls
// back to hashing precisionskin.onnx if it can find it. Never throws.
// ============================================================================

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkinDevApp.AI
{
    public sealed class ModelInfo
    {
        public string RunName { get; set; } = "unknown";
        public string PipelineVersion { get; set; } = "";
        public string ServiceVersion { get; set; } = "";
        public string OnnxSha256 { get; set; } = "unknown";
        public string KerasSha256 { get; set; } = "unknown";
        public string ServiceSha256 { get; set; } = "unknown";
        public string CreatedUtc { get; set; } = "";
        public string Source { get; set; } = "not found";

        private static ModelInfo _current;
        private static readonly object Gate = new object();

        public static ModelInfo Current
        {
            get
            {
                lock (Gate)
                {
                    if (_current == null) _current = Load();
                    return _current;
                }
            }
        }

        public static void Refresh()
        {
            lock (Gate) { _current = null; }
        }

        private static string[] SearchDirs()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string env = Environment.GetEnvironmentVariable("PRECISIONSKIN_MODEL_DIR");

            return new[]
            {
                env,
                baseDir,
                Path.Combine(baseDir, "models"),
                @"C:\PrecisionSkinV1\models"
            };
        }

        public static ModelInfo Load()
        {
            var info = new ModelInfo();

            try
            {
                foreach (string dir in SearchDirs())
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;

                    string json = Path.Combine(dir, "model_version.json");
                    if (!File.Exists(json)) continue;

                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(json)))
                    {
                        JsonElement root = doc.RootElement;
                        info.RunName = Str(root, "run_name", info.RunName);
                        info.ServiceVersion = Str(root, "service_version", "");
                        info.CreatedUtc = Str(root, "created_utc", "");

                        JsonElement sha;
                        if (root.TryGetProperty("sha256", out sha) && sha.ValueKind == JsonValueKind.Object)
                        {
                            info.OnnxSha256 = Str(sha, "precisionskin.onnx", info.OnnxSha256);
                            info.KerasSha256 = Str(sha, "precisionskin_best.keras", info.KerasSha256);
                            info.ServiceSha256 = Str(sha, "gradcam_service.py", info.ServiceSha256);
                        }
                    }

                    info.Source = json;
                    return info;
                }

                // Fallback: hash the ONNX file we can find.
                foreach (string dir in SearchDirs())
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string onnx = Path.Combine(dir, "precisionskin.onnx");
                    if (!File.Exists(onnx)) continue;

                    info.OnnxSha256 = Sha256File(onnx);
                    info.Source = onnx + " (hashed; model_version.json not found)";
                    return info;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ModelInfo] " + ex.Message);
            }

            return info;
        }

        private static string Str(JsonElement e, string name, string fallback)
        {
            JsonElement v;
            if (e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
            return fallback;
        }

        private static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
