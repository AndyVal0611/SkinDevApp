// ============================================================================
// V6Agreement.cs  -  namespace SkinDevApp.V6
//
// The suggested "agreement with the AI" in the researcher review, when the AI result is a V6 unified assessment (several conditions possible,
// a result can be "uncertain"). It is only a SUGGESTION: the reviewer can change it, and no review ever changes the saved AI result.
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace SkinDevApp.V6
{
    public static class V6Agreement
    {
        /// <summary>labels: what the reviewer ticked (Acne, Eczema, Hyperpigmentation, Normal, Other / uncertain, Unusable image). Returns Agree, Partially agree, Disagree, Uncertain, or null when nothing is ticked.</summary>
        public static string Suggest(IList<string> labels, V6SessionSummary ai)
        {
            if (labels == null || labels.Count == 0 || ai == null) return null;
            if (labels.Contains("Other / uncertain") || labels.Contains("Unusable image")) return "Uncertain";
            List<string> human = labels.Where(l => V6Conditions.IndexOf(l) >= 0).ToList();
            bool humanNormal = labels.Contains("Normal");
            if (ai.PresentUnion.Count == 0)
            {
                if (ai.AllNoFindings) return humanNormal && human.Count == 0 ? "Agree" : "Disagree";
                return "Uncertain";                                   // the AI itself was uncertain or could not decide: there is nothing to agree with
            }
            if (humanNormal && human.Count == 0) return "Disagree";
            if (human.Count == ai.PresentUnion.Count && human.All(ai.PresentUnion.Contains)) return "Agree";
            if (human.Any(ai.PresentUnion.Contains)) return "Partially agree";
            return "Disagree";
        }
    }
}
