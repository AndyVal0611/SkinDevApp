// ============================================================================
// ScanStateMachine.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// The kiosk's scan flow, as a pure, thread-safe state machine (no UI, no I/O):
//
//   Live -> Scanning -> Stabilizing -> HoldStill -> StableCapturing
//        -> FinalAnalysis -> Results -> (NewScan) Cooldown -> Live
//
//   Live             camera on, no face / positioning stage
//   Scanning         face present, model not yet confident
//   Stabilizing      confident, but the class has not held long enough
//   HoldStill        class is stable; every gate must stay green for HoldStillMs
//   StableCapturing  gates held -> the EXACT current frame is captured
//   FinalAnalysis    all-class Grad-CAM++ on that frame + archive to disk
//   Results          frozen result shown (until NewScan)
//   Cooldown         short pause after NewScan so the previous capture cannot re-fire
//
// WHAT AUTO-CAPTURE MEANS (and does not mean):
//   It captures when the prediction has been TEMPORALLY CONSISTENT and the image
//   is good: confident, class unchanged for N ticks, clear margin over the
//   runner-up, raw per-tick predictions agreeing, no motion, sharp, well exposed
//   and (optionally) the face positioned. That shows the model is STABLE on
//   this subject - not that it is CORRECT. There is no ground truth at capture
//   time, so every capture is saved as Pending Review.
// ============================================================================

using System;
using System.Collections.Generic;
using SkinDevApp.AI;

namespace SkinDevApp.Scanning
{
    public enum ScanState
    {
        Idle = 0,
        Live,
        Scanning,
        Stabilizing,
        HoldStill,
        StableCapturing,
        FinalAnalysis,
        Results,
        Cooldown
    }

    public sealed class ScanInputs
    {
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        public bool FaceFound { get; set; }
        /// <summary>Face positioned correctly (guide says "Ready").</summary>
        public bool FaceReady { get; set; }
        /// <summary>Sharp + well exposed.</summary>
        public bool QualityOk { get; set; }

        /// <summary>Frame-to-frame motion score (MotionMeter.Between).</summary>
        public double Motion { get; set; }

        public StabilizedResult Stable { get; set; }

        /// <summary>This tick's RAW ONNX argmax (not smoothed).</summary>
        public int RawPredictedIndex { get; set; }

        /// <summary>False when the cascade is missing or the operator turned the face requirement off.</summary>
        public bool RequireFace { get; set; } = true;

        /// <summary>
        /// Multi-view scans: the head pose matches the requested view (Front / Left / Right).
        /// Always true in single-capture mode.
        /// </summary>
        public bool PoseOk { get; set; } = true;

        /// <summary>What to tell the patient when PoseOk is false.</summary>
        public string PoseMessage { get; set; } = "";
    }

    public sealed class ScanGates
    {
        public bool Face { get; set; }
        public bool Pose { get; set; } = true;
        public bool Quality { get; set; }
        public bool Stable { get; set; }
        public bool Confident { get; set; }
        public bool Margin { get; set; }
        public bool Consistent { get; set; }
        public bool Still { get; set; }

        public bool AllOk =>
            Face && Pose && Quality && Stable && Confident && Margin && Consistent && Still;

        public string FirstFailure
        {
            get
            {
                if (!Face) return "face not positioned";
                if (!Pose) return "head pose";
                if (!Quality) return "image quality";
                if (!Stable) return "prediction not stable yet";
                if (!Confident) return "confidence below threshold";
                if (!Margin) return "margin over runner-up too small";
                if (!Consistent) return "per-frame predictions disagree";
                if (!Still) return "movement";
                return "";
            }
        }
    }

