// ============================================================================
// Livegradcamcontroller.cs  —  v2 (decoupled loops + auto-capture)
//
// WHY THIS WAS REWRITTEN
//   The old controller ran ONNX -> stabiliser -> Grad-CAM in ONE loop. With a
//   slow Grad-CAM (several seconds on a weak CPU) the UI state, the scores and
//   the heatmap all waited for it, and the heatmap was hidden by the age gate
//   before the next one arrived.
//
// NOW: two independent loops, plus a final analysis path
//
//   FAST loop  (~200 ms)   latest frame -> quality/face guide -> ONNX ->
//                          temporal stabiliser -> scan state machine.
//                          NEVER waits for Grad-CAM.
//   SLOW loop  (as fast as the service allows)
//                          latest frame -> all-class Grad-CAM++ (service v2.0).
//                          "Latest frame wins": a newer frame replaces a
//                          waiting one; results whose frame_id does not match
//                          are discarded.
//   FINAL path (once per capture)
//                          the EXACT frame that satisfied the gates -> all-class
//                          Grad-CAM++ (PNG, never dropped) -> archive
//                          -> ScanCompleted event.
//
// The camera thread only calls RenderLiveOverlay(frame) (cheap: it blends a
// pre-composited class-coloured layer) so the video never blocks.
//
// Threading: events are raised on background threads. The UI must marshal with
// Dispatcher.BeginInvoke (never Invoke) so it cannot stall the loops.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Imaging;
using SkinDevApp.Scanning;

namespace SkinDevApp.Explainability
{
    /// <summary>One fast-loop tick, plus the freshest heatmap facts. Immutable snapshot.</summary>
    public sealed class LiveSnapshot
    {
        public long FrameId { get; set; }
        public DateTime TickUtc { get; set; }

        public PredictionResult Onnx { get; set; }
        public StabilizedResult Stable { get; set; }
        public FrameQuality Quality { get; set; }
        public ScanDecision Decision { get; set; }

        public double Motion { get; set; }
        public double FastLoopMs { get; set; }

        // Heatmap side (slow loop)
        public bool HeatmapAvailable { get; set; }
        public double HeatmapAgeSeconds { get; set; }
        public double GradCamMs { get; set; }
        public double GradCamRoundTripMs { get; set; }
        public string ExecMode { get; set; } = "";
        public int ServiceIndex { get; set; } = -1;
        public string ServiceClass { get; set; } = "";
        public string RegionText { get; set; } = "";
        public bool ServiceOnline { get; set; }

        /// <summary>ONNX top class differs from the service's top class (researcher info only).</summary>
        public bool Disagreement =>
            Onnx != null && ServiceIndex >= 0 && Onnx.PredictedIndex != ServiceIndex;
    }

    public sealed class LiveGradCamController : IDisposable
    {
        private readonly Func<Mat> _frameProvider;

        private CancellationTokenSource _cts;
        private Task _fastLoop;
        private Task _slowLoop;
        private Task _finalTask;

        private readonly FrameQualityAnalyzer _quality = new FrameQualityAnalyzer();
        private readonly PredictionStabilizer _stabilizer;
        private readonly ScanStateMachine _machine = new ScanStateMachine();
        private readonly ViewPoseEstimator _pose = new ViewPoseEstimator();
        private volatile int _requiredView = (int)ScanView.Any;
        private volatile MultiViewSession _session;
        private volatile PoseReading _lastPose;
        private volatile string _poseMessage = "";

        // --- shared state (guarded by _stateLock) -------------------------------
        private readonly object _stateLock = new object();
        private ClassHeatmapSet _maps;
        private ClassOverlayLayer _layer;
        private string _layerKey = "";
        private int _displayIndex = -1;

        // --- hand-off from fast loop to slow loop (latest frame wins) ----------
        private readonly object _pendingLock = new object();
        private PendingFrame _pending;

        private Mat _prevThumb;
        private long _frameCounter;
        private int _manualCaptureFlag;
        private volatile bool _finalBusy;
        private bool _disposed;

        private sealed class PendingFrame : IDisposable
        {
            public long Id;
            public Mat Working;
            public Mat Thumb;
            public Rect? FaceBox;
            public void Dispose() { Working?.Dispose(); Thumb?.Dispose(); }
        }

        // ── Tunables ────────────────────────────────────────────────────────────

        /// <summary>Fast loop period (ONNX + guide + state machine).</summary>
        public int FastLoopMs { get; set; } = 200;

