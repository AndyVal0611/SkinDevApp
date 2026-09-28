// ============================================================================
// Livegradcamcontroller.cs  —  UPDATED VERSION
//
// Changes from your original (search "// FIX" to find every change):
//
//   FIX 1  Added _frameCounter for monotonic frame IDs (frame_id echo)
//   FIX 2  Added _stabilizer field (PredictionStabilizer)
//   FIX 3  LiveInfo now also carries StabilizedResult so the UI can read it
//   FIX 4  ExplainRawAsync receives the frame ID and its echo is verified;
//          stale responses are silently skipped (no error logged, no status flip)
//   FIX 5  ONNX Predict() offloaded to Task.Run so the background task thread
//          is not blocked during CPU inference
//   FIX 6  Frame encode switched from PNG to JPEG Q90 (saves ~10ms per tick)
//   FIX 7  PredictionStabilizer is updated and its result stored in LiveInfo
//   FIX 8  Stabilizer is Reset() when Stop() is called
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Imaging;

namespace SkinDevApp.Explainability
{
    public sealed class LiveInfo
    {
        public bool HasData { get; set; }

        /// <summary>Raw ONNX result for the current tick.</summary>
        public PredictionResult Onnx { get; set; }

        /// <summary>
        /// Stabilized display state (EMA + hysteresis + stable-frames gate).
        /// Read this for what to show in the UI instead of Onnx.PredictedClass.
        /// </summary>
        public StabilizedResult Stable { get; set; }

        public string ServiceClass { get; set; } = "";
        public int ServiceIndex { get; set; } = -1;
        public float ServiceConfidence { get; set; }
        public string Method { get; set; } = "";

        public double GradCamMs { get; set; }
        public DateTime ComputedAtUtc { get; set; }

        public bool Disagreement =>
            Onnx != null && ServiceIndex >= 0 && Onnx.PredictedIndex != ServiceIndex;
    }

    public sealed class LiveGradCamController : IDisposable
    {
        private readonly Func<Mat> _frameProvider;

        private CancellationTokenSource _cts;
        private Task _loop;

        private readonly object _stateLock = new object();
        private Mat _latestHeatmap;
        private Mat _latestSourceGray;
        private LiveInfo _latestInfo = new LiveInfo { HasData = false };

        // FIX 1: Monotonically increasing ID.  Each tick increments this and
        // sends the string value as frame_id to the service.  The echoed value
        // is compared on return; a mismatch means the response is stale.
        private long _frameCounter = 0;

        // FIX 2: Temporal stabilizer — keeps one instance alive for the whole
        // camera session so the EMA state accumulates across ticks.
        private readonly PredictionStabilizer _stabilizer;

        private bool _disposed;

        // ── Tuneable properties ───────────────────────────────────────────────

        /// <summary>Grad-CAM++ refresh period in ms.</summary>
        public int RefreshIntervalMs { get; set; } = 1000;

        /// <summary>Working frame width fed to ONNX and the Grad-CAM service.</summary>
        public int WorkingWidth { get; set; } = 640;

        /// <summary>Heatmap is fully hidden once it is older than this.</summary>
        public double MaxHeatmapAgeSeconds { get; set; } = 8.0;

        /// <summary>
        /// Mean-absolute-difference motion score above which the heatmap fades.
        /// 25.0 tolerates normal head movement during live video.
        /// </summary>
        public double MotionThreshold { get; set; } = 25.0;

        public double LastMotionScore { get; private set; }

        public bool Enabled { get; set; } = true;

        public bool ServiceOnline { get; private set; }
        public string LastError { get; private set; }

        public event Action<LiveInfo> AnalysisUpdated;
        public event Action<bool, string> ServiceStatusChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public LiveGradCamController(Func<Mat> frameProvider)
        {
            _frameProvider = frameProvider
                ?? throw new ArgumentNullException(nameof(frameProvider));

            // FIX 2: Build the stabilizer with the canonical class order.
            _stabilizer = new PredictionStabilizer(PredictionResult.ClassNames);
        }

        // ── Public state accessors ────────────────────────────────────────────

        public TimeSpan HeatmapAge
        {
            get
            {
                lock (_stateLock)
                {
                    if (!_latestInfo.HasData) return TimeSpan.MaxValue;
                    return DateTime.UtcNow - _latestInfo.ComputedAtUtc;
                }
            }
        }

        public Mat GetLatestHeatmapClone()
        {
            lock (_stateLock) { return _latestHeatmap?.Clone(); }
        }

        public LiveInfo GetLatestInfo()
        {
            lock (_stateLock) { return _latestInfo; }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LiveGradCamController));
            if (_loop != null && !_loop.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _loop?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException) { }

            _cts?.Dispose();
            _cts = null;
            _loop = null;

            lock (_stateLock)
            {
                _latestHeatmap?.Dispose();
                _latestHeatmap = null;
                _latestSourceGray?.Dispose();
                _latestSourceGray = null;
                _latestInfo = new LiveInfo { HasData = false };
            }

            // FIX 8: Reset the EMA state so a re-started camera begins fresh.
            _stabilizer.Reset();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        // ── Frame helpers ─────────────────────────────────────────────────────

        private Mat ToWorkingFrame(Mat frame) => ResizeToWidth(frame, WorkingWidth);

        public static Mat ResizeToWidth(Mat frame, int width)
        {
            if (frame.Width <= width) return frame.Clone();

            double scale = (double)width / frame.Width;
            Mat resized = new Mat();
            Cv2.Resize(
                frame, resized,
                new Size(width, (int)Math.Round(frame.Height * scale)),
                interpolation: InterpolationFlags.Linear);
            return resized;
        }

        private static Mat MakeMotionThumb(Mat bgr)
        {
            using (Mat gray = new Mat())
            {
                if (bgr.Channels() == 1) bgr.CopyTo(gray);
                else Cv2.CvtColor(bgr, gray, bgr.Channels() == 4
                    ? ColorConversionCodes.BGRA2GRAY
                    : ColorConversionCodes.BGR2GRAY);

                Mat small = new Mat();
                Cv2.Resize(gray, small, new Size(96, 96),
                    interpolation: InterpolationFlags.Area);
                Cv2.GaussianBlur(small, small, new Size(5, 5), 0);
                return small;
            }
        }

        // ── Overlay alpha (called every video frame from the camera thread) ───

        public double GetOverlayAlpha(
            Mat currentFrameBgr,
            double baseAlpha = HeatmapRenderer.DefaultAlpha)
        {
            Mat srcThumb;
            double ageSec;

            lock (_stateLock)
            {
                if (!_latestInfo.HasData || _latestSourceGray == null) return 0.0;
                ageSec = (DateTime.UtcNow - _latestInfo.ComputedAtUtc).TotalSeconds;
                srcThumb = _latestSourceGray.Clone();
            }

            using (srcThumb)
            {
                if (ageSec >= MaxHeatmapAgeSeconds) return 0.0;

                double half = MaxHeatmapAgeSeconds * 0.5;
                double ageFactor = ageSec <= half
                    ? 1.0
                    : Math.Max(0.0, 1.0 - (ageSec - half) / half);

                using (Mat cur = MakeMotionThumb(currentFrameBgr))
                using (Mat a = new Mat())
                using (Mat b = new Mat())
                using (Mat d = new Mat())
                {
                    srcThumb.ConvertTo(a, MatType.CV_32FC1);
                    cur.ConvertTo(b, MatType.CV_32FC1);

                    Cv2.Subtract(a, new Scalar(Cv2.Mean(a).Val0), a);
                    Cv2.Subtract(b, new Scalar(Cv2.Mean(b).Val0), b);

                    Cv2.Absdiff(a, b, d);
                    double motion = Cv2.Mean(d).Val0;
                    LastMotionScore = motion;

                    double motionFactor = Math.Max(0.0, Math.Min(1.0,
                        (MotionThreshold - motion) / (MotionThreshold * 0.5)));

                    return baseAlpha * ageFactor * motionFactor;
                }
            }
        }

        // ── Background loop ───────────────────────────────────────────────────

        private async Task LoopAsync(CancellationToken ct)
        {
            bool online = await GradCamService.IsReadyAsync(ct).ConfigureAwait(false);
            SetStatus(online, online ? null : "Grad-CAM service not reachable");

            while (!ct.IsCancellationRequested)
            {
                DateTime tickStart = DateTime.UtcNow;

                if (!Enabled)
                {
                    await DelayRemainder(tickStart, ct).ConfigureAwait(false);
                    continue;
                }

                Mat raw = null;

                try
                {
                    raw = _frameProvider();

                    if (raw == null || raw.Empty())
                    {
                        await DelayRemainder(tickStart, ct).ConfigureAwait(false);
                        continue;
                    }

                    using (Mat working = ToWorkingFrame(raw))
                    {
                        Mat sourceThumb = MakeMotionThumb(working);

                        // FIX 5: Offload ONNX inference to the thread pool so
                        // this async method doesn't block its thread during CPU work.
                        PredictionResult onnx =
                            await Task.Run(() => AiEngine.Predict(working), ct)
                                      .ConfigureAwait(false);

                        // FIX 7: Feed raw probabilities into the stabilizer immediately
                        // after ONNX returns, before even sending to the Grad-CAM service.
                        StabilizedResult stable = _stabilizer.Update(onnx.Probabilities);

                        // FIX 1 + FIX 6: Monotonic frame ID for stale detection;
                        // JPEG Q90 instead of PNG (~10ms saved per tick at 640px).
                        long thisFrameId = Interlocked.Increment(ref _frameCounter);
                        string frameIdStr = thisFrameId.ToString();

                        byte[] jpg = working.ImEncode(".jpg", new[]
                        {
                            new ImageEncodingParam(ImwriteFlags.JpegQuality, 90)
                        });

                        GradCamRawResult cam = await GradCamService
                            .ExplainRawAsync(jpg, frameId: frameIdStr, ct: ct)
                            .ConfigureAwait(false);

                        // FIX 4a: Silently skip stale responses — no error, no status flip.
                        if (cam.WasStale)
                        {
                            cam.Dispose();
                            sourceThumb.Dispose();
                            await DelayRemainder(tickStart, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (!cam.Ok)
                        {
                            SetStatus(false, cam.Error);
                            cam.Dispose();
                            sourceThumb.Dispose();
                        }
                        else
                        {
                            // FIX 4b: Discard response if the echoed frame_id doesn't
                            // match.  This catches the case where the service processed
                            // a previous frame even without drop_if_stale firing.
                            if (cam.EchoedFrameId != null && cam.EchoedFrameId != frameIdStr)
                            {
                                Debug.WriteLine(
                                    $"[LIVE] frame_id mismatch: sent {frameIdStr}, got {cam.EchoedFrameId} — discarded");
                                cam.Dispose();
                                sourceThumb.Dispose();
                                await DelayRemainder(tickStart, ct).ConfigureAwait(false);
                                continue;
                            }

                            SetStatus(true, null);

                            // FIX 7: Store stable result alongside the raw result.
                            var info = new LiveInfo
                            {
                                HasData = true,
                                Onnx = onnx,
                                Stable = stable,    // <-- new field
                                ServiceIndex = cam.PredictedIndex,
                                ServiceClass = cam.PredictedClass,
                                ServiceConfidence = cam.Confidence,
                                Method = cam.Method,
                                GradCamMs = cam.ServiceLatencyMs,
                                ComputedAtUtc = DateTime.UtcNow
                            };

                            Mat previousHeatmap;
                            Mat previousThumb;

                            lock (_stateLock)
                            {
                                previousHeatmap = _latestHeatmap;
                                previousThumb = _latestSourceGray;
                                _latestHeatmap = cam.Heatmap;
                                _latestSourceGray = sourceThumb;
                                _latestInfo = info;
                            }

                            previousHeatmap?.Dispose();
                            previousThumb?.Dispose();

                            if (info.Disagreement)
                            {
                                Debug.WriteLine(
                                    $"[LIVE MISMATCH] onnx={onnx.PredictedClass} keras={info.ServiceClass}");
                            }

                            AnalysisUpdated?.Invoke(info);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    raw?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus(false, ex.Message);
                    Debug.WriteLine($"[LIVE] tick failed: {ex}");
                }
                finally
                {
                    raw?.Dispose();
                }

                await DelayRemainder(tickStart, ct).ConfigureAwait(false);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task DelayRemainder(DateTime tickStart, CancellationToken ct)
        {
            TimeSpan elapsed = DateTime.UtcNow - tickStart;
            TimeSpan remaining = TimeSpan.FromMilliseconds(RefreshIntervalMs) - elapsed;

            if (remaining > TimeSpan.Zero)
            {
                try { await Task.Delay(remaining, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        private void SetStatus(bool online, string error)
        {
            if (ServiceOnline == online && LastError == error) return;
            ServiceOnline = online;
            LastError = error;
            ServiceStatusChanged?.Invoke(online, error);
        }
    }
}