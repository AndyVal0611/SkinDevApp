// SessionInterpretation.cs - builds the ScanInterpretation of a saved scan (or one of its photographs) from the stored scores and boxes.
// Reads only; it never writes to the scan record and ignores researcher and dermatologist reviews, which are kept separately.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class SessionInterpretation
    {
        private static readonly string[] Conditions = { "Acne", "Hyperpigmentation", "Eczema" };

        /// <summary>The pooled four-class scores (as on the Results page): fused over the views, or the single analysed view.</summary>
        public static double[] OverallPercents(SessionDetail d)
        {
            Scanning.FusedResult f = d.Fusion;
            if (f != null && f.PooledPercent != null && f.PooledPercent.Count > 0)
                return StudyText.Classes.Select(c => { double v; return f.PooledPercent.TryGetValue(c, out v) ? v : 0; }).ToArray();
            CaptureViewRow only = d.Views.FirstOrDefault(v => v.Scores != null);
            return only != null ? only.Scores.AsArray() : new double[4];
        }

        /// <summary>The whole scan: pooled scores with the detector evidence of every analysed photograph.</summary>
        public static ScanInterpretation For(SessionDetail d)
        {
            List<CaptureViewRow> scored = d.Views.Where(v => v.Scores != null).ToList();
            if (scored.Count == 0) return ScanInterpreter.Interpret(null, new List<ViewEvidence>());
            return ScanInterpreter.Interpret(OverallPercents(d), scored.Select(Evidence).ToList());
        }

        /// <summary>One photograph on its own.</summary>
        public static ScanInterpretation For(CaptureViewRow v) =>
            v?.Scores == null ? ScanInterpreter.Interpret(null, new List<ViewEvidence>()) : ScanInterpreter.Interpret(v.Scores.AsArray(), Evidence(v));

        public static ViewEvidence Evidence(CaptureViewRow v)
        {
            bool ok = v.Localization != null && v.Localization.Status == "OK";
            return new ViewEvidence
            {
                Lesions = v.Lesions.Select(l => new LesionFinding { ClassName = l.ClassName, Confidence = l.Confidence }).ToList(),
                Covered = Conditions.Select(c => ok && Covers(v.Localization.ModelTag, c)).ToArray()
            };
        }

        /// <summary>
        /// Did the saved detector run cover this class? Specialist models are saved as "tag [Acne, 800 px]"; a part without brackets
        /// is the older 3-class detector, which covered acne, hyperpigmentation and eczema.
        /// </summary>
        public static bool Covers(string modelTag, string cls)
        {
            if (string.IsNullOrWhiteSpace(modelTag)) return false;
            foreach (string part in modelTag.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = Regex.Match(part, @"\[([^\]]*)\]");
                if (!m.Success) return true;
                string names = m.Groups[1].Value.Split(',')[0];
                if (names.Split('/').Any(n => string.Equals(n.Trim(), cls, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
    }
}
