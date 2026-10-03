// ============================================================================
// RegionAttribution.cs  (NEW)  -  namespace SkinDevApp.Explainability
//
// APPROXIMATE facial-region description of where one class heatmap is strong:
//     "Strongest model attribution: right cheek"
//
// What this is NOT:
//   * not lesion localisation or segmentation
//   * not evidence that a lesion exists there
// The face box (Haar) is only a REFERENCE FRAME for naming areas. The
// classifier never sees it. If attribution is spread out, NO region is
// forced: TopZone is null and Diffuse is true.
//
// Algorithm and constants are identical to region_attribution() in notebook
// Cell 32 (HOT_THRESHOLD, REGION_MIN_SHARE, REGION_MIN_LEAD, zone borders).
//
// Frames are NOT mirrored: the subject's RIGHT side appears on the image LEFT.
// If your camera driver mirrors the image, set MirroredCamera = true.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SkinDevApp.Explainability
{
    public sealed class RegionResult
    {
        /// <summary>Zone name, or null when attribution is diffuse.</summary>
        public string TopZone { get; set; }

        /// <summary>Share of "hot" attribution mass in the top zone (0..1).</summary>
        public double TopShare { get; set; }

        /// <summary>Share of hot mass that falls outside the face box (0..1).</summary>
        public double OutsideShare { get; set; }

        public bool Diffuse { get; set; } = true;

        public Dictionary<string, double> ZoneShare { get; set; } = new Dictionary<string, double>();

        public string Describe()
        {
            if (Diffuse || string.IsNullOrEmpty(TopZone))
                return "Attribution is diffuse — no single region stands out";
            return "Strongest model attribution: " + TopZone;
        }
    }

    public static class RegionAttribution
    {
        public const double HotThreshold = 0.30;
        public const double MinShare = 0.40;
        public const double MinLead = 0.08;

        /// <summary>Set true if the camera/driver delivers a mirrored image.</summary>
        public static bool MirroredCamera = false;

        public static readonly string[] Zones =
        {
            "forehead", "right cheek", "left cheek", "nose", "mouth/chin"
        };

        private static string Side(bool imageLeft)
        {
            // Un-mirrored: image-left == subject's right.
            bool subjectRight = MirroredCamera ? !imageLeft : imageLeft;
            return subjectRight ? "right cheek" : "left cheek";
        }

        /// <param name="heat8u">8-bit single-channel heatmap (0..255).</param>
        /// <param name="faceBox">Face box in HEATMAP pixel coordinates, or null
        /// to use the whole image as the reference frame.</param>
        public static RegionResult Compute(Mat heat8u, Rect? faceBox)
        {
            var result = new RegionResult();
            foreach (string z in Zones) result.ZoneShare[z] = 0.0;

            if (heat8u == null || heat8u.Empty() || heat8u.Channels() != 1)
                return result;

            using (Mat cont = heat8u.Clone())   // Clone() is always continuous
            {
                int w = cont.Width, h = cont.Height;
                byte[] data = new byte[w * h];
                Marshal.Copy(cont.Data, data, 0, data.Length);

                int bx = 0, by = 0, bw = w, bh = h;
                if (faceBox.HasValue)
                {
                    Rect b = faceBox.Value;
                    bx = Math.Max(0, b.X);
                    by = Math.Max(0, b.Y);
                    bw = Math.Min(b.Width, w - bx);
                    bh = Math.Min(b.Height, h - by);
                }

                if (bw < 4 || bh < 4) return result;

                double total = 0.0;
                double outside = 0.0;
                double[] zone = new double[Zones.Length];

                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        double heat = data[row + x] / 255.0;
                        double wgt = (heat - HotThreshold) / (1.0 - HotThreshold);
                        if (wgt <= 0.0) continue;
                        if (wgt > 1.0) wgt = 1.0;

                        total += wgt;

                        if (x < bx || x >= bx + bw || y < by || y >= by + bh)
                        {
                            outside += wgt;
                            continue;
                        }

                        double u = (x - bx + 0.5) / bw;
                        double v = (y - by + 0.5) / bh;

                        int zi;
                        if (v < 0.30) zi = 0;                       // forehead
                        else if (v < 0.72)
                        {
                            if (u < 0.30) zi = 1;                   // image-left cheek
                            else if (u > 0.70) zi = 2;              // image-right cheek
                            else zi = 3;                            // nose
                        }
                        else zi = 4;                                // mouth / chin

                        zone[zi] += wgt;
                    }
                }

                if (total < 1e-6) return result;

                result.OutsideShare = outside / total;

                // Zone names use the SUBJECT's own left/right.
                string[] names =
                {
                    "forehead", Side(true), Side(false), "nose", "mouth/chin"
                };

                int best = 0, second = -1;
                for (int i = 0; i < zone.Length; i++)
                {
                    result.ZoneShare[names[i]] = zone[i] / total;
                    if (zone[i] > zone[best]) best = i;
                }
                for (int i = 0; i < zone.Length; i++)
                {
                    if (i == best) continue;
                    if (second < 0 || zone[i] > zone[second]) second = i;
                }

                double topShare = zone[best] / total;
                double secondShare = second >= 0 ? zone[second] / total : 0.0;
                result.TopShare = topShare;

                bool focal = topShare >= MinShare
                          && (topShare - secondShare) >= MinLead
                          && result.OutsideShare < 0.5;

                result.Diffuse = !focal;
                result.TopZone = focal ? names[best] : null;
                return result;
            }
        }
    }
}
