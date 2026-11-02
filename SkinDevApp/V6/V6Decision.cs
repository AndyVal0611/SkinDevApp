// ============================================================================
// V6Decision.cs  -  namespace SkinDevApp.V6
//
// Pure decision logic after the trained fusion model: Platt calibration, per-condition thresholds, review flags and the overall state.
// A port of decide() / stage1_flags() in v6/astra_training_src/09_stages_trust.cellsrc with Stage 2 (ROI) NOT included:
// where the notebook would escalate to Stage 2, this deployment returns UNCERTAIN_REVIEW_REQUIRED and still shows the fused leaning.
//
// The fused global_yolo probability is the decision. Nothing here averages, votes or overrides it. The Global-only fusion is used ONLY to raise
// a "cnn_yolo_conflict" review flag; it never changes a score.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.V6
{
    public sealed class DecisionResult
    {
        public string State;
        public ConditionStatus[] Status = new ConditionStatus[V6Conditions.Count];
        public bool[] AbsenceSupported = new bool[V6Conditions.Count];
        public List<string> Flags = new List<string>();
        public List<string> Reasons = new List<string>();
    }

    public static class V6Decision
    {
        public const double LogitEps = 1e-4;

        public static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

        /// <summary>Fusion input g_logit: ln(p/(1-p)) of the clipped sigmoid of the Global logit (z clipped to about +-9.21).</summary>
        public static float GlobalLogitInput(double z)
        {
            double p = Math.Min(Math.Max(Sigmoid(z), LogitEps), 1.0 - LogitEps);
            return (float)Math.Log(p / (1.0 - p));
        }

        public static double[] ApplyPlatt(IReadOnlyList<double> z, PlattParam[] cal)
        {
            var p = new double[z.Count];
            for (int j = 0; j < p.Length; j++) p[j] = Sigmoid(cal[j].A * z[j] + cal[j].B);
            return p;
        }

        /// <summary>Review flags (port of stage1_flags). avail/detected come from the evidence vector, never from the number of boxes alone.</summary>
        public static List<string> Flags(double[] pFusion, double[] thrFusion, double[] pGlobalOnly, double[] thrGlobal, bool[] avail, bool[] detected,
                                         double band, bool oodFlag, QualityInput quality)
        {
            var f = new List<string>();
            for (int j = 0; j < V6Conditions.Count; j++)
            {
                string c = V6Conditions.Names[j];
                if (avail[j] && ((pGlobalOnly[j] >= thrGlobal[j]) != detected[j])) f.Add("cnn_yolo_conflict_" + c);
                if (Math.Abs(pFusion[j] - thrFusion[j]) < band) f.Add("near_threshold_" + c);
            }
            if (oodFlag) f.Add("ood");
            if (quality != null)
            {
                foreach (string b in quality.Blocking) f.Add("quality_" + b);
                if (quality.Warnings.Count > 0) f.Add("quality_warning:" + string.Join(",", quality.Warnings));
            }
            return f;
        }

        /// <param name="error">non-null: inference failed. Never produces a Normal-looking result.</param>
        public static DecisionResult Decide(double[] pFusion, double[] thr, double band, bool[] avail, IReadOnlyList<string> flags,
                                            bool oodFlag, QualityInput quality, string error = null)
        {
            var r = new DecisionResult();
            for (int j = 0; j < V6Conditions.Count; j++) r.Status[j] = ConditionStatus.Uncertain;
            r.Flags.AddRange(flags ?? new string[0]);

            if (error != null) { r.State = V6States.InferenceError; r.Reasons.Add("error: " + error); return r; }
            if (quality != null && quality.Blocking.Count > 0)
            { r.State = V6States.RecaptureRequired; r.Reasons.Add("blocking quality: " + string.Join(",", quality.Blocking)); return r; }
            if (oodFlag)
            { r.State = V6States.OutOfScopeReviewRequired; r.Reasons.Add("input unlike the training distribution (OOD)"); return r; }
            if (double.IsNaN(band) || double.IsInfinity(band))
            { r.State = V6States.UncertainReviewRequired; r.Reasons.Add("no eligible calibrated reliability band; supported decisions disabled"); return r; }

            var predicted = new List<string>(); var uncertain = new List<string>();
            for (int j = 0; j < V6Conditions.Count; j++)
            {
                string c = V6Conditions.Names[j]; double p = pFusion[j], t = thr[j];
                r.Status[j] = p >= t + band ? ConditionStatus.Present : (p < t - band ? ConditionStatus.Absent : ConditionStatus.Uncertain);
                r.AbsenceSupported[j] = r.Status[j] == ConditionStatus.Absent && avail[j];      // absence needs a working detector for that condition
                if (r.Status[j] == ConditionStatus.Present) predicted.Add(c);
                else if (r.Status[j] == ConditionStatus.Uncertain || !r.AbsenceSupported[j]) uncertain.Add(c);
            }
            if (uncertain.Count > 0) r.Reasons.Add("uncertain or unsupported absence for: " + string.Join(", ", uncertain));
            if (r.Flags.Count > 0) r.Reasons.Add("unresolved flags: " + string.Join(", ", r.Flags));
            bool normalWouldBeSupported = predicted.Count == 0 && uncertain.Count == 0 && r.Flags.Count == 0;
            if (normalWouldBeSupported && (quality == null || !quality.Assessed))
            { r.Reasons.Add("image quality was not assessed: a Normal result cannot be supported"); r.State = V6States.UncertainReviewRequired; return r; }
            if (uncertain.Count > 0 || r.Flags.Count > 0) { r.State = V6States.UncertainReviewRequired; return r; }
            r.State = predicted.Count >= 2 ? V6States.MultipleFindingsSupported : predicted.Count == 1 ? V6States.FindingsSupported : V6States.NoFindingsSupported;
            return r;
        }
    }
}
