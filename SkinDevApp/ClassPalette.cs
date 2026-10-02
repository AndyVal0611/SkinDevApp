// ============================================================================
// ClassPalette.cs  (NEW)  -  namespace SkinDevApp.Imaging
//
// Single source of truth for class colours and overlay intensity.
//
//   colour   = CLASS IDENTITY   (Acne red, Hyperpigmentation blue,
//                                Eczema yellow/orange, Normal green)
//   opacity  = ATTRIBUTION STRENGTH (heat^gamma * strength factor)
//
// The numbers below are IDENTICAL to CLASS_COLORS_BGR / OVERLAY_* in notebook
// Cell 32, so the kiosk and the thesis figures look the same.
// Class order is the model's: Acne, Hyperpigmentation, Eczema, Normal.
// ============================================================================

using OpenCvSharp;

namespace SkinDevApp.Imaging
{
    /// <summary>Which heatmap(s) the live preview / result view draws.</summary>
    public enum OverlayView
    {
        /// <summary>The currently displayed (stabilised) class only.</summary>
        Predicted = 0,
        /// <summary>All four class maps, strongest drawn on top.</summary>
        All = 1,
        Acne = 2,
        Hyperpigmentation = 3,
        Eczema = 4,
        Normal = 5
    }

    public static class ClassPalette
    {
        public const int ClassCount = 4;

        public static readonly string[] Names =
        {
            "Acne", "Hyperpigmentation", "Eczema", "Normal"
        };

        /// <summary>BGR colours for OpenCV blending.</summary>
        public static readonly Scalar[] ColorsBgr =
        {
            new Scalar(0,   0,   255),   // Acne               red
            new Scalar(255, 80,  0),     // Hyperpigmentation  blue
            new Scalar(0,   190, 255),   // Eczema             yellow / orange
            new Scalar(60,  200, 0)      // Normal             green
        };

        /// <summary>#RRGGBB colours for WPF brushes (same colours as above).</summary>
        public static readonly string[] Hex =
        {
            "#FF0000", "#0050FF", "#FFBE00", "#00C83C"
        };

        public static double AlphaMax { get; set; } = 0.60;   // peak opacity (Settings > overlay opacity)
        public const double Gamma = 1.25;           // >1 keeps weak attribution transparent
        public const double StrengthFloor = 0.35;   // opacity multiplier of the weakest class map

        /// <summary>
        /// relative_strength (0..1, from the service) -> opacity multiplier.
        /// Gives every class the SAME base intensity scale, so a weak class
        /// map is honestly fainter than a strong one.
        /// </summary>
        public static double StrengthFactor(double relativeStrength)
        {
            double r = relativeStrength < 0.0 ? 0.0 : (relativeStrength > 1.0 ? 1.0 : relativeStrength);
            return StrengthFloor + (1.0 - StrengthFloor) * r;
        }

        public static int IndexOf(string className)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], className, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>OverlayView -> class index for single-class views, else -1.</summary>
        public static int ViewToClassIndex(OverlayView view)
        {
            switch (view)
            {
                case OverlayView.Acne: return 0;
                case OverlayView.Hyperpigmentation: return 1;
                case OverlayView.Eczema: return 2;
                case OverlayView.Normal: return 3;
                default: return -1;
            }
        }
    }
}