        /// <summary>Pause between Grad-CAM requests, to leave CPU for the UI.</summary>
        public int MinGradCamIntervalMs { get; set; } = 80;

        /// <summary>Working frame width for ONNX and Grad-CAM.</summary>
        public int WorkingWidth { get; set; } = 640;

        /// <summary>A heatmap older than this is never drawn.</summary>
        public double MaxHeatmapAgeSeconds { get; set; } = 6.0;

        /// <summary>Mean-removed motion score above which the overlay fades out.</summary>
        public double MotionThreshold { get; set; } = 25.0;

        /// <summary>Which class map(s) the live overlay draws.</summary>
        public OverlayView View { get; set; } = OverlayView.Predicted;

        /// <summary>Show the freeze frame for this long before the final analysis starts.</summary>
        public int CaptureFlashMs { get; set; } = 600;

        public bool Enabled { get; set; } = true;

        // ── Multi-view scan ─────────────────────────────────────────────────────

        /// <summary>
        /// The view the patient must show before auto-capture may fire.
        /// ScanView.Any (default) = the old single-capture behaviour, no pose gate.
        /// </summary>
        public ScanView RequiredView
        {
            get { return (ScanView)_requiredView; }
            set { _requiredView = (int)value; _pose.Reset(); }
        }

        /// <summary>When set (with RequiredView != Any) captures are filed under this session.</summary>
        public MultiViewSession Session
        {
            get { return _session; }
            set { _session = value; }
        }

        /// <summary>Latest head-pose measurement (null when no view is required).</summary>
        public PoseReading LastPose { get { return _lastPose; } }

        /// <summary>Latest pose instruction, for researcher metrics.</summary>
        public string PoseMessage { get { return _poseMessage; } }

        /// <summary>False when the YuNet model file is missing: pose is then instruction-only.</summary>
        public bool PoseCheckAvailable { get { return _pose.Available; } }
        public string PoseLoadError { get { return _pose.LoadError; } }

        public bool AutoCaptureEnabled
        {
            get { return _machine.AutoCaptureEnabled; }
            set { _machine.AutoCaptureEnabled = value; }
        }

        /// <summary>
        /// Require a well-positioned face before auto-capture. Ignored (treated as
        /// false) when the Haar cascade file is missing.
        /// </summary>
        public bool RequireFaceForAutoCapture { get; set; } = true;

        public ScanStateMachine Machine => _machine;

        public double LastMotionScore { get; private set; }
        public bool ServiceOnline { get; private set; }
        public string LastError { get; private set; }
        public bool FaceGuideAvailable => _quality.CascadeAvailable;

        // ── Events (raised on background threads!) ──────────────────────────────

        /// <summary>Every fast-loop tick.</summary>
        public event Action<LiveSnapshot> FastUpdated;

        /// <summary>
        /// A capture was triggered. The Mat is a CLONE of the exact frame; the
        /// receiver owns and must dispose it.
        /// </summary>
        public event Action<string, Mat> CaptureTriggered;

        /// <summary>Final analysis finished. The receiver owns and must dispose the result.</summary>
        public event Action<ScanResult> ScanCompleted;

        public event Action<bool, string> ServiceStatusChanged;

        // ── Construction ────────────────────────────────────────────────────────

        public LiveGradCamController(Func<Mat> frameProvider)
        {
            _frameProvider = frameProvider ?? throw new ArgumentNullException(nameof(frameProvider));

            // At ~5 ticks/s: alpha 0.25 gives a ~0.8 s memory; 8 consecutive ticks ~ 1.6 s.
            _stabilizer = new PredictionStabilizer(PredictionResult.ClassNames)
            {
                EmaAlpha = 0.25f,
                ConfidenceThreshold = 0.65f,
                HysteresisMargin = 0.10f,
                StableFramesRequired = 8
            };

            _machine.EmaAlpha = _stabilizer.EmaAlpha;
            _machine.HysteresisMargin = _stabilizer.HysteresisMargin;
            _machine.StableFramesRequired = _stabilizer.StableFramesRequired;
            _machine.MinConfidence = _stabilizer.ConfidenceThreshold;
        }

