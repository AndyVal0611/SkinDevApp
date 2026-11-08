// ============================================================================
// V6Heatmaps.cs  -  namespace SkinDevApp.V6
//
// V6's heat maps, in the same form as V5's (one colour-tinted map per condition on the analysed photograph, the same palette, opacity and gamma), but built
// from what V6 actually uses as localised evidence: the lesion-detector candidate pool that feeds the fusion.
//
//   strength at a pixel = the highest detector confidence among that condition's boxes covering it (a soft bump that peaks at the box centre)
//   colour              = the condition (acne red, hyperpigmentation blue, eczema orange)
//
// It is an EVIDENCE map: where the detectors found something and how confident they were. It is NOT an attribution of the fusion model (a Grad-CAM of the
// fusion was tried: the maps of different conditions were 95-98 % identical, so they said nothing about where). Boxes whose centre lies outside the face
// region (+25 %) are not drawn (hair, background): display only, never the assessment. Pure OpenCV; no WPF.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.V6
{
    public static class V6Heatmaps
    {
        public const string OverlayPrefix = "v6_overlay_";      // v6_overlay_<Condition>.png : photograph tinted by that condition's evidence
        public const string HeatPrefix = "v6_heatmap_";         // v6_heatmap_<Condition>.png : the evidence map alone, 8-bit
        public const string AllFile = "v6_overlay_all.png";     // the three conditions together, strongest on top
        public const string CombinedPrefix = "v6_combined_";    // v6_combined_<Condition>.png : that condition's overlay with the marked boxes on top

        public static string OverlayPath(string folder, string condition) => string.IsNullOrEmpty(folder) ? null : ExistingOrNull(Path.Combine(folder, OverlayPrefix + condition + ".png"));
        public static string CombinedPath(string folder, string condition) => string.IsNullOrEmpty(folder) ? null : ExistingOrNull(Path.Combine(folder, CombinedPrefix + condition + ".png"));
        public static string AllPath(string folder) => string.IsNullOrEmpty(folder) ? null : ExistingOrNull(Path.Combine(folder, AllFile));
        private static string ExistingOrNull(string p) => File.Exists(p) ? p : null;

        /// <summary>The condition with a "present" verdict and the highest AI support, else simply the highest AI support.</summary>
        public static string LeadCondition(UnifiedAssessment a)
        {
            if (a == null || a.Conditions.Length == 0) return null;
            var present = a.Conditions.Where(c => c.Status == ConditionStatus.Present).ToList();
            return (present.Count > 0 ? present : a.Conditions.ToList()).OrderByDescending(c => c.CalibratedProbability).First().Condition;
        }

        /// <summary>Highest detector confidence among the condition's candidate boxes drawn on the map, or 0 when none.</summary>
        public static double MaxConfidence(UnifiedAssessment a, string condition, Size frame, Rect? face)
            => BoxesFor(a, condition, face).Select(b => (double)b.Confidence).DefaultIfEmpty(0).Max();

        private static System.Collections.Generic.IEnumerable<V6Box> BoxesFor(UnifiedAssessment a, string condition, Rect? face)
        {
            foreach (V6Box b in (a.Pool.Count > 0 ? a.Pool : a.Boxes).Where(x => x.Condition == condition))
            {
                double cx = (b.X0 + b.X1) / 2.0, cy = (b.Y0 + b.Y1) / 2.0;
                if (face.HasValue && face.Value.Width > 0 && face.Value.Height > 0)
                {
                    Rect f = face.Value; const double ex = 0.25;
                    if (cx < f.X - ex * f.Width || cx > f.X + f.Width + ex * f.Width || cy < f.Y - ex * f.Height || cy > f.Y + f.Height + ex * f.Height) continue;
                }
                yield return b;
            }
        }

        /// <summary>Evidence map of one condition: CV_32F, frame-sized, values 0..1 (detector confidence).</summary>
        public static Mat Heat(UnifiedAssessment a, string condition, Size frame, Rect? face)
        {
            var heat = new Mat(frame.Height, frame.Width, MatType.CV_32FC1, Scalar.All(0));
            foreach (V6Box b in BoxesFor(a, condition, face))
            {
                double cx = (b.X0 + b.X1) / 2.0, cy = (b.Y0 + b.Y1) / 2.0, sx = Math.Max(4.0, (b.X1 - b.X0) / 3.0), sy = Math.Max(4.0, (b.Y1 - b.Y0) / 3.0);
                int x0 = Math.Max(0, (int)Math.Floor(cx - 2.5 * sx)), x1 = Math.Min(frame.Width - 1, (int)Math.Ceiling(cx + 2.5 * sx));
                int y0 = Math.Max(0, (int)Math.Floor(cy - 2.5 * sy)), y1 = Math.Min(frame.Height - 1, (int)Math.Ceiling(cy + 2.5 * sy));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        double dx = (x - cx) / sx, dy = (y - cy) / sy;
                        float v = (float)(b.Confidence * Math.Exp(-0.5 * (dx * dx + dy * dy)));
                        if (v > heat.At<float>(y, x)) heat.Set(y, x, v);               // strongest box wins; overlapping boxes never add up
                    }
            }
            return heat;
        }

        /// <summary>Detector confidence at or above this is drawn at full strength (detector boxes rarely exceed 0.7, so without this gain the tint would be too faint to see).</summary>
        public const double FullStrengthConfidence = 0.70;

        /// <summary>Opacity map of an evidence map: strength^gamma * peak opacity, strength = detector confidence / 0.70 capped at 1. CV_32F, same size as heat.</summary>
        public static Mat Alpha(Mat heat)
        {
            var alpha = new Mat();
            using (Mat strength = new Mat())
            {
                Cv2.Multiply(heat, Scalar.All(1.0 / FullStrengthConfidence), strength);
                Cv2.Threshold(strength, strength, 1.0, 1.0, ThresholdTypes.Trunc);
                Cv2.Pow(strength, ClassPalette.Gamma, alpha);
            }
            Cv2.Multiply(alpha, Scalar.All(ClassPalette.AlphaMax), alpha);
            return alpha;
        }

        /// <summary>Blends colour into a BGR frame (or region) with a per-pixel opacity map of the same size: out = frame*(1-a) + colour*a.</summary>
        public static Mat Blend(Mat frameBgr, Mat alpha, int conditionIndex)
        {
            Scalar color = ClassPalette.ColorsBgr[conditionIndex];
            using (Mat frame32 = new Mat())
            {
                frameBgr.ConvertTo(frame32, MatType.CV_32FC3);
                Mat[] ch = Cv2.Split(frame32);
                try
                {
                    for (int c = 0; c < 3; c++)
                        using (Mat keep = new Mat()) using (Mat tint = new Mat())
                        {
                            Cv2.Subtract(Scalar.All(1.0), alpha, keep);
                            Cv2.Multiply(ch[c], keep, ch[c]);
                            Cv2.Multiply(alpha, Scalar.All(color[c]), tint);
                            Cv2.Add(ch[c], tint, ch[c]);
                        }
                    using (Mat merged = new Mat()) { Cv2.Merge(ch, merged); var result = new Mat(); merged.ConvertTo(result, MatType.CV_8UC3); return result; }
                }
                finally { foreach (Mat m in ch) m.Dispose(); }
            }
        }

        /// <summary>Same blending rule as V5's overlays: opacity = strength^gamma * peak opacity, color = the condition; strength = detector confidence / 0.70, capped at 1.</summary>
        public static Mat Overlay(Mat frameBgr, Mat heat, int conditionIndex)
        {
            using (Mat alpha = Alpha(heat)) return Blend(frameBgr, alpha, conditionIndex);
        }

        /// <summary>Writes the per-condition heat maps, overlays, the three-condition overlay and the combined (lead overlay + boxes) picture into the capture folder.</summary>
        public static void WriteAll(string folder, UnifiedAssessment a, Mat frameBgr, Rect? face)
        {
            if (string.IsNullOrEmpty(folder) || frameBgr == null || frameBgr.Empty()) return;
            Size size = frameBgr.Size();
            Mat all = frameBgr.Clone();
            try
            {
                for (int i = 0; i < V6Conditions.Count; i++)
                {
                    string c = V6Conditions.Names[i];
                    using (Mat heat = Heat(a, c, size, face))
                    {
                        using (Mat h8 = new Mat()) { heat.ConvertTo(h8, MatType.CV_8UC1, 255.0); File.WriteAllBytes(Path.Combine(folder, HeatPrefix + c + ".png"), h8.ImEncode(".png")); }
                        using (Mat ov = Overlay(frameBgr, heat, i))
                        {
                            File.WriteAllBytes(Path.Combine(folder, OverlayPrefix + c + ".png"), ov.ImEncode(".png"));
                            using (Mat both = V6Archive.DrawMarkedAreas(ov, a, face)) File.WriteAllBytes(Path.Combine(folder, CombinedPrefix + c + ".png"), both.ImEncode(".png"));
                        }
                        Mat next = Overlay(all, heat, i); all.Dispose(); all = next;                                 // the three conditions together, in palette order
                    }
                }
                File.WriteAllBytes(Path.Combine(folder, AllFile), all.ImEncode(".png"));
            }
            finally { all.Dispose(); }
        }
    }
}
