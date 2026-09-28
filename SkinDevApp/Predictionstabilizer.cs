// ============================================================================
// PredictionStabilizer.cs
// Add this file to your Visual Studio project under the SkinDevApp.AI folder.
//
// WHAT THIS DOES
// --------------
// Receives raw per-tick ONNX probability arrays from the live loop and
// returns a single stable display state.  Prevents the UI from flipping
// every second by combining three mechanisms:
//
//   1. EMA smoothing   - blends each new frame with the running average
//   2. Hysteresis gate - a new class must beat the current one by a margin
//   3. Stable-frames   - that margin must hold for N consecutive ticks
//
// Thread-safe: every public method can be called from any thread.
// ============================================================================

using System;

namespace SkinDevApp.AI
{
    /// <summary>
    /// The display state returned after each stabilizer update.
    /// </summary>
    public sealed class StabilizedResult
    {
        /// <summary>
        /// The class currently shown in the UI.  Empty string until the first
        /// stable prediction is reached.
        /// </summary>
        public string DisplayClass { get; init; } = "";

        /// <summary>Smoothed confidence of the DisplayClass, 0-1.</summary>
        public float DisplayConfidence { get; init; }

        /// <summary>
        /// EMA-smoothed probability for every class, aligned with
        /// PredictionResult.ClassNames.
        /// </summary>
        public float[] SmoothedProbabilities { get; init; } = Array.Empty<float>();

        /// <summary>
        /// True once the display class has been held stable for the required
        /// number of consecutive ticks.
        /// </summary>
        public bool IsStable { get; init; }

        /// <summary>
        /// True when the smoothed winner is below the confidence threshold.
        /// The UI should show an "Analyzing…" message instead of a diagnosis.
        /// </summary>
        public bool IsUncertain { get; init; }

        /// <summary>Short human-readable status line for the UI.</summary>
        public string StatusText { get; init; } = "";

        /// <summary>How many consecutive ticks the current pending class has won.</summary>
        public int FramesHeldStable { get; init; }
    }

    /// <summary>
    /// EMA + hysteresis + stable-frame-gate stabilizer for live ONNX predictions.
    /// </summary>
    public sealed class PredictionStabilizer
    {
        // ── Tuneable parameters ───────────────────────────────────────────────

        /// <summary>
        /// EMA weight for the newest frame (0..1).
        /// Lower = smoother but slower to react.
        /// 0.25 = effective window of ~4 ticks at a 1 s refresh rate.
        /// Raise to 0.35 if the display feels too slow to respond.
        /// </summary>
        public float EmaAlpha { get; set; } = 0.25f;

        /// <summary>
        /// Minimum smoothed confidence before a class is shown.
        /// Below this the UI displays "Analyzing… low confidence."
        /// 0.65 is conservative but matches the Eczema precision floor.
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.65f;

        /// <summary>
        /// A new class must exceed the currently displayed class's smoothed
        /// probability by at least this margin before the pending counter
        /// starts.  Stops Acne/Eczema flip-flopping at 55%/45%.
        /// </summary>
        public float HysteresisMargin { get; set; } = 0.10f;

        /// <summary>
        /// How many consecutive ticks the pending class must remain dominant
        /// before the display switches.  At 1 s/tick, 4 = 4 seconds.
        /// </summary>
        public int StableFramesRequired { get; set; } = 4;

        // ── Private state ─────────────────────────────────────────────────────

        private readonly object _lock = new object();
        private readonly string[] _classNames;

        private float[] _smoothed;       // EMA running average
        private string _displayClass;   // what is currently shown in the UI
        private string _pendingClass;   // candidate challenger
        private int _stableCounter;  // consecutive ticks _pendingClass has won

        // ── Constructor ───────────────────────────────────────────────────────

