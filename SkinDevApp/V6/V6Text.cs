// ============================================================================
// V6Text.cs  -  namespace SkinDevApp.V6
//
// Plain-language wording of a UnifiedAssessment, shared by every place that shows or prints it (Results page, receipts, the A4 report,
// the dermatologist and researcher windows). Pure text: no WPF, no database. Cautious by design: an experimental AI assessment, not a diagnosis.
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkinDevApp.V6
{
    public static class V6Text
    {
        public const string Disclaimer = "Research assessment from a trained image model combined with lesion-detector evidence. It is not a diagnosis, and it is not clinically validated. " +
                                         "Every assessment waits for dermatologist review. Boxes are possible locations only and are not a lesion count or a measure of severity.";

        public const string DisclaimerShort = "Research assessment, not a diagnosis. It waits for dermatologist review.";

        public static string StateHeadline(UnifiedAssessment a)
        {
            switch (a.State)
            {
                case V6States.FindingsSupported: return "Finding supported: " + string.Join(", ", a.PresentConditions);
                case V6States.MultipleFindingsSupported: return "Several findings supported: " + string.Join(", ", a.PresentConditions);
                case V6States.NoFindingsSupported: return "No findings supported (normal-looking skin in the AI evidence)";
                case V6States.UncertainReviewRequired: return "Uncertain: review recommended";
                case V6States.RecaptureRequired: return "Please retake the photograph";
                case V6States.OutOfScopeReviewRequired: return "Outside what the system was trained on: review required";
                default: return "The analysis could not be completed";
            }
        }

        public static string VerdictText(ConditionAssessment c) => c.Verdict == "present" ? "Present" : c.Verdict == "absent" ? "Not found" : c.Verdict == "possible" ? "Possible" : "Uncertain";

        public static string DetectorText(ConditionAssessment c) =>
            !c.DetectorAvailable ? "detector not available" : c.DetectorFired ? "detector marked an area" : "detector ran: nothing marked";

        public static string Pct(double p) => (p * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

        /// <summary>"AI support 42.1% (decision limit 42.0%)": one decimal only when the whole numbers would look identical.</summary>
        public static string SupportText(ConditionAssessment c)
        {
            bool close = Pct(c.CalibratedProbability) == Pct(c.Threshold);
            string p = close ? (c.CalibratedProbability * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%" : Pct(c.CalibratedProbability);
            string t = close ? (c.Threshold * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%" : Pct(c.Threshold);
            return "AI support " + p + " (decision limit " + t + ")";
        }

        /// <summary>"Acne: Not found (AI support 12% (decision limit 42%); detector ran: nothing marked)"</summary>
        public static string ConditionLine(ConditionAssessment c) =>
            c.Condition + ": " + VerdictText(c) + " (" + SupportText(c) + "; " + DetectorText(c) + ")";

        private static string JoinNames(System.Collections.Generic.IList<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        /// <summary>Plain-language review notes. Conditions that share a note are named together, so the same sentence is never repeated.</summary>
        public static IEnumerable<string> FlagLines(UnifiedAssessment a)
        {
            var conflict = new System.Collections.Generic.List<string>(); var near = new System.Collections.Generic.List<string>(); var other = new System.Collections.Generic.List<string>();
            foreach (string f in a.Flags)
            {
                if (f.StartsWith("cnn_yolo_conflict_")) conflict.Add(f.Substring("cnn_yolo_conflict_".Length));
                else if (f.StartsWith("near_threshold_")) near.Add(f.Substring("near_threshold_".Length));
                else if (f == "ood") other.Add("This photograph looks unlike the photographs the system was trained on.");
                else if (f.StartsWith("quality_warning:"))
                {
                    string w = f.Substring("quality_warning:".Length);
                    other.Add(w == "face_not_found" ? "No face was found in this photograph, so the image quality could not be fully checked." : "Photo quality warning: " + w.Replace(",", ", "));
                }
                else if (f.StartsWith("quality_")) other.Add(V6QualityMapper.Advice(f.Substring("quality_".Length)) ?? ("Photo quality: " + f.Substring("quality_".Length)));
                else other.Add(f);
            }
            if (conflict.Count > 0) yield return "For " + JoinNames(conflict) + ", the whole-photograph model and the lesion detector point different ways.";
            if (near.Count > 0) yield return JoinNames(near) + (near.Count == 1 ? " is" : " are") + " close to the decision limit.";
            foreach (string o in other) yield return o;
            foreach (string r in a.Reasons)
                if (r.StartsWith("detector unavailable") || r.StartsWith("image quality was not assessed") || r.StartsWith("error:"))      // the quality advice above already says it in plain words
                    yield return char.ToUpper(r[0]) + r.Substring(1);
        }

        public static string BoxesLine(UnifiedAssessment a) =>
            a.Boxes.Count == 0 ? "Marked areas: none"
                               : "Marked areas: " + a.Boxes.Count + " (" + string.Join(", ", a.Boxes.GroupBy(b => b.Condition).Select(g => g.Key + " " + g.Count())) + ")";

        /// <summary>Short label kept in the receipt history (the scan record itself is never changed).</summary>
        public static string ShortLabel(UnifiedAssessment a) =>
            a.State == V6States.FindingsSupported || a.State == V6States.MultipleFindingsSupported ? "V6: " + string.Join("+", a.PresentConditions) :
            a.State == V6States.NoFindingsSupported ? "V6: no findings" :
            a.State == V6States.InferenceError ? "V6: not completed" : "V6: review required";
    }
}
