// ============================================================================
// V6Roi.cs  -  namespace SkinDevApp.V6
//
// The YOLO-guided region branch of the full V6 fusion. A port of build_roi_candidates(), crop_roi(), square_crop_box(), yolo_roi_features() and
// roi_sets_for() in v6/astra_training_src/07_roi_branch.cellsrc: the detector boxes pick up to 12 regions (tier 1 deployed boxes round-robin over
// the detectors, tier 2 lower-confidence candidates, tier 3 wider-context copies of the first tier-1 regions), each region is cropped square with
// context and read by the ROI MobileNetV2, and its 64-d embedding + 4 scores + 15 geometry/evidence numbers form one 83-number row.
// An image with no regions gives an all-false mask, never a zero crop.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace SkinDevApp.V6
{
    public sealed class RoiCandidate
    {
        public int Tier;                       // 1, 2 or 3
        public bool Alt;                       // tier 3: same box, wider context window
        public int Condition;                  // palette index
        public double Conf, X0, Y0, X1, Y1, RelArea, Cx, Cy, WRel, HRel, Aspect, SupportFrac;
        public bool Deployed;
    }

    public static class V6Roi
    {
        public const int EmbDim = 64, ClassCount = 4, GeoDim = 15;

        private sealed class Row
        {
            public int Condition; public double Conf, X0, Y0, X1, Y1, RelArea, Cx, Cy; public bool Deployed; public int Support, Size;
        }

        private static double Iou(Row a, Row b)
        {
            double iw = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), ih = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
            if (iw <= 0 || ih <= 0) return 0.0;
            double i = iw * ih, u = (a.X1 - a.X0) * (a.Y1 - a.Y0) + (b.X1 - b.X0) * (b.Y1 - b.Y0) - i;
            return u > 0 ? i / u : 0.0;
        }

        /// <summary>Candidates for one frame from the (unfiltered) detector pools, in the order the trained model saw them.</summary>
        public static List<RoiCandidate> BuildCandidates(IReadOnlyList<ConditionPool> pools, int W, int H, RoiSettings s)
        {
            var rows = new List<Row>();
            for (int j = 0; j < pools.Count; j++)
            {
                ConditionPool p = pools[j];
                if (p == null || !p.Available || p.PixelBoxes.Count != p.Boxes.Count) continue;        // pixel boxes are needed for the geometry
                for (int k = 0; k < p.Boxes.Count; k++)
                {
                    EvidenceBox e = p.Boxes[k]; PixelBox b = p.PixelBoxes[k];
                    rows.Add(new Row
                    {
                        Condition = j, Conf = e.Conf, Deployed = e.Deployed, RelArea = e.RelArea, Cx = e.Cx, Cy = e.Cy, Support = b.Support, Size = b.EnsembleSize,
                        X0 = DetectorEvidence.RoundHalfEven(b.X0, 2), Y0 = DetectorEvidence.RoundHalfEven(b.Y0, 2), X1 = DetectorEvidence.RoundHalfEven(b.X1, 2), Y1 = DetectorEvidence.RoundHalfEven(b.Y1, 2)
                    });
                }
            }
            List<Row> cand = rows.OrderByDescending(r => r.Conf).ToList();                              // stable, like Python sorted()
            var chosen = new List<Row>();
            bool Dup(Row r) => chosen.Any(c => Iou(r, c) > s.OverlapSuppressIou);

            var per = Enumerable.Range(0, V6Conditions.Count).Select(c => new Queue<Row>(cand.Where(r => r.Deployed && r.Condition == c))).ToArray();
            var t1 = new List<Row>();
            while (t1.Count < s.MaxTier1 && per.Any(q => q.Count > 0))
                for (int c = 0; c < per.Length; c++)
                    if (per[c].Count > 0 && t1.Count < s.MaxTier1)
                    {
                        Row r = per[c].Dequeue();
                        if (!Dup(r)) { chosen.Add(r); t1.Add(r); }
                    }
            int nAlt = Math.Min(t1.Count, Math.Max(0, s.MaxTotal / 4));
            var t2 = new List<Row>();
            foreach (Row r in cand)
            {
                if (t1.Count + t2.Count + nAlt >= s.MaxTotal) break;
                if (!r.Deployed && !Dup(r)) { chosen.Add(r); t2.Add(r); }
            }
            var outList = new List<RoiCandidate>();
            var groups = new List<Tuple<int, List<Row>, bool>> { Tuple.Create(1, t1, false), Tuple.Create(2, t2, false), Tuple.Create(3, t1.Take(nAlt).ToList(), true) };
            foreach (var g in groups)
                foreach (Row r in g.Item2)
                {
                    double wRel = (r.X1 - r.X0) / W, hRel = (r.Y1 - r.Y0) / H;
                    outList.Add(new RoiCandidate
                    {
                        Tier = g.Item1, Alt = g.Item3, Condition = r.Condition, Conf = r.Conf, Deployed = r.Deployed, X0 = r.X0, Y0 = r.Y0, X1 = r.X1, Y1 = r.Y1,
                        RelArea = r.RelArea, Cx = r.Cx, Cy = r.Cy, WRel = wRel, HRel = hRel, SupportFrac = (double)r.Support / Math.Max(r.Size, 1),
                        Aspect = Math.Min(10.0, Math.Max(0.1, wRel * W / Math.Max(hRel * H, 1e-6)))
                    });
                }
            return outList;
        }

        /// <summary>The 15 geometry/evidence numbers of a region (yolo_roi_features).</summary>
        public static float[] GeometryFeatures(RoiCandidate c)
        {
            var f = new float[GeoDim];
            f[c.Condition] = 1f;
            f[3] = (float)c.Conf; f[4] = (float)c.RelArea; f[5] = (float)c.Cx; f[6] = (float)c.Cy; f[7] = (float)c.WRel; f[8] = (float)c.HRel;
            f[9] = (float)Math.Log(c.Aspect); f[10] = c.Deployed ? 1f : 0f; f[11] = (float)c.SupportFrac;
            f[12] = c.Tier == 1 ? 1f : 0f; f[13] = c.Tier == 2 ? 1f : 0f; f[14] = c.Tier == 3 ? 1f : 0f;
            return f;
        }

        /// <summary>Margin-expanded SQUARE window containing the box, shifted to lie inside the frame (square_crop_box).</summary>
        public static bool SquareCropBox(double x0, double y0, double x1, double y1, int W, int H, double margin, out int cx0, out int cy0, out int side)
        {
            cx0 = cy0 = side = 0;
            double sum = x0 + y0 + x1 + y1;
            if (double.IsNaN(sum) || double.IsInfinity(sum) || x1 - x0 < 2 || y1 - y0 < 2) return false;
            double sd = Math.Max(x1 - x0, y1 - y0) * (1 + 2 * margin);
            sd = Math.Min(sd, Math.Min(W, H));
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            double nx0 = Math.Min(Math.Max(cx - sd / 2, 0), W - sd), ny0 = Math.Min(Math.Max(cy - sd / 2, 0), H - sd);
            cx0 = (int)Math.Round(nx0, MidpointRounding.ToEven); cy0 = (int)Math.Round(ny0, MidpointRounding.ToEven);
            side = Math.Max((int)Math.Round(sd, MidpointRounding.ToEven), 2);
            return true;
        }

        /// <summary>The square crop of a candidate as a BGR Mat (caller disposes), or null when the region is too small, outside the frame or degenerate (crop_roi).</summary>
        public static Mat Crop(Mat bgr, RoiCandidate c, RoiSettings s)
        {
            int W = bgr.Width, H = bgr.Height;
            if (Math.Max(c.X1 - c.X0, c.Y1 - c.Y0) < s.MinSideFrac * Math.Min(W, H)) return null;
            if (c.X0 < -1 || c.Y0 < -1 || c.X1 > W + 1 || c.Y1 > H + 1) return null;
            if (!SquareCropBox(c.X0, c.Y0, c.X1, c.Y1, W, H, c.Alt ? s.AltMargin : s.Margin, out int x, out int y, out int side)) return null;
            Rect rect = new Rect(x, y, side, side).Intersect(new Rect(0, 0, W, H));            // numpy slicing truncates at the frame edge
            if (rect.Width < 2 || rect.Height < 2) return null;
            return new Mat(bgr, rect).Clone();
        }
    }
}
