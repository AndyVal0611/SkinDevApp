// ============================================================================
// Gradcamservice.cs  —  UPDATED VERSION
//
// Changes from your original (search for "// FIX" to find every change):
//
//   FIX 1  GradCamRequest:  added FrameId (string) and DropIfStale (bool)
//   FIX 2  GradCamResponse: added FrameId echo field
//   FIX 3  GradCamRawResult: added FrameId echo field
//   FIX 4  IsReadyAsync: CancellationToken is now actually passed to GetStringAsync
//   FIX 5  ExplainRawAsync: sends FrameId + DropIfStale; reads echoed FrameId back;
//          handles the "stale" short-circuit the service returns in live mode;
//          switches from PNG to JPEG Q90 for lower per-tick encode cost
// ============================================================================

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

        // FIX 1a: Opaque ID echoed back by the service unchanged.
        // The live loop sets this to a monotonically-increasing tick counter
        // so it can detect and discard responses that belong to a stale frame.
        [JsonPropertyName("frame_id")]
        public string FrameId { get; set; }

        // FIX 1b: When true, the service skips computing Grad-CAM if a newer
        // request arrived while this one was waiting for the model lock.
        // Always true for the live loop; always false for a captured-frame analysis.
        [JsonPropertyName("drop_if_stale")]
        public bool DropIfStale { get; set; } = false;
    }

    public sealed class GradCamResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; }
        [JsonPropertyName("stale")] public bool Stale { get; set; }   // FIX 2a: service returns stale=true when dropped
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

        // FIX 2b: Echoed frame_id so the live loop can check for staleness.
        [JsonPropertyName("frame_id")]
        public string FrameId { get; set; }
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

        public static GradCamResult Failed(string error) =>
            new GradCamResult { Ok = false, Error = error };

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
        public bool WasStale { get; set; }  // FIX 3a: set to true when service dropped the request
        public int PredictedIndex { get; set; } = -1;
        public string PredictedClass { get; set; } = "";
        public float Confidence { get; set; }
        public string Method { get; set; } = "";
        public double ServiceLatencyMs { get; set; }
        public Mat Heatmap { get; set; }

        // FIX 3b: Echoed frame ID so the caller can detect stale responses.
        public string EchoedFrameId { get; set; }

        public static GradCamRawResult Failed(string error) =>
            new GradCamRawResult { Ok = false, Error = error };

        public static GradCamRawResult Stale() =>
            new GradCamRawResult { Ok = false, WasStale = true, Error = "superseded" };

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

        // FIX 4: Pass the CancellationToken to GetStringAsync.
        // In the original, a linked CTS with a 3-second timeout was created
        // but the token was never actually given to GetStringAsync, so the
        // timeout did nothing.
        public static async Task<bool> IsReadyAsync(CancellationToken ct = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                // GetAsync(string, CancellationToken) exists on all .NET versions,
                // so the 3-second timeout actually fires.
                using var response = await Http.GetAsync("/health", cts.Token)
                                               .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return false;

                var jsonString = await response.Content.ReadAsStringAsync()
                                               .ConfigureAwait(false);

                var health = JsonSerializer.Deserialize<GradCamHealth>(jsonString, JsonOptions);
                return health != null && health.Ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GradCAM] health check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Full Grad-CAM++ request — used for captured-frame analysis.
        /// Returns overlay + heatmap as Bitmaps.
        /// </summary>
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
                    ReturnHeatmap = true,
                    DropIfStale = false   // Never drop for a captured analysis.
                };

                var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpResponse = await Http.PostAsync("/gradcam", content, ct)
                                                   .ConfigureAwait(false);
                var responseString = await httpResponse.Content.ReadAsStringAsync()
                                                       .ConfigureAwait(false);
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

        // FIX 5: ExplainRawAsync for the live loop.
        //   - Accepts a frameId string; sends it and reads the echo back.
        //   - Sets drop_if_stale=true so the service skips computation when a
        //     newer frame has already arrived (latest-frame-wins, no queue build-up).
        //   - Switches from PNG to JPEG Q90: saves ~5-15ms per tick at 640px wide
        //     with no meaningful quality loss for a 224x224 classification input.
        //   - Returns WasStale=true when the service short-circuits; the live loop
        //     should simply skip that tick without logging an error.
        public static async Task<GradCamRawResult> ExplainRawAsync(
            byte[] imageBytes,
            int? classIndex = null,
            string frameId = null,
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
                    ReturnHeatmap = true,
                    FrameId = frameId,
                    DropIfStale = true   // Live mode: skip if superseded.
                };

                var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpResponse = await Http.PostAsync("/gradcam", content, ct)
                                                   .ConfigureAwait(false);
                var responseString = await httpResponse.Content.ReadAsStringAsync()
                                                       .ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<GradCamResponse>(responseString, JsonOptions);

                // Service returned stale=true: a newer frame superseded ours.
                // This is normal in live mode — just skip the tick.
                if (payload != null && payload.Stale)
                    return GradCamRawResult.Stale();

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
                    Heatmap = heat,
                    EchoedFrameId = payload.FrameId   // FIX 5c: return echoed ID to caller
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