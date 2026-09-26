using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace SkinDevApp.Explainability
{
    // ======================================================================
    // WIRE MODELS  (match gradcam_service.py exactly)
    // ======================================================================

    public sealed class GradCamRequest
    {
        [JsonPropertyName("image_base64")]
        public string ImageBase64 { get; set; } = string.Empty;

        /// <summary>null = explain the predicted class.</summary>
        [JsonPropertyName("class_index")]
        public int? ClassIndex { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "gradcam++";

        [JsonPropertyName("layer")]
        public string Layer { get; set; }

        /// <summary>Live mode sets this false and blends the heatmap client-side.</summary>
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

        [JsonPropertyName("probabilities")]
        public Dictionary<string, float> Probabilities { get; set; }

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

    /// <summary>
    /// Decoded, ready-to-display result.
    /// Overlay and Heatmap are owned by the caller - dispose them (or wrap
    /// the whole result in a `using` block, since GradCamResult itself
    /// implements IDisposable and disposes both for you).
    /// </summary>
    public sealed class GradCamResult : IDisposable
    {
        public bool Ok { get; set; }
        public string Error { get; set; }

        public int PredictedIndex { get; set; }
        public string PredictedClass { get; set; } = "";
        public float Confidence { get; set; }
        public string Method { get; set; } = "";
        public string TargetLayer { get; set; } = "";

        /// <summary>Server-side compute time.</summary>
        public double ServiceLatencyMs { get; set; }

        /// <summary>Wall-clock time including HTTP, base64 and PNG decode.</summary>
        public double RoundTripMs { get; set; }

        public Bitmap Overlay { get; set; }
        public Bitmap Heatmap { get; set; }

        public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();

        public static GradCamResult Failed(string error) =>
            new GradCamResult { Ok = false, Error = error };

        public void Dispose()
        {
            Overlay?.Dispose();
            Heatmap?.Dispose();
        }
    }

    /// <summary>
    /// Live-mode result: raw heatmap only, no server-rendered overlay - the
    /// caller composites it with SkinDevApp.Imaging.HeatmapRenderer.Blend().
    /// The Mat is owned by the caller.
    /// </summary>
    public sealed class GradCamRawResult : IDisposable
    {
        public bool Ok { get; set; }
        public string Error { get; set; }

        public int PredictedIndex { get; set; } = -1;
        public string PredictedClass { get; set; } = "";
        public float Confidence { get; set; }
        public string Method { get; set; } = "";
        public double ServiceLatencyMs { get; set; }

        /// <summary>8-bit single-channel attribution map. May be null.</summary>
        public Mat Heatmap { get; set; }

        public static GradCamRawResult Failed(string error) =>
            new GradCamRawResult { Ok = false, Error = error };

        public void Dispose() => Heatmap?.Dispose();
    }

    // ======================================================================
    // SERVICE CLIENT
    // ======================================================================

    /// <summary>
    /// Talks to the local Python Grad-CAM++ service on 127.0.0.1:8765.
    ///
    /// ONE static HttpClient for the whole application lifetime. Creating a
    /// HttpClient per request exhausts sockets under TIME_WAIT and will start
    /// throwing SocketException after a few hundred analyses.
    ///
    /// NOTE: uses HttpClientHandler, not SocketsHttpHandler - the latter is
    /// .NET-Core-only and does not exist on .NET Framework 4.7.2.
    /// </summary>
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
            var handler = new HttpClientHandler
            {
                // Loopback only - never send this through a corporate proxy.
                UseProxy = false
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl),
                // Grad-CAM++ on a CPU-only machine is typically 300ms-2s.
                // 20s covers a cold first request right after service start.
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        /// <summary>
        /// True when the service is up and the Keras model is in RAM.
        /// Call this once when the dashboard loads and show a status hint.
        /// </summary>
        public static async Task<bool> IsReadyAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var health = await Http
                    .GetFromJsonAsync<GradCamHealth>("/health", JsonOptions, cts.Token)
                    .ConfigureAwait(false);

                return health != null && health.Ok && health.ModelLoaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GradCAM] health check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send captured image bytes, receive the Grad-CAM++ overlay.
        ///
        /// IMPORTANT: pass PNG bytes of the SAME frame that went to ONNX.
        /// Re-encoding as JPEG changes pixels and can shift the prediction.
        /// </summary>
        /// <param name="imageBytes">PNG (preferred) or JPEG encoded frame.</param>
        /// <param name="classIndex">null = explain the predicted class.</param>
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
                    Method = method
                };

                using var httpResponse = await Http
                    .PostAsJsonAsync("/gradcam", request, ct)
                    .ConfigureAwait(false);

                var payload = await httpResponse.Content
                    .ReadFromJsonAsync<GradCamResponse>(JsonOptions, ct)
                    .ConfigureAwait(false);

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
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return GradCamResult.Failed(
                    "Grad-CAM service timed out. Is gradcam_service.py still running?");
            }
            catch (HttpRequestException ex)
            {
                return GradCamResult.Failed(
                    $"Cannot reach the Grad-CAM service at {BaseUrl}. " +
                    $"Start gradcam_service.py first. ({ex.Message})");
            }
            catch (Exception ex)
            {
                return GradCamResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// LIVE MODE: request the raw heatmap only, skipping the server-side
        /// overlay render (a colormap pass + a full-size PNG encode). This is
        /// the single biggest saving available at a 2s refresh rate.
        ///
        /// The caller composites with SkinDevApp.Imaging.HeatmapRenderer.Blend().
        /// </summary>
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
                    ReturnOverlay = false,   // <- the live-mode saving
                    ReturnHeatmap = true
                };

                using var httpResponse = await Http
                    .PostAsJsonAsync("/gradcam", request, ct)
                    .ConfigureAwait(false);

                var payload = await httpResponse.Content
                    .ReadFromJsonAsync<GradCamResponse>(JsonOptions, ct)
                    .ConfigureAwait(false);

                if (payload == null)
                    return GradCamRawResult.Failed("Empty response.");

                if (!payload.Ok)
                    return GradCamRawResult.Failed(payload.Error ?? "Service error.");

                Mat heat = null;

                if (!string.IsNullOrWhiteSpace(payload.HeatmapBase64))
                {
                    byte[] png = Convert.FromBase64String(payload.HeatmapBase64);
                    heat = Cv2.ImDecode(png, ImreadModes.Grayscale);

                    if (heat.Empty())
                    {
                        heat.Dispose();
                        return GradCamRawResult.Failed("Heatmap PNG failed to decode.");
                    }
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
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return GradCamRawResult.Failed("Grad-CAM service timed out.");
            }
            catch (HttpRequestException ex)
            {
                return GradCamRawResult.Failed($"Service unreachable: {ex.Message}");
            }
            catch (Exception ex)
            {
                return GradCamRawResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Base64 PNG -> Bitmap. The intermediate Bitmap keeps a lock on its
        /// MemoryStream, so a detached copy is returned instead.
        /// </summary>
        private static Bitmap DecodePng(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);

                using var ms = new MemoryStream(bytes, writable: false);
                using var decoded = new Bitmap(ms);

                return new Bitmap(decoded);   // independent of the stream
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GradCAM] PNG decode failed: {ex.Message}");
                return null;
            }
        }
    }
}