    /// <summary>Numbers saved with every capture so the trigger can be audited.</summary>
    public sealed class StabilityMetrics
    {
        public string DisplayClass { get; set; } = "";
        public float DisplayConfidence { get; set; }
        public float Margin { get; set; }
        public int FramesHeldStable { get; set; }
        public int StableFramesRequired { get; set; }
        public int WindowTicks { get; set; }
        public double ConsistencyRatio { get; set; }
        public double MotionMean { get; set; }
        public double MotionMax { get; set; }
        public double HoldMs { get; set; }
        public float EmaAlpha { get; set; }
        public float ConfidenceThreshold { get; set; }
        public float HysteresisMargin { get; set; }
        public string Trigger { get; set; } = "auto";
    }

    public sealed class ScanDecision
    {
        public ScanState State { get; set; }
        public string Message { get; set; } = "";
        /// <summary>0..1 progress of the current phase (stabilising count / hold timer).</summary>
        public double Progress { get; set; }
        /// <summary>True exactly once per capture: grab THIS tick's frame now.</summary>
        public bool CaptureNow { get; set; }
        public ScanGates Gates { get; set; } = new ScanGates();
        public StabilityMetrics Metrics { get; set; } = new StabilityMetrics();
        public double CooldownRemainingSeconds { get; set; }
    }

    public sealed class ScanStateMachine
    {
        private readonly object _lock = new object();

        // ---- tunables ------------------------------------------------------

        /// <summary>All gates must stay green this long before capture.</summary>
        public int HoldStillMs { get; set; } = 1200;

        /// <summary>Raw per-tick predictions examined for agreement.</summary>
        public int WindowTicks { get; set; } = 8;

        /// <summary>Share of the window whose RAW argmax equals the displayed class.</summary>
        public double MinConsistency { get; set; } = 0.75;

        /// <summary>Smoothed top-1 minus top-2 probability.</summary>
        public float MinMargin { get; set; } = 0.15f;

        /// <summary>Smoothed confidence of the displayed class.</summary>
        public float MinConfidence { get; set; } = 0.65f;

        /// <summary>Frame-to-frame motion above this breaks "still".</summary>
        public double MotionStillThreshold { get; set; } = 6.0;

        public double CooldownSeconds { get; set; } = 4.0;

        public bool AutoCaptureEnabled { get; set; } = true;

        // ---- state ---------------------------------------------------------

        private ScanState _state = ScanState.Idle;
        private DateTime _holdStart = DateTime.MinValue;
        private DateTime _cooldownUntil = DateTime.MinValue;

        private readonly Queue<int> _rawWindow = new Queue<int>();
        private readonly Queue<double> _motionWindow = new Queue<double>();

        // stabiliser parameters, copied in so they are saved with the capture
        public float EmaAlpha { get; set; } = 0.25f;
        public float HysteresisMargin { get; set; } = 0.10f;
        public int StableFramesRequired { get; set; } = 8;

        public ScanState State
        {
            get { lock (_lock) return _state; }
        }

        /// <summary>True while a capture is being analysed or shown: the fast loop idles.</summary>
        public bool IsBusy
        {
            get
            {
                lock (_lock)
                    return _state == ScanState.StableCapturing
                        || _state == ScanState.FinalAnalysis
                        || _state == ScanState.Results;
            }
        }

        // ---- lifecycle -----------------------------------------------------

        public void Start()
        {
            lock (_lock)
            {
                ResetWindows();
                _state = ScanState.Live;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                ResetWindows();
                _state = ScanState.Idle;
            }
        }

        public void BeginFinalAnalysis()
        {
            lock (_lock) { if (_state == ScanState.StableCapturing) _state = ScanState.FinalAnalysis; }
        }

        public void CompleteAnalysis()
        {
            lock (_lock) { if (_state == ScanState.FinalAnalysis || _state == ScanState.StableCapturing) _state = ScanState.Results; }
        }

        /// <summary>User pressed "New Scan": cooldown, then back to Live.</summary>
        public void NewScan(DateTime nowUtc)
        {
            lock (_lock)
            {
                ResetWindows();
                _cooldownUntil = nowUtc.AddSeconds(CooldownSeconds);
                _state = ScanState.Cooldown;
            }
        }