        /// <summary>
        /// Researcher settings (Settings page). Call before Start(). The stabiliser and the
        /// state machine get the same thresholds so the saved capture metadata matches.
        /// </summary>
        public void ApplySettings(int stableFrames, int holdStillMs, double cooldownSeconds,
                                  float minConfidence, float minMargin, double minConsistency,
                                  double minSharpness, double minBrightness, double maxBrightness,
                                  double frontMaxAbsYaw, double sideMinAbsYaw)
        {
            _stabilizer.StableFramesRequired = stableFrames;
            _stabilizer.ConfidenceThreshold = minConfidence;

            _machine.StableFramesRequired = stableFrames;
            _machine.MinConfidence = minConfidence;
            _machine.MinMargin = minMargin;
            _machine.MinConsistency = minConsistency;
            _machine.HoldStillMs = holdStillMs;
            _machine.CooldownSeconds = cooldownSeconds;

            _quality.MinSharpness = minSharpness;
            _quality.MinBrightness = minBrightness;
            _quality.MaxBrightness = maxBrightness;

            _pose.FrontMaxAbsYaw = frontMaxAbsYaw;
            _pose.SideMinAbsYaw = sideMinAbsYaw;
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LiveGradCamController));
            if (_fastLoop != null && !_fastLoop.IsCompleted) return;