        /// <param name="classNames">
        /// Must match PredictionResult.ClassNames order:
        /// ["Acne", "Hyperpigmentation", "Eczema", "Normal"]
        /// </param>
        public PredictionStabilizer(string[] classNames)
        {
            _classNames = classNames ?? throw new ArgumentNullException(nameof(classNames));
            _smoothed = new float[classNames.Length];
            _displayClass = "";
            _pendingClass = "";
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Feed a new raw probability array from one ONNX tick.
        /// Returns the current stable display state.
        /// </summary>
        /// <param name="rawProbabilities">
        /// Softmax output from PredictionResult.Probabilities.
        /// Length must equal ClassNames.Length.
        /// </param>
        public StabilizedResult Update(float[] rawProbabilities)
        {
            if (rawProbabilities == null || rawProbabilities.Length != _classNames.Length)
                throw new ArgumentException(
                    $"Expected {_classNames.Length} probabilities, got " +
                    $"{rawProbabilities?.Length ?? 0}.");

            lock (_lock)
            {
                // ── Step 1: EMA smoothing ─────────────────────────────────────
                for (int i = 0; i < _smoothed.Length; i++)
                    _smoothed[i] = EmaAlpha * rawProbabilities[i]
                                 + (1f - EmaAlpha) * _smoothed[i];

                // ── Step 2: Find smoothed winner ──────────────────────────────
                int bestIdx = 0;
                for (int i = 1; i < _smoothed.Length; i++)
                    if (_smoothed[i] > _smoothed[bestIdx]) bestIdx = i;

                string bestClass = _classNames[bestIdx];
                float bestConf = _smoothed[bestIdx];

                // ── Step 3: Low-confidence hold ───────────────────────────────
                // Below the threshold the model is guessing; show "Analyzing…"
                // and reset the pending counter so no stale class gets promoted.
                if (bestConf < ConfidenceThreshold)
                {
                    _stableCounter = 0;
                    _pendingClass = "";
                    return BuildResult(isUncertain: true);
                }

                // ── Step 4: Hysteresis gate ───────────────────────────────────
                // Only start the stable-frames counter if the challenger beats
                // the current display class by the required margin.
                bool sameAsDisplay = bestClass == _displayClass;

                if (!sameAsDisplay)
                {
                    int displayIdx = Array.IndexOf(_classNames, _displayClass);
                    float displayConf = displayIdx >= 0 ? _smoothed[displayIdx] : 0f;

                    bool marginMet = (bestConf - displayConf) >= HysteresisMargin;

                    if (!marginMet)
                    {
                        // Marginal win — keep the current display, don't count.
                        _stableCounter = 0;
                        _pendingClass = "";
                        return BuildResult(isUncertain: false);
                    }
                }

                // ── Step 5: Stable-frames gate ────────────────────────────────
                if (sameAsDisplay || bestClass == _pendingClass)
                {
                    _stableCounter++;
                }
                else
                {
                    // New challenger — restart the counter.
                    _pendingClass = bestClass;
                    _stableCounter = 1;
                }

                // Promote the challenger once it has held for enough ticks.
                if (_stableCounter >= StableFramesRequired && !sameAsDisplay)
                    _displayClass = bestClass;

                return BuildResult(isUncertain: false);
            }
        }

        /// <summary>Reset all running state (e.g. when the camera restarts).</summary>
        public void Reset()
        {
            lock (_lock)
            {
                Array.Clear(_smoothed, 0, _smoothed.Length);
                _displayClass = "";
                _pendingClass = "";
                _stableCounter = 0;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        // Called inside the lock; captures a snapshot of _smoothed before returning.
        private StabilizedResult BuildResult(bool isUncertain)
        {
            float[] snapshot = (float[])_smoothed.Clone();

            int displayIdx = Array.IndexOf(_classNames, _displayClass);
            float displayConf = displayIdx >= 0 ? snapshot[displayIdx] : 0f;

            string statusText;
            if (isUncertain)
                statusText = "Analyzing\u2026 low confidence";
            else if (_displayClass == "")
                statusText = "Confirming\u2026";
            else if (_stableCounter < StableFramesRequired)
                statusText = $"Confirming {_pendingClass}\u2026 ({_stableCounter}/{StableFramesRequired})";
            else
                statusText = _displayClass;

            return new StabilizedResult
            {
                DisplayClass = _displayClass,
                DisplayConfidence = displayConf,
                SmoothedProbabilities = snapshot,
                IsStable = !isUncertain && _stableCounter >= StableFramesRequired,
                IsUncertain = isUncertain,
                StatusText = statusText,
                FramesHeldStable = _stableCounter,
            };
        }
    }
}