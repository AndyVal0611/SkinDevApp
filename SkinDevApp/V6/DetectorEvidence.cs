// ============================================================================
// DetectorEvidence.cs  -  namespace SkinDevApp.V6
//
// Builds the 21-number detector evidence vector `det_ev` that the trained global_yolo fusion expects. Mirrors detector_evidence() in
// v6/astra_training_src/08_fusion.cellsrc and the box bookkeeping in 06_yolo_three_detectors.cellsrc (see V6_EVIDENCE_SCHEMA_SPEC.md, section 4).
//
// Per condition (Acne, Hyperpigmentation, Eczema), 7 numbers:
//   [0] available          1 if the detector ran, 0 if it is missing or failed
//   [1] detected           1 if at least one box is deployed (confidence >= the deployed threshold)
//   [2] max_deployed_conf  confidence of the best deployed box, else 0
//   [3] max_pool_conf      highest confidence in the whole candidate pool (deployed or not), else 0
//   [4] sqrt_rel_area      sqrt(best deployed box area / frame area), else 0
//   [5] cx                 best deployed box centre x / frame width, else 0
//   [6] cy                 best deployed box centre y / frame height, else 0
//
// A missing detector gives seven zeros INCLUDING available = 0. It is never encoded as "no detection", and absence of boxes is never
// treated as evidence that a condition is absent. No box counts are used.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.V6
{
    public static class DetectorEvidence
    {
        public const int PerCondition = 7;
        public const int Total = V6Conditions.Count * PerCondition;

        /// <summary>pools must be in palette order: Acne, Hyperpigmentation, Eczema.</summary>
        public static float[] Build(IReadOnlyList<ConditionPool> pools)
        {
            if (pools == null || pools.Count != V6Conditions.Count)
                throw new ArgumentException("exactly one pool per condition is required (Acne, Hyperpigmentation, Eczema)");
            var ev = new float[Total];
            for (int j = 0; j < V6Conditions.Count; j++)
            {
                ConditionPool p = pools[j];
                if (p == null || !p.Available) continue;                       // seven zeros, available = 0
                int o = j * PerCondition;
                ev[o] = 1f;
                List<EvidenceBox> dep = p.Boxes.Where(b => b.Deployed).ToList();
                double poolMax = p.Boxes.Count == 0 ? 0.0 : p.Boxes.Max(b => b.Conf);
                ev[o + 1] = dep.Count > 0 ? 1f : 0f;
                ev[o + 3] = (float)poolMax;
                if (dep.Count == 0) continue;
                EvidenceBox best = dep.OrderByDescending(b => b.Conf).First();
                ev[o + 2] = (float)best.Conf;
                ev[o + 4] = (float)Math.Sqrt(best.RelArea);
                ev[o + 5] = (float)best.Cx;
                ev[o + 6] = (float)best.Cy;
            }
            return ev;
        }

        /// <summary>
        /// Converts the candidate pool of one condition from frame pixels to evidence values. The caller must pass UNFILTERED detections:
        /// training evidence was computed without the face filter (a face filter may only affect what is drawn).
        /// </summary>
        public static ConditionPool FromPixelBoxes(string condition, bool available, IEnumerable<PixelBox> boxes, int frameW, int frameH,
                                                   double deployedThreshold, double poolMinConf, int maxBoxes, string error = null)
        {
            if (frameW <= 0 || frameH <= 0) throw new ArgumentException("frame size must be positive");
            var pool = new ConditionPool { Condition = condition, Available = available, DeployedThreshold = deployedThreshold, Error = error };
            if (!available || boxes == null) return pool;
            foreach (PixelBox b in boxes.Where(x => x.Conf >= poolMinConf).OrderByDescending(x => x.Conf).Take(maxBoxes))
            {
                pool.PixelBoxes.Add(b);
                pool.Boxes.Add(new EvidenceBox
                {
                    Conf = RoundHalfEven(Math.Min(1.0, b.Conf), 5),
                    Deployed = b.Conf >= deployedThreshold,                                           // decided on the unrounded confidence, as in training
                    RelArea = RoundHalfEven(((b.X1 - b.X0) * (b.Y1 - b.Y0)) / ((double)frameW * frameH), 6),
                    Cx = RoundHalfEven((b.X0 + b.X1) / 2.0 / frameW, 4),
                    Cy = RoundHalfEven((b.Y0 + b.Y1) / 2.0 / frameH, 4)
                });
            }
            return pool;
        }

        public static double RoundHalfEven(double v, int digits) => Math.Round(v, digits, MidpointRounding.ToEven);
    }
}