        /// <summary>Manual Snap: capture the current frame regardless of the gates.</summary>
        public ScanDecision ForceCapture(StabilizedResult stable, DateTime nowUtc)
        {
            lock (_lock)
            {
                _state = ScanState.StableCapturing;
                var d = new ScanDecision
                {
                    State = _state,
                    Message = "Capturing…",
                    Progress = 1.0,
                    CaptureNow = true
                };
                d.Metrics = BuildMetrics(stable, "manual", 0.0);
                return d;
            }
        }

        // ---- the tick ------------------------------------------------------

        public ScanDecision Update(ScanInputs inp)
        {
            lock (_lock)
            {
                var d = new ScanDecision { State = _state };

                // Terminal / waiting states: nothing to evaluate.
                if (_state == ScanState.Idle)
                {
                    d.Message = "Camera off";
                    return d;
                }
                if (_state == ScanState.StableCapturing)
                {
                    d.Message = "Stable — Capturing";
                    d.Progress = 1.0;
                    return d;
                }
                if (_state == ScanState.FinalAnalysis)
                {
                    d.Message = "Final analysis…";
                    return d;
                }
                if (_state == ScanState.Results)
                {
                    d.Message = "Results";
                    return d;
                }

                if (_state == ScanState.Cooldown)
                {
                    double left = (_cooldownUntil - inp.NowUtc).TotalSeconds;
                    if (left > 0)
                    {
                        d.CooldownRemainingSeconds = left;
                        d.Message = "Ready for next scan in " + Math.Ceiling(left).ToString("0") + "s";
                        return d;
                    }
                    _state = ScanState.Live;
                    d.State = _state;
                }

                // ---- history windows ---------------------------------------
                _rawWindow.Enqueue(inp.RawPredictedIndex);
                while (_rawWindow.Count > WindowTicks) _rawWindow.Dequeue();

                _motionWindow.Enqueue(inp.Motion);
                while (_motionWindow.Count > WindowTicks) _motionWindow.Dequeue();

                StabilizedResult st = inp.Stable;
                bool faceOk = !inp.RequireFace || (inp.FaceFound && inp.FaceReady);
                bool faceSeen = !inp.RequireFace || inp.FaceFound;

                // ---- gates -------------------------------------------------
                var g = new ScanGates
                {
                    Face = faceOk,
                    Pose = inp.PoseOk,
                    Quality = inp.QualityOk,
                    Stable = st != null && st.IsStable && !st.IsUncertain && !string.IsNullOrEmpty(st.DisplayClass),
                    Still = inp.Motion <= MotionStillThreshold
                };

                int displayIdx = st != null ? Array.IndexOf(PredictionResult.ClassNames, st.DisplayClass) : -1;

                g.Confident = st != null && displayIdx >= 0 && st.DisplayConfidence >= MinConfidence;
                g.Margin = st != null && displayIdx >= 0 && Margin(st) >= MinMargin;
                g.Consistent = displayIdx >= 0 && Consistency(displayIdx) >= MinConsistency
                               && _rawWindow.Count >= WindowTicks;

                d.Gates = g;

                // ---- state selection ---------------------------------------
                if (!faceSeen)
                {
                    _state = ScanState.Live;
                    _holdStart = DateTime.MinValue;
                    d.Message = "Position your face in the oval";
                }
                else if (!inp.PoseOk)
                {
                    // Multi-view: wrong head pose for the requested view. Never capture, restart the hold.
                    _state = ScanState.Scanning;
                    _holdStart = DateTime.MinValue;
                    d.Message = string.IsNullOrEmpty(inp.PoseMessage) ? "Adjust your head position" : inp.PoseMessage;
                }
                else if (st == null || st.IsUncertain)
                {
                    _state = ScanState.Scanning;
                    _holdStart = DateTime.MinValue;
                    d.Message = "Scanning…";
                }
                else if (!st.IsStable)
                {
                    _state = ScanState.Stabilizing;
                    _holdStart = DateTime.MinValue;
                    d.Progress = Clamp01((double)st.FramesHeldStable / Math.Max(1, StableFramesRequired));
                    d.Message = "Stabilizing… " + st.FramesHeldStable + "/" + StableFramesRequired;
                }
                else if (!g.AllOk)
                {
                    // Class is stable but a gate is red: keep waiting, restart the hold timer.
                    _state = ScanState.Stabilizing;
                    _holdStart = DateTime.MinValue;
                    d.Progress = 1.0;
                    d.Message = faceOk ? "Hold still… (" + g.FirstFailure + ")"
                                       : "Adjust position…";
                }
                else
                {
                    if (_state != ScanState.HoldStill || _holdStart == DateTime.MinValue)
                        _holdStart = inp.NowUtc;

                    _state = ScanState.HoldStill;

                    double heldMs = (inp.NowUtc - _holdStart).TotalMilliseconds;
                    d.Progress = Clamp01(heldMs / HoldStillMs);
                    double remaining = Math.Max(0.0, (HoldStillMs - heldMs) / 1000.0);
                    d.Message = "Hold still… " + remaining.ToString("0.0") + "s";

                    if (heldMs >= HoldStillMs && AutoCaptureEnabled)
                    {
                        d.Metrics = BuildMetrics(st, "auto", heldMs);
                        _state = ScanState.StableCapturing;
                        d.CaptureNow = true;
                        d.Message = "Stable — Capturing";
                        d.Progress = 1.0;
                    }
                }

                d.State = _state;
                if (!d.CaptureNow) d.Metrics = BuildMetrics(st, "auto", 0.0);
                return d;
            }
        }