            _stabilizer.Reset();
            _quality.Reset();
            Interlocked.Exchange(ref _manualCaptureFlag, 0);
            _machine.Start();
            _finalBusy = false;

            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            _fastLoop = Task.Run(() => FastLoopAsync(ct));
            _slowLoop = Task.Run(() => SlowLoopAsync(ct));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                Task[] tasks = { _fastLoop, _slowLoop, _finalTask };
                foreach (Task t in tasks)
                    if (t != null) t.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException) { }

            _cts?.Dispose();
            _cts = null;
            _fastLoop = _slowLoop = _finalTask = null;

            lock (_pendingLock)
            {
                _pending?.Dispose();
                _pending = null;
            }

            lock (_stateLock)
            {
                _layer?.Dispose(); _layer = null; _layerKey = "";
                _maps?.Dispose(); _maps = null;
            }

            _prevThumb?.Dispose();
            _prevThumb = null;

            _stabilizer.Reset();
            _quality.Reset();
            _machine.Stop();
            _finalBusy = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _quality.Dispose();
            _pose.Dispose();
            _disposed = true;
        }

        /// <summary>User pressed "New Scan": cooldown, fresh stabiliser, back to Live.</summary>
        public void NewScan()
        {
            lock (_stateLock)
            {
                _layer?.Dispose(); _layer = null; _layerKey = "";
                _maps?.Dispose(); _maps = null;
            }

            lock (_pendingLock)
            {
                _pending?.Dispose();
                _pending = null;
            }

            _stabilizer.Reset();
            _quality.Reset();
            _pose.Reset();
            _prevThumb?.Dispose();
            _prevThumb = null;

            Interlocked.Exchange(ref _manualCaptureFlag, 0);
            _machine.NewScan(DateTime.UtcNow);
            _finalBusy = false;
        }

        /// <summary>Manual Snap: capture the next analysed frame regardless of the gates.</summary>
        public void RequestManualCapture()
        {
            Interlocked.Exchange(ref _manualCaptureFlag, 1);
        }

        // ── Frame helpers ───────────────────────────────────────────────────────

        public static Mat ResizeToWidth(Mat frame, int width)
        {
            if (frame.Width <= width) return frame.Clone();

            double scale = (double)width / frame.Width;
            Mat resized = new Mat();
            Cv2.Resize(
                frame, resized,
                new OpenCvSharp.Size(width, (int)Math.Round(frame.Height * scale)),
                interpolation: InterpolationFlags.Linear);
            return resized;
        }

        // ── Live overlay (called for every video frame, camera thread) ──────────

        /// <summary>
        /// Blend the freshest class-coloured heatmap onto <paramref name="frameBgr"/>.
        /// Returns null when there is nothing valid to draw (no heatmap yet, too
        /// old, or the subject moved too much since the heatmap's frame). Caller
        /// disposes the returned Mat.
        /// </summary>
        public Mat RenderLiveOverlay(Mat frameBgr)
        {
            if (frameBgr == null || frameBgr.Empty()) return null;

            using (Mat cur = MotionMeter.MakeThumb(frameBgr))
            {
                lock (_stateLock)
                {
                    if (_maps == null || _maps.SourceThumb == null) return null;

                    double ageSec = (DateTime.UtcNow - _maps.ComputedAtUtc).TotalSeconds;
                    if (ageSec >= MaxHeatmapAgeSeconds) return null;

                    double half = MaxHeatmapAgeSeconds * 0.5;
                    double ageFactor = ageSec <= half
                        ? 1.0
                        : Math.Max(0.0, 1.0 - (ageSec - half) / half);

                    double motion = MotionMeter.Between(_maps.SourceThumb, cur);
                    LastMotionScore = motion;

                    double motionFactor = Math.Max(0.0, Math.Min(1.0,
                        (MotionThreshold - motion) / (MotionThreshold * 0.5)));

                    double factor = ageFactor * motionFactor;
                    if (factor < 0.02) return null;

                    ClassOverlayLayer layer = GetLayerLocked();
                    if (layer == null) return null;

                    return layer.Blend(frameBgr, factor);
                }
            }
        }

        // _stateLock must be held.
        private ClassOverlayLayer GetLayerLocked()
        {
            if (_maps == null) return null;

            string key = _maps.FrameId + "|" + (int)View + "|" + _displayIndex;
            if (_layer != null && _layerKey == key) return _layer;

            _layer?.Dispose();
            _layer = ClassHeatmapRenderer.BuildLayer(_maps, _maps.IndicesForView(View, _displayIndex));
            _layerKey = key;
            return _layer;
        }

        /// <summary>Force the cached layer to be rebuilt (e.g. after changing View).</summary>
        public void InvalidateOverlay()
        {
            lock (_stateLock) { _layerKey = ""; }
        }

        // ── FAST loop ───────────────────────────────────────────────────────────

        private async Task FastLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                DateTime tickStart = DateTime.UtcNow;

                try
                {
                    if (!Enabled || _machine.IsBusy)
                    {
                        await DelayRemainder(tickStart, FastLoopMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    Mat raw = _frameProvider();

                    if (raw == null || raw.Empty())
                    {
                        raw?.Dispose();
                        await DelayRemainder(tickStart, FastLoopMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    Mat working = ResizeToWidth(raw, WorkingWidth);
                    bool rawOwnedByCapture = false;

                    try
                    {
                        // ---- quality / guide (positioning aid only) ---------------
                        FrameQuality q = _quality.Analyze(working);

                        // ---- multi-view: head-pose gate for the requested view ------
                        ScanView required = RequiredView;
                        PoseReading pose = null;
                        PoseVerdict verdict = null;
                        if (required != ScanView.Any)
                        {
                            pose = _pose.Estimate(working);
                            verdict = _pose.Evaluate(required, pose, working.Width, working.Height);
                            _lastPose = pose;
                            ApplyPoseToQuality(q, required, pose, verdict);
                            _poseMessage = verdict.Message;
                        }
                        else
                        {
                            _lastPose = null;
                            _poseMessage = "";
                        }

                        // ---- motion between consecutive ticks ---------------------
                        Mat thumb = MotionMeter.MakeThumb(working);
                        double motion = _prevThumb == null ? 0.0 : MotionMeter.Between(_prevThumb, thumb);
                        _prevThumb?.Dispose();
                        _prevThumb = thumb.Clone();

                        // ---- classification: ALWAYS on the whole frame -----------
                        // (no face-detection gate: the face box never reaches the model)
                        PredictionResult onnx = AiEngine.Predict(working);
                        StabilizedResult stable = _stabilizer.Update(onnx.Probabilities);

                        long id = Interlocked.Increment(ref _frameCounter);

                        int displayIdx = Array.IndexOf(PredictionResult.ClassNames, stable.DisplayClass);
                        lock (_stateLock) { _displayIndex = displayIdx; }

                        // ---- offer the frame to the slow loop (latest wins) -------
                        Offer(new PendingFrame
                        {
                            Id = id,
                            Working = working.Clone(),
                            Thumb = thumb,                    // ownership moves to PendingFrame
                            FaceBox = q.FaceBox
                        });

                        // ---- scan state machine -------------------------------------
                        bool requireFace = RequireFaceForAutoCapture && q.CascadeAvailable;
                        DateTime now = DateTime.UtcNow;

                        bool manual = Interlocked.Exchange(ref _manualCaptureFlag, 0) == 1;

                        ScanDecision decision = manual
                            ? _machine.ForceCapture(stable, now)
                            : _machine.Update(new ScanInputs
                            {
                                NowUtc = now,
                                FaceFound = q.FaceFound,
                                FaceReady = q.Ready,
                                QualityOk = q.QualityOk,
                                Motion = motion,
                                Stable = stable,
                                RawPredictedIndex = onnx.PredictedIndex,
                                RequireFace = requireFace,
                                PoseOk = verdict == null || verdict.Ok,
                                PoseMessage = verdict != null ? verdict.Message : ""
                            });

                        // ---- publish ---------------------------------------------
                        FastUpdated?.Invoke(BuildSnapshot(id, now, onnx, stable, q, decision, motion, tickStart));

                        // ---- capture: the EXACT frame that was just analysed -------
                        if (decision.CaptureNow)
                        {
                            _finalBusy = true;

                            var req = new CaptureRequest
                            {
                                Trigger = decision.Metrics.Trigger,
                                FrameId = id,
                                Original = raw,               // full-resolution frame as captured
                                Working = working.Clone(),    // what ONNX and Grad-CAM see
                                Onnx = onnx,
                                Stable = stable,
                                Quality = q,
                                Stability = decision.Metrics,
                                Pose = pose,
                                PoseVerified = verdict != null && verdict.Verified && verdict.Ok
                            };

                            MultiViewSession session = _session;
                            if (session != null && required != ScanView.Any)
                                session.Stamp(req, required);

                            rawOwnedByCapture = true;
                            _finalTask = Task.Run(() => RunCaptureAsync(req, ct));
                        }
                    }
                    finally
                    {
                        working.Dispose();
                        if (!rawOwnedByCapture) raw.Dispose();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[LIVE] fast tick failed: " + ex);
                }

                await DelayRemainder(tickStart, FastLoopMs, ct).ConfigureAwait(false);
            }
        }

        private LiveSnapshot BuildSnapshot(
            long id, DateTime now, PredictionResult onnx, StabilizedResult stable,
            FrameQuality q, ScanDecision decision, double motion, DateTime tickStart)
        {
            var snap = new LiveSnapshot
            {
                FrameId = id,
                TickUtc = now,
                Onnx = onnx,
                Stable = stable,
                Quality = q,
                Decision = decision,
                Motion = motion,
                FastLoopMs = (DateTime.UtcNow - tickStart).TotalMilliseconds,
                ServiceOnline = ServiceOnline
            };

            lock (_stateLock)
            {
                if (_maps != null)
                {
                    snap.HeatmapAvailable = true;
                    snap.HeatmapAgeSeconds = (now - _maps.ComputedAtUtc).TotalSeconds;
                    snap.GradCamMs = _maps.ServiceLatencyMs;
                    snap.GradCamRoundTripMs = _maps.RoundTripMs;
                    snap.ExecMode = _maps.ExecMode;
                    snap.ServiceIndex = _maps.PredictedIndex;
                    snap.ServiceClass = _maps.PredictedIndex >= 0
                        ? ClassPalette.Names[_maps.PredictedIndex] : "";
                    snap.RegionText = _maps.RegionSummary();
                }
            }

            return snap;
        }

        private void Offer(PendingFrame frame)
        {
            lock (_pendingLock)
            {
                _pending?.Dispose();      // newer frame replaces the waiting one
                _pending = frame;
            }
        }

        // ── SLOW loop (live Grad-CAM++) ─────────────────────────────────────────

        private DateTime _lastHealthCheckUtc = DateTime.MinValue;

        private async Task SlowLoopAsync(CancellationToken ct)
        {
            bool online = await GradCamService.IsReadyAsync(ct).ConfigureAwait(false);
            SetStatus(online, online ? null : "Grad-CAM service not reachable");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!Enabled || _finalBusy)
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                        continue;
                    }

                    PendingFrame pf;
                    lock (_pendingLock) { pf = _pending; _pending = null; }

                    if (pf == null)
                    {
                        // No frame waiting. While the service is flagged offline (or we have
                        // heard nothing for a while) re-check /health so the badge recovers
                        // as soon as the service is up, even before the first frame arrives.
                        if ((!ServiceOnline && (DateTime.UtcNow - _lastHealthCheckUtc).TotalSeconds >= 2.0))
                        {
                            _lastHealthCheckUtc = DateTime.UtcNow;
                            bool up = await GradCamService.IsReadyAsync(ct).ConfigureAwait(false);
                            if (up) SetStatus(true, null);
                        }
                        await Task.Delay(30, ct).ConfigureAwait(false);
                        continue;
                    }

                    bool handedOver = false;

                    try
                    {
                        byte[] jpg = pf.Working.ImEncode(".jpg", new[]
                        {
                            new ImageEncodingParam(ImwriteFlags.JpegQuality, 90)
                        });

                        string idStr = pf.Id.ToString();

                        GradCamAllResult r = await GradCamService
                            .ExplainAllClassesAsync(jpg, idStr, true, ScanPipeline.ServiceHeatmapMaxSide, ct)
                            .ConfigureAwait(false);

                        try
                        {
                            if (r.Stale) continue;          // superseded: normal in live mode

                            if (!r.Ok)
                            {
                                SetStatus(false, r.Error);
                                await Task.Delay(500, ct).ConfigureAwait(false);
                                continue;
                            }

                            if (r.FrameId != idStr)
                            {
                                Debug.WriteLine("[LIVE] frame_id mismatch: sent " + idStr + ", got " + r.FrameId);
                                continue;
                            }

                            SetStatus(true, null);

                            ClassHeatmapSet set = r.ToHeatmapSet(
                                pf.Id, pf.Thumb, pf.Working.Size(), pf.FaceBox);
                            handedOver = true;                       // set owns pf.Thumb now

                            ClassHeatmapSet old;
                            lock (_stateLock)
                            {
                                old = _maps;
                                _maps = set;
                                _layer?.Dispose();
                                _layer = null;
                                _layerKey = "";
                            }
                            old?.Dispose();
                        }
                        finally
                        {
                            r.Dispose();
                        }
                    }
                    finally
                    {
                        if (handedOver)
                        {
                            pf.Thumb = null;                         // moved into the set
                        }
                        pf.Dispose();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus(false, ex.Message);
                    Debug.WriteLine("[LIVE] slow tick failed: " + ex);
                    try { await Task.Delay(500, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }

                try { await Task.Delay(MinGradCamIntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ── FINAL analysis ──────────────────────────────────────────────────────

        private async Task RunCaptureAsync(CaptureRequest req, CancellationToken ct)
        {
            try
            {
                // 1. freeze notification (the UI shows the exact captured frame)
                Mat freeze = req.Working.Clone();
                var handler = CaptureTriggered;
                if (handler != null) handler(req.Trigger, freeze);
                else freeze.Dispose();

                // brief "Stable — Capturing" flash before the heavy work
                await Task.Delay(CaptureFlashMs, ct).ConfigureAwait(false);

                _machine.BeginFinalAnalysis();

                // 2. all-class Grad-CAM++ on THE SAME frame + archive
                ScanResult result = await ScanPipeline.FinalAnalysisAsync(req, ct).ConfigureAwait(false);

                _machine.CompleteAnalysis();

                var done = ScanCompleted;
                if (done != null) done(result);
                else result.Dispose();
            }
            catch (OperationCanceledException)
            {
                req.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LIVE] capture failed: " + ex);
                _machine.CompleteAnalysis();
                ScanCompleted?.Invoke(new ScanResult { Ok = false, Error = ex.Message, Request = req });
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Folds the pose measurement into the quality/guide object the rest of the loop already uses.
        /// Front: the Haar positioning gate stays; a wrong pose only changes the message.
        /// Left/Right: the frontal Haar cascade cannot see a turned face, so the YuNet result supplies
        /// "face found / ready". FaceBox is cleared so the zone attribution never names a facial
        /// region from a box that was not measured on a frontal face.
        /// </summary>
        private void ApplyPoseToQuality(FrameQuality q, ScanView required, PoseReading pose, PoseVerdict verdict)
        {
            if (!_pose.Available)
            {
                if (required != ScanView.Front)
                {
                    q.FaceFound = false;
                    q.FaceBox = null;
                    q.Guidance = GuidanceState.Unavailable;
                    q.GuidanceText = "Pose model missing: press Snap Now when the view is right";
                }
                return;
            }

            if (required == ScanView.Front)
            {
                if (!verdict.Ok && q.Ready)
                {
                    q.Guidance = GuidanceState.CenterFace;
                    q.GuidanceText = verdict.Message;
                }
                return;
            }

            q.FaceFound = pose != null && pose.FaceFound;
            q.FaceBox = null;
            if (verdict.Ok)
            {
                q.Guidance = GuidanceState.Ready;
                q.GuidanceText = verdict.Message;
            }
            else
            {
                q.Guidance = q.FaceFound ? GuidanceState.CenterFace : GuidanceState.NoFace;
                q.GuidanceText = verdict.Message;
            }
        }

        private static async Task DelayRemainder(DateTime tickStart, int periodMs, CancellationToken ct)
        {
            TimeSpan elapsed = DateTime.UtcNow - tickStart;
            TimeSpan remaining = TimeSpan.FromMilliseconds(periodMs) - elapsed;

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