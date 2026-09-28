using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.AI
{
    /// <summary>
    /// Result of a single ONNX Runtime classification.
    /// Class order is fixed by the trained model and must never be reordered:
    ///   0 = Acne, 1 = Hyperpigmentation, 2 = Eczema, 3 = Normal
    ///
    /// This order matches the four progress bars in ClientDashboardForm.xaml
    /// exactly (Acne, Hyperpigmentation, Eczema, Normal) - do not reorder
    /// either side without updating the other.
    /// </summary>
    public sealed class PredictionResult
    {
        public static readonly string[] ClassNames =
        {
            "Acne",
            "Hyperpigmentation",
            "Eczema",
            "Normal"
        };

        public int PredictedIndex { get; set; }

        public string PredictedClass =>
            PredictedIndex >= 0 && PredictedIndex < ClassNames.Length
                ? ClassNames[PredictedIndex]
                : "Unknown";

        /// <summary>Softmax probability of the predicted class, 0..1.</summary>
        public float Confidence { get; set; }

        /// <summary>Full softmax vector, index-aligned with <see cref="ClassNames"/>.</summary>
        public float[] Probabilities { get; set; } = Array.Empty<float>();

        /// <summary>Pure model inference time, excluding camera capture and UI.</summary>
        public double InferenceMs { get; set; }

        public IEnumerable<KeyValuePair<string, float>> AsPairs() =>
            ClassNames
                .Select((name, i) => new KeyValuePair<string, float>(
                    name,
                    i < Probabilities.Length ? Probabilities[i] : 0f));

        public string ConfidenceText => $"{Confidence * 100f:0.0}%";

        public override string ToString() =>
            $"{PredictedClass} ({ConfidenceText}) in {InferenceMs:0.0} ms";
    }
}