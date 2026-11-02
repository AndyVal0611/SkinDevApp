// ============================================================================
// V6SessionSummary.cs  -  namespace SkinDevApp.V6
//
// One scan's saved V6 assessments reduced to text, for every screen and printout that used to show the V5 classifier and the separate detectors.
// Built only from the sealed files saved at scan time (never re-run). The photographs of a three-view scan are NOT merged into one verdict:
// each is reported on its own and the headline only counts how many need a closer look. Null when the V6 flag is off or the scan has no V6 files,
// so older scans keep showing what produced them.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using SkinDevApp.Data;

namespace SkinDevApp.V6
{
    public sealed class V6ViewSummary
    {
        public string View, Label, Headline, BoxesLine;
        public UnifiedAssessment Assessment;
        public List<string> ConditionLines = new List<string>();
        public List<string> FlagLines = new List<string>();
        public bool ReviewRequired;
    }

    public sealed class V6SessionSummary
    {
        public bool Single;
        public string Headline, ShortLabel;
        public bool ReviewRecommended;
        public List<V6ViewSummary> Views = new List<V6ViewSummary>();
        public List<string> Notes = new List<string>();
        public List<string> Problems = new List<string>();
        public string Disclaimer = V6Text.Disclaimer;

        /// <summary>Conditions the saved V6 results call present in at least one photograph.</summary>
        public List<string> PresentUnion = new List<string>();
        /// <summary>Every photograph ended in NO_FINDINGS_SUPPORTED.</summary>
        public bool AllNoFindings;

        public static V6SessionSummary For(SessionDetail d)
        {
            List<string> problems;
            Dictionary<string, UnifiedAssessment> map = V6SessionLoader.Load(d, out problems);
            return map == null ? null : From(map, d.Session != null && d.Session.ScanMode == "Single", problems);
        }

        private static readonly string[] Order = { "Single", "Front", "Left", "Right" };

        public static V6SessionSummary From(Dictionary<string, UnifiedAssessment> map, bool single, IList<string> problems = null)
        {
            var s = new V6SessionSummary { Single = single || map.Count == 1 };
            if (problems != null) s.Problems.AddRange(problems);
            foreach (string key in Order.Where(map.ContainsKey).Concat(map.Keys.Where(k => !Order.Contains(k))))
            {
                UnifiedAssessment a = map[key];
                var v = new V6ViewSummary
                {
                    View = key, Label = key == "Single" ? "Photograph" : key + " view", Assessment = a, Headline = V6Text.StateHeadline(a), BoxesLine = V6Text.BoxesLine(a), ReviewRequired = a.ReviewRequired,
                    ConditionLines = a.Conditions.Select(V6Text.ConditionLine).ToList(), FlagLines = V6Text.FlagLines(a).Distinct().ToList()
                };
                s.Views.Add(v);
            }
            s.PresentUnion = s.Views.SelectMany(v => v.Assessment.PresentConditions).Distinct().OrderBy(c => V6Conditions.IndexOf(c)).ToList();
            s.AllNoFindings = s.Views.Count > 0 && s.Views.All(v => v.Assessment.State == V6States.NoFindingsSupported);
            int review = s.Views.Count(v => v.ReviewRequired);
            s.ReviewRecommended = review > 0;
            if (s.Single)
            {
                s.Headline = s.Views[0].Headline;
                s.ShortLabel = V6Text.ShortLabel(s.Views[0].Assessment);
            }
            else
            {
                s.Headline = "Unified AI assessment for each photograph" + (review > 0 ? ": " + review + " of " + s.Views.Count + " need a closer look" : "");
                s.ShortLabel = s.PresentUnion.Count > 0 ? "V6: " + string.Join("+", s.PresentUnion) : s.AllNoFindings ? "V6: no findings" : "V6: review required";
                s.Notes.Add("The photographs are not merged into a single verdict; each one is reported on its own.");
                if (s.Views.Select(v => string.Join("|", v.Assessment.PresentConditions.OrderBy(c => c))).Distinct().Count() > 1)
                    s.Notes.Add("The photographs do not all show the same findings.");
            }
            return s;
        }
    }
}
