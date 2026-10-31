// ============================================================================
// DetectionDiagnostics.cs  -  namespace SkinDevApp.AI
//
// READ-ONLY analysis of a set of lesion boxes: which boxes overlap, and how.
// It never removes, merges or re-scores a box and changes no threshold; it only describes what the detector already returned,
// so that "two overlapping boxes" can be told apart as
//   Duplicate  - the same patch boxed twice (IoU above DuplicateIou): NMS should normally have removed one;
//   Nested     - a small box lying inside a larger one (the larger box covers most of the smaller): IoU is low, so IoU-based NMS keeps both;
//   Overlap    - partly overlapping, neither of the above.
// The two cut-offs below are diagnostic labels only. They are not detection thresholds.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.AI
{
    public enum BoxRelation { None, Overlap, Nested, Duplicate }

    public sealed class BoxPairReport
    {
        public string ClassName { get; set; }
        public int A { get; set; }                 // index into the analysed list
        public int B { get; set; }
        public double Iou { get; set; }
        /// <summary>Intersection area divided by the SMALLER box's area (1.0 = fully inside the other box).</summary>
        public double ContainmentOfSmaller { get; set; }
        public BoxRelation Relation { get; set; }
    }

    public static class DetectionDiagnostics
    {
        public const double DuplicateIou = 0.5;          // diagnostic label only
        public const double NestedContainment = 0.8;     // diagnostic label only

        public static double Iou(LesionBox a, LesionBox b)
        {
            double inter = Intersection(a, b);
            if (inter <= 0) return 0;
            double u = Area(a) + Area(b) - inter;
            return u > 0 ? inter / u : 0;
        }

        public static double ContainmentOfSmaller(LesionBox a, LesionBox b)
        {
            double inter = Intersection(a, b);
            double small = Math.Min(Area(a), Area(b));
            return inter > 0 && small > 0 ? inter / small : 0;
        }

        public static BoxRelation Classify(double iou, double containment)
        {
            if (iou > DuplicateIou) return BoxRelation.Duplicate;
            if (containment >= NestedContainment) return BoxRelation.Nested;
            return iou > 0 ? BoxRelation.Overlap : BoxRelation.None;
        }

        /// <summary>Every same-class pair that overlaps at all. Boxes of different classes are not compared (the detector's NMS is per class too).</summary>
        public static List<BoxPairReport> Analyse(IList<LesionBox> boxes)
        {
            var pairs = new List<BoxPairReport>();
            if (boxes == null) return pairs;
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (boxes[i].ClassIndex != boxes[j].ClassIndex) continue;
                    double iou = Iou(boxes[i], boxes[j]);
                    double con = ContainmentOfSmaller(boxes[i], boxes[j]);
                    BoxRelation rel = Classify(iou, con);
                    if (rel == BoxRelation.None) continue;
                    pairs.Add(new BoxPairReport { ClassName = boxes[i].ClassName, A = i, B = j, Iou = iou, ContainmentOfSmaller = con, Relation = rel });
                }
            return pairs;
        }

        private static double Area(LesionBox b) => Math.Max(0, b.X1 - b.X0) * Math.Max(0, b.Y1 - b.Y0);

        private static double Intersection(LesionBox a, LesionBox b)
        {
            double iw = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
            double ih = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
            return iw > 0 && ih > 0 ? iw * ih : 0;
        }
    }
}