        // ---- helpers -------------------------------------------------------

        private void ResetWindows()
        {
            _rawWindow.Clear();
            _motionWindow.Clear();
            _holdStart = DateTime.MinValue;
        }

        private static float Margin(StabilizedResult st)
        {
            float[] p = st.SmoothedProbabilities;
            if (p == null || p.Length < 2) return 0f;

            float top = float.MinValue, second = float.MinValue;
            foreach (float v in p)
            {
                if (v > top) { second = top; top = v; }
                else if (v > second) second = v;
            }
            return top - second;
        }

        private double Consistency(int displayIdx)
        {
            if (_rawWindow.Count == 0) return 0.0;
            int agree = 0;
            foreach (int v in _rawWindow) if (v == displayIdx) agree++;
            return (double)agree / _rawWindow.Count;
        }

        private StabilityMetrics BuildMetrics(StabilizedResult st, string trigger, double holdMs)
        {
            double mean = 0.0, max = 0.0;
            if (_motionWindow.Count > 0)
            {
                double sum = 0.0;
                foreach (double m in _motionWindow) { sum += m; if (m > max) max = m; }
                mean = sum / _motionWindow.Count;
            }

            int displayIdx = st != null ? Array.IndexOf(PredictionResult.ClassNames, st.DisplayClass) : -1;

            return new StabilityMetrics
            {
                DisplayClass = st != null ? st.DisplayClass : "",
                DisplayConfidence = st != null ? st.DisplayConfidence : 0f,
                Margin = st != null ? Margin(st) : 0f,
                FramesHeldStable = st != null ? st.FramesHeldStable : 0,
                StableFramesRequired = StableFramesRequired,
                WindowTicks = _rawWindow.Count,
                ConsistencyRatio = displayIdx >= 0 ? Consistency(displayIdx) : 0.0,
                MotionMean = mean,
                MotionMax = max,
                HoldMs = holdMs,
                EmaAlpha = EmaAlpha,
                ConfidenceThreshold = MinConfidence,
                HysteresisMargin = HysteresisMargin,
                Trigger = trigger
            };
        }

        private static double Clamp01(double v)
        {
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }
    }
}
