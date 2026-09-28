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
        public PredictionResult Onnx { get; set; }

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

        private bool _disposed;

        /// <summary>Grad-CAM++ refresh period. 1000ms ensures smooth continuous polling.</summary>
        public int RefreshIntervalMs { get; set; } = 1000;

        /// <summary>Frames are downscaled to this width before analysis.</summary>
        public int WorkingWidth { get; set; } = 640;

        /// <summary>
        /// Extended to 8.0s to prevent premature age-fading when requests take longer.
        /// </summary>
        public double MaxHeatmapAgeSeconds { get; set; } = 8.0;

        /// <summary>
        /// Motion threshold set to 25.0 to tolerate natural head movement during live video feeds.
        /// </summary>
        public double MotionThreshold { get; set; } = 25.0;

        public double LastMotionScore { get; private set; }

        public bool Enabled { get; set; } = true;

        public bool ServiceOnline { get; private set; }
        public string LastError { get; private set; }

        public event Action<LiveInfo> AnalysisUpdated;
        public event Action<bool, string> ServiceStatusChanged;

        public LiveGradCamController(Func<Mat> frameProvider)
        {
            _frameProvider = frameProvider ?? throw new ArgumentNullException(nameof(frameProvider));
        }

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
            lock (_stateLock)
            {
                return _latestHeatmap?.Clone();
            }
        }

        public LiveInfo GetLatestInfo()
        {
            lock (_stateLock)
            {
                return _latestInfo;
            }
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LiveGradCamController));
            if (_loop != null && !_loop.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        }

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
                    ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);

                Mat small = new Mat();
                Cv2.Resize(gray, small, new Size(96, 96), interpolation: InterpolationFlags.Area);
                Cv2.GaussianBlur(small, small, new Size(5, 5), 0);
                return small;
            }
        }

        public double GetOverlayAlpha(Mat currentFrameBgr, double baseAlpha = HeatmapRenderer.DefaultAlpha)
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
                        PredictionResult onnx = AiEngine.Predict(working);

                        byte[] png = working.ImEncode(".png");

                        Stopwatch sw = Stopwatch.StartNew();
                        GradCamRawResult cam = await GradCamService
                            .ExplainRawAsync(png, ct: ct)
                            .ConfigureAwait(false);
                        sw.Stop();

                        if (!cam.Ok)
                        {
                            SetStatus(false, cam.Error);
                            cam.Dispose();
                            sourceThumb.Dispose();
                        }
                        else
                        {
                            SetStatus(true, null);

                            var info = new LiveInfo
                            {
                                HasData = true,
                                Onnx = onnx,
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
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}