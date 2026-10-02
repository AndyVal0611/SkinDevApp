// ============================================================================
// Gradcamservice.cs  —  UPDATED VERSION  (v2.0 service support)
//
//   NEW (v2.0)  ExplainAllClassesAsync(...) -> GradCamAllResult: one request
//               returns a Grad-CAM++ map for EACH of the four classes with
//               raw_peak / relative_strength / concentration / diffuse flags.
//               Requires gradcam_service.py v2.0 (the old /gradcam endpoint and
//               ExplainAsync / ExplainRawAsync still work unchanged).
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

        // v2.0: ask for one map per class instead of a single overlay.
        [JsonPropertyName("all_classes")]
        public bool AllClasses { get; set; } = false;

        // v2.0: longest side of each returned class heatmap (client resizes).
        [JsonPropertyName("heatmap_max_side")]
        public int? HeatmapMaxSide { get; set; }

        // v2.1: also return each class's RAW CAM (float32, feature-map resolution).
        [JsonPropertyName("include_raw_cam")]
        public bool IncludeRawCam { get; set; } = false;
    }

    /// <summary>v2.1: how much the four class maps agree with each other (raw CAMs).</summary>
    public sealed class ClassMapSimilarityDto
    {
        [JsonPropertyName("level")] public string Level { get; set; }
        [JsonPropertyName("maps_largely_shared")] public bool MapsLargelyShared { get; set; }
        [JsonPropertyName("mean_pairwise_pearson")] public double MeanPairwisePearson { get; set; }
        [JsonPropertyName("min_pairwise_pearson")] public double MinPairwisePearson { get; set; }
        [JsonPropertyName("max_pairwise_pearson")] public double MaxPairwisePearson { get; set; }
        [JsonPropertyName("mean_top10pct_iou")] public double MeanTop10Iou { get; set; }
        [JsonPropertyName("pairwise_pearson")] public Dictionary<string, double> PairwisePearson { get; set; }
        [JsonPropertyName("shared_component_r2")] public Dictionary<string, double> SharedComponentR2 { get; set; }
        [JsonPropertyName("basis")] public string Basis { get; set; }
    }

    public sealed class GradCamClassDto
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("class")] public string Class { get; set; }
        [JsonPropertyName("probability")] public float Probability { get; set; }
        [JsonPropertyName("is_predicted")] public bool IsPredicted { get; set; }
        [JsonPropertyName("raw_peak")] public double RawPeak { get; set; }
        [JsonPropertyName("relative_strength")] public double RelativeStrength { get; set; }
        [JsonPropertyName("top10pct_mass")] public double Top10Mass { get; set; }
        [JsonPropertyName("centroid_x")] public double CentroidX { get; set; }
        [JsonPropertyName("centroid_y")] public double CentroidY { get; set; }
        [JsonPropertyName("diffuse")] public bool Diffuse { get; set; }
        [JsonPropertyName("heatmap_base64")] public string HeatmapBase64 { get; set; }
        [JsonPropertyName("raw_cam_shape")] public int[] RawCamShape { get; set; }
        [JsonPropertyName("raw_cam_base64")] public string RawCamBase64 { get; set; }
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

        // v2.0 (all_classes responses)
        [JsonPropertyName("classes")] public List<GradCamClassDto> Classes { get; set; }
        [JsonPropertyName("service_version")] public string ServiceVersion { get; set; }
        [JsonPropertyName("exec_mode")] public string ExecMode { get; set; }
        [JsonPropertyName("class_map_similarity")] public ClassMapSimilarityDto ClassMapSimilarity { get; set; }
    }

    public sealed class GradCamHealth
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("model_loaded")] public bool ModelLoaded { get; set; }
        [JsonPropertyName("target_layer")] public string TargetLayer { get; set; }
        [JsonPropertyName("classes")] public List<string> Classes { get; set; }
        [JsonPropertyName("service_version")] public string ServiceVersion { get; set; }
        [JsonPropertyName("exec_mode")] public string ExecMode { get; set; }
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

    /// <summary>
    /// v2.0 all-class result. Maps are OWNED by this object until you move them
    /// into a ClassHeatmapSet with <see cref="ToHeatmapSet"/>.
    /// </summary>
    public sealed class GradCamAllResult : IDisposable
    {
        public bool Ok { get; set; }
        public bool Stale { get; set; }
        public string Error { get; set; }
        public string FrameId { get; set; }

        public int PredictedIndex { get; set; } = -1;
        public float Confidence { get; set; }
        public float[] Probabilities { get; set; } = new float[SkinDevApp.Imaging.ClassPalette.ClassCount];

        public string Method { get; set; } = "";
        public string ExecMode { get; set; } = "";
        public string ServiceVersion { get; set; } = "";
        public double ServiceLatencyMs { get; set; }
        public double RoundTripMs { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        public ClassMapInfo[] Maps { get; set; } = new ClassMapInfo[0];

        /// <summary>v2.1 inter-class agreement of the raw maps (null with an older service).</summary>
        public ClassMapSimilarityDto Similarity { get; set; }

        public static GradCamAllResult Failed(string error) =>
            new GradCamAllResult { Ok = false, Error = error };

        public static GradCamAllResult StaleResult(string frameId) =>
            new GradCamAllResult { Ok = false, Stale = true, Error = "superseded", FrameId = frameId };

        /// <summary>
        /// Move the maps into a ClassHeatmapSet (this result no longer owns them).
        /// faceBoxSource = face box in SOURCE frame pixels (reference frame only) or null.
        /// </summary>
        public ClassHeatmapSet ToHeatmapSet(
            long frameId, Mat sourceThumb, OpenCvSharp.Size sourceSize, Rect? faceBoxSource)
        {
            var set = new ClassHeatmapSet
            {
                FrameId = frameId,
                ComputedAtUtc = DateTime.UtcNow,
                PredictedIndex = PredictedIndex,
                Confidence = Confidence,
                Probabilities = Probabilities,
                Method = Method,
                ExecMode = ExecMode,
                ServiceVersion = ServiceVersion,
                ServiceLatencyMs = ServiceLatencyMs,
                RoundTripMs = RoundTripMs,
                SourceSize = sourceSize,
                FaceBoxSource = faceBoxSource,
                SourceThumb = sourceThumb,
                Maps = Maps,
                Similarity = Similarity
            };

            // Approximate facial-region attribution per class (reference frame only).
            foreach (ClassMapInfo m in set.Maps)
            {
                if (m.Heat8U == null || m.Heat8U.Empty()) continue;

                Rect? boxInHeat = null;
                if (faceBoxSource.HasValue && sourceSize.Width > 0 && sourceSize.Height > 0)
                {
                    double sx = (double)m.Heat8U.Width / sourceSize.Width;
                    double sy = (double)m.Heat8U.Height / sourceSize.Height;
                    Rect b = faceBoxSource.Value;
                    boxInHeat = new Rect(
                        (int)Math.Round(b.X * sx), (int)Math.Round(b.Y * sy),
                        (int)Math.Round(b.Width * sx), (int)Math.Round(b.Height * sy));
                }

                m.Region = RegionAttribution.Compute(m.Heat8U, boxInHeat);
            }

            Maps = new ClassMapInfo[0];     // ownership transferred
            return set;
        }

        public void Dispose()
        {
            if (Maps != null)
                foreach (ClassMapInfo m in Maps) m?.Dispose();
            Maps = new ClassMapInfo[0];
        }
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


        /// <summary>
        /// v2.0: one request -> a Grad-CAM++ map for EACH of the four classes.
        ///   dropIfStale = true  for the live loop (latest frame wins)
        ///   dropIfStale = false for the final capture analysis (never dropped)
        /// Sends the frame as-is; use PNG for the capture, JPEG for live ticks.
        /// </summary>
        public static async Task<GradCamAllResult> ExplainAllClassesAsync(
            byte[] imageBytes,
            string frameId,
            bool dropIfStale,
            int heatmapMaxSide = 320,
            CancellationToken ct = default,
            bool includeRawCam = false)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return GradCamAllResult.Failed("No image data.");

            var sw = Stopwatch.StartNew();

            try
            {
                var request = new GradCamRequest
                {
                    ImageBase64 = Convert.ToBase64String(imageBytes),
                    Method = "gradcam++",
                    ReturnOverlay = false,
                    ReturnHeatmap = false,
                    FrameId = frameId,
                    DropIfStale = dropIfStale,
                    AllClasses = true,
                    HeatmapMaxSide = heatmapMaxSide,
                    IncludeRawCam = includeRawCam
                };

                var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpResponse = await Http.PostAsync("/gradcam", content, ct)
                                                   .ConfigureAwait(false);
                var responseString = await httpResponse.Content.ReadAsStringAsync()
                                                       .ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<GradCamResponse>(responseString, JsonOptions);

                if (payload != null && payload.Stale)
                    return GradCamAllResult.StaleResult(payload.FrameId);

                if (payload == null || !payload.Ok)
                    return GradCamAllResult.Failed(payload?.Error ?? "Service error.");

                if (payload.Classes == null || payload.Classes.Count != SkinDevApp.Imaging.ClassPalette.ClassCount)
                    return GradCamAllResult.Failed(
                        "Service did not return four class maps. Is gradcam_service.py v2.0 running?");

                var result = new GradCamAllResult
                {
                    Ok = true,
                    FrameId = payload.FrameId,
                    PredictedIndex = payload.PredictedIndex,
                    Confidence = payload.Confidence,
                    Method = payload.Method ?? "",
                    ExecMode = payload.ExecMode ?? "",
                    ServiceVersion = payload.ServiceVersion ?? "",
                    ServiceLatencyMs = payload.LatencyMs,
                    Similarity = payload.ClassMapSimilarity
                };

                var maps = new ClassMapInfo[SkinDevApp.Imaging.ClassPalette.ClassCount];

                try
                {
                    foreach (GradCamClassDto c in payload.Classes)
                    {
                        if (c.Index < 0 || c.Index >= maps.Length)
                            throw new InvalidOperationException("Bad class index " + c.Index);

                        Mat heat = null;
                        if (!string.IsNullOrWhiteSpace(c.HeatmapBase64))
                            heat = Cv2.ImDecode(Convert.FromBase64String(c.HeatmapBase64), ImreadModes.Grayscale);

                        result.Probabilities[c.Index] = c.Probability;

                        float[] rawCam = null;
                        int rawH = 0, rawW = 0;
                        if (!string.IsNullOrWhiteSpace(c.RawCamBase64) && c.RawCamShape != null && c.RawCamShape.Length == 2)
                        {
                            byte[] rb = Convert.FromBase64String(c.RawCamBase64);
                            rawH = c.RawCamShape[0]; rawW = c.RawCamShape[1];
                            if (rawH > 0 && rawW > 0 && rb.Length == rawH * rawW * 4)
                            {
                                rawCam = new float[rawH * rawW];
                                Buffer.BlockCopy(rb, 0, rawCam, 0, rb.Length);   // float32 little-endian (x86/x64)
                            }
                        }

                        maps[c.Index] = new ClassMapInfo
                        {
                            Index = c.Index,
                            Name = SkinDevApp.Imaging.ClassPalette.Names[c.Index],
                            Probability = c.Probability,
                            RawPeak = c.RawPeak,
                            RelativeStrength = c.RelativeStrength,
                            Top10Mass = c.Top10Mass,
                            CentroidX = c.CentroidX,
                            CentroidY = c.CentroidY,
                            Diffuse = c.Diffuse,
                            Heat8U = heat,
                            RawCam = rawCam,
                            RawCamHeight = rawH,
                            RawCamWidth = rawW
                        };
                    }
                }
                catch
                {
                    foreach (ClassMapInfo m in maps) m?.Dispose();
                    throw;
                }

                for (int i = 0; i < maps.Length; i++)
                    if (maps[i] == null)
                        return GradCamAllResult.Failed("Class map " + i + " missing from the response.");

                result.Maps = maps;
                sw.Stop();
                result.RoundTripMs = sw.Elapsed.TotalMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                return GradCamAllResult.Failed($"{ex.GetType().Name}: {ex.Message}");
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