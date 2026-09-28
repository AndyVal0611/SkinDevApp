using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace SkinDevApp.Explainability
{
    public sealed class GradCamRequest
    {
        [JsonPropertyName("image_base64")]
        public string ImageBase64 { get; set; } = string.Empty;

        [JsonPropertyName("class_index")]
        public int? ClassIndex { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "gradcam++";

        [JsonPropertyName("layer")]
        public string Layer { get; set; }

        [JsonPropertyName("return_overlay")]
        public bool ReturnOverlay { get; set; } = true;

        [JsonPropertyName("return_heatmap")]
        public bool ReturnHeatmap { get; set; } = true;
    }

    public sealed class GradCamResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; }
        [JsonPropertyName("predicted_index")] public int PredictedIndex { get; set; }
        [JsonPropertyName("predicted_class")] public string PredictedClass { get; set; }
        [JsonPropertyName("confidence")] public float Confidence { get; set; }
        [JsonPropertyName("probabilities")] public Dictionary<string, float> Probabilities { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("target_layer")] public string TargetLayer { get; set; }
        [JsonPropertyName("latency_ms")] public double LatencyMs { get; set; }
        [JsonPropertyName("overlay_base64")] public string OverlayBase64 { get; set; }
        [JsonPropertyName("heatmap_base64")] public string HeatmapBase64 { get; set; }
        [JsonPropertyName("notes")] public List<string> Notes { get; set; }
    }

    public sealed class GradCamHealth
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("model_loaded")] public bool ModelLoaded { get; set; }
        [JsonPropertyName("target_layer")] public string TargetLayer { get; set; }
        [JsonPropertyName("classes")] public List<string> Classes { get; set; }
    }

    public sealed class GradCamResult : IDisposable
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public int PredictedIndex { get; set; }
        public string PredictedClass { get; set; } = "";
        public float Confidence { get; set; }
        public string Method { get; set; } = "";
        public string TargetLayer { get; set; } = "";
        public double ServiceLatencyMs { get; set; }
        public double RoundTripMs { get; set; }
        public Bitmap Overlay { get; set; }
        public Bitmap Heatmap { get; set; }
        public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();

        public static GradCamResult Failed(string error) => new GradCamResult { Ok = false, Error = error };

        public void Dispose()
        {
            Overlay?.Dispose();
            Heatmap?.Dispose();
        }
    }

    public sealed class GradCamRawResult : IDisposable
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public int PredictedIndex { get; set; } = -1;
        public string PredictedClass { get; set; } = "";
        public float Confidence { get; set; }
        public string Method { get; set; } = "";
        public double ServiceLatencyMs { get; set; }
        public Mat Heatmap { get; set; }

        public static GradCamRawResult Failed(string error) => new GradCamRawResult { Ok = false, Error = error };

        public void Dispose() => Heatmap?.Dispose();
    }

    public static class GradCamService
    {
        public const string BaseUrl = "http://127.0.0.1:8765";
        private static readonly HttpClient Http = CreateClient();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { UseProxy = false };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public static async Task<bool> IsReadyAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var jsonString = await Http.GetStringAsync("/health").ConfigureAwait(false);
                var health = JsonSerializer.Deserialize<GradCamHealth>(jsonString, JsonOptions);

                return health != null && health.Ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GradCAM] health check failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<GradCamResult> ExplainAsync(
            byte[] imageBytes,
            int? classIndex = null,
            string method = "gradcam++",
            CancellationToken ct = default)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return GradCamResult.Failed("No image data to explain.");

            var sw = Stopwatch.StartNew();

            try
            {
                var request = new GradCamRequest
                {
                    ImageBase64 = Convert.ToBase64String(imageBytes),
                    ClassIndex = classIndex,
                    Method = method,
                    ReturnOverlay = true,
                    ReturnHeatmap = true
                };

                var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpResponse = await Http.PostAsync("/gradcam", content, ct).ConfigureAwait(false);
                var responseString = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<GradCamResponse>(responseString, JsonOptions);

                if (payload == null)
                    return GradCamResult.Failed("Empty response from Grad-CAM service.");

                if (!payload.Ok)
                    return GradCamResult.Failed(payload.Error ?? "Unknown service error.");

                var overlay = DecodePng(payload.OverlayBase64);
                var heatmap = DecodePng(payload.HeatmapBase64);

                sw.Stop();

                return new GradCamResult
                {
                    Ok = true,
                    PredictedIndex = payload.PredictedIndex,
                    PredictedClass = payload.PredictedClass ?? "",
                    Confidence = payload.Confidence,
                    Method = payload.Method ?? "",
                    TargetLayer = payload.TargetLayer ?? "",
                    ServiceLatencyMs = payload.LatencyMs,
                    RoundTripMs = sw.Elapsed.TotalMilliseconds,
                    Overlay = overlay,
                    Heatmap = heatmap,
                    Notes = payload.Notes ?? new List<string>()
                };
            }
            catch (Exception ex)
            {
                return GradCamResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public static async Task<GradCamRawResult> ExplainRawAsync(
            byte[] imageBytes,
            int? classIndex = null,
            CancellationToken ct = default)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return GradCamRawResult.Failed("No image data.");

            try
            {
                var request = new GradCamRequest
                {
                    ImageBase64 = Convert.ToBase64String(imageBytes),
                    ClassIndex = classIndex,
                    Method = "gradcam++",
                    ReturnOverlay = false,
                    ReturnHeatmap = true
                };

                var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpResponse = await Http.PostAsync("/gradcam", content, ct).ConfigureAwait(false);
                var responseString = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<GradCamResponse>(responseString, JsonOptions);

                if (payload == null || !payload.Ok)
                    return GradCamRawResult.Failed(payload?.Error ?? "Service error.");

                Mat heat = null;
                if (!string.IsNullOrWhiteSpace(payload.HeatmapBase64))
                {
                    byte[] png = Convert.FromBase64String(payload.HeatmapBase64);
                    heat = Cv2.ImDecode(png, ImreadModes.Grayscale);
                }

                return new GradCamRawResult
                {
                    Ok = true,
                    PredictedIndex = payload.PredictedIndex,
                    PredictedClass = payload.PredictedClass ?? "",
                    Confidence = payload.Confidence,
                    Method = payload.Method ?? "",
                    ServiceLatencyMs = payload.LatencyMs,
                    Heatmap = heat
                };
            }
            catch (Exception ex)
            {
                return GradCamRawResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Bitmap DecodePng(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes, writable: false);
                using var decoded = new Bitmap(ms);
                return new Bitmap(decoded);
            }
            catch
            {
                return null;
            }
        }
    }
}