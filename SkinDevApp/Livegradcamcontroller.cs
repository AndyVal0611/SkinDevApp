using System;

using System.Diagnostics;

using System.Threading;

using System.Threading.Tasks;

using OpenCvSharp;

using SkinDevApp.AI;



namespace SkinDevApp.Explainability

{

    /// <summary>Snapshot of the latest live analysis, without the heatmap Mat

    /// itself (that's fetched separately via GetLatestHeatmapClone, to avoid

    /// handing out a reference the background loop might dispose next tick).</summary>

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



        /// <summary>True when ONNX and the Keras service disagree on the same frame.</summary>

        public bool Disagreement =>

            Onnx != null && ServiceIndex >= 0 && Onnx.PredictedIndex != ServiceIndex;

    }



    /// <summary>

    /// Runs continuous, THROTTLED Grad-CAM++ in the background while the

    /// camera is live.

    ///

    /// THE HARD CONSTRAINT

    /// --------------------

    /// Grad-CAM++ costs roughly 500ms-2s per pass on a CPU-only deployment

    /// machine. The camera produces a frame every ~33ms. Running a real

    /// gradient pass on every frame is not possible on that hardware - not

    /// slow, impossible. So the work is split by cost:

    ///

    ///     ONNX classification   -> every tick (cheap, tens of ms)

    ///     Grad-CAM++ gradient   -> once per RefreshIntervalMs (default 2000)

    ///     heatmap compositing   -> every camera frame, in the UI layer

    ///

    /// The preview looks continuously live because the compositing runs at

    /// full frame rate; the attribution behind it refreshes about twice a

    /// second. State this distinction accurately in the thesis - this is not

    /// 30 FPS Grad-CAM++.

    ///

    /// The frame provider downsizes to WorkingWidth before analysis, so ONNX

    /// and the Grad-CAM service see identical pixels and cannot disagree for

    /// preprocessing reasons.

    /// </summary>

    public sealed class LiveGradCamController : IDisposable

    {

        private readonly Func<Mat> _frameProvider;



        private CancellationTokenSource _cts;

        private Task _loop;



        private readonly object _stateLock = new object();

        private Mat _latestHeatmap;

        private LiveInfo _latestInfo = new LiveInfo { HasData = false };



        private bool _disposed;



        /// <summary>Grad-CAM++ refresh period. 2000ms is the tested default.</summary>

        public int RefreshIntervalMs { get; set; } = 2000;



        /// <summary>Frames are downscaled to this width before analysis.</summary>

        public int WorkingWidth { get; set; } = 640;



        /// <summary>Set false to pause analysis without tearing down the loop.</summary>

        public bool Enabled { get; set; } = true;



        public bool ServiceOnline { get; private set; }

        public string LastError { get; private set; }



        /// <summary>Raised on the background thread after each completed analysis.</summary>

        public event Action<LiveInfo> AnalysisUpdated;



        /// <summary>Raised when the service goes offline or comes back.</summary>

        public event Action<bool, string> ServiceStatusChanged;



        public LiveGradCamController(Func<Mat> frameProvider)

        {

            _frameProvider = frameProvider ?? throw new ArgumentNullException(nameof(frameProvider));

        }



        /// <summary>How old the current heatmap is. TimeSpan.MaxValue if none yet.</summary>

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



        /// <summary>Cloned copy of the current heatmap, or null if none yet. Caller disposes.</summary>

        public Mat GetLatestHeatmapClone()

        {

            lock (_stateLock)

            {

                return _latestHeatmap?.Clone();

            }

        }



        /// <summary>Snapshot of the latest classification/status (no image data).</summary>

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



        private Mat ToWorkingFrame(Mat frame)

        {

            if (frame.Width <= WorkingWidth) return frame.Clone();



            double scale = (double)WorkingWidth / frame.Width;



            Mat working = new Mat();

            Cv2.Resize(

                frame, working,

                new Size(WorkingWidth, (int)Math.Round(frame.Height * scale)),

                interpolation: InterpolationFlags.Linear);



            return working;

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

                        // --- ONNX on the working frame -------------------------

                        PredictionResult onnx = AiEngine.Predict(working);



                        // --- Grad-CAM++ on the SAME pixels ----------------------

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



                            lock (_stateLock)

                            {

                                previousHeatmap = _latestHeatmap;

                                _latestHeatmap = cam.Heatmap;   // take ownership - do not dispose cam

                                _latestInfo = info;

                            }



                            previousHeatmap?.Dispose();



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

                    // Dito ay ipinapasa na natin ang eksaktong exception message sa UI status

                    SetStatus(false, $"{ex.GetType().Name}: {ex.Message}");

                    Debug.WriteLine($"[LIVE] tick failed: {ex}");

                }

                finally

                {

                    raw?.Dispose();

                }



                await DelayRemainder(tickStart, ct).ConfigureAwait(false);

            }

        }



        /// <summary>

        /// Waits out the remainder of the refresh period. If a pass overran

        /// the interval, the next one starts immediately rather than

        /// queueing, so a slow machine degrades gracefully instead of

        /// building a backlog.

        /// </summary>

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

