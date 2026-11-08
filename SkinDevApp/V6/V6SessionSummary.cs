// ============================================================================
// V6SessionSummary.cs  -  namespace SkinDevApp.V6
//
// One scan's saved final-model results reduced to clinician-facing text, shared by every screen and printout (Results page, patient record,
// dermatologist and researcher review, thermal receipt, A4 report). Built only from the sealed files next to each photograph (never re-run).
// The wording comes from ClinicalSummary: Condition detected / Needs review / No condition detected, per-condition status, certainty and the
// facial regions where the detectors marked areas. Null when the scan has no saved result.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using SkinDevApp.Data;

namespace SkinDevApp.V6
{
    public sealed class V6ViewSummary
    {
        public string View, Label, Headline, BoxesLine, Origin;
        public UnifiedAssessment Assessment;
        public ClinicalSummary Clinical;
        public List<string> ConditionLines = new List<string>();
        public List<string> FlagLines = new List<string>();
        public bool ReviewRequired;
    }

    public sealed class V6SessionSummary
    {
        public bool Single;
        public string Headline, ShortLabel;
        public bool ReviewRecommended;
        public ClinicalSummary Clinical;
        public string ReviewStatus = Data.ReviewStatus.Pending;
        public List<V6ViewSummary> Views = new List<V6ViewSummary>();
        public List<string> Notes = new List<string>();
        public List<string> Problems = new List<string>();
        public string Disclaimer = ClinicalSummary.Disclaimer;

        /// <summary>Conditions detected in at least one photograph.</summary>
        public List<string> PresentUnion = new List<string>();
        /// <summary>Every photograph: no condition detected.</summary>
        public bool AllNoFindings;
        /// <summary>At least one photograph was assessed later from its saved image (taken before the final model was installed).</summary>
        public bool Reanalysed;

        /// <summary>Face box (normalised x, y, w, h) of a photograph folder, used to name facial regions. Set by the application; null in tests.</summary>
        public static Func<string, double[]> FaceBoxProvider { get; set; }

        public static V6SessionSummary For(SessionDetail d)
        {
            List<string> problems;
            Dictionary<string, UnifiedAssessment> map = V6SessionLoader.Load(d, out problems);
            if (map == null) return null;
            var folders = new Dictionary<string, string>();
            foreach (CaptureViewRow v in d.Views) if (!string.IsNullOrEmpty(v.ViewType) && !folders.ContainsKey(v.ViewType)) folders[v.ViewType] = v.RecordFolder;
            V6SessionSummary s = From(map, d.Session != null && d.Session.ScanMode == "Single", problems, folders);
            if (d.Session != null) s.ReviewStatus = Data.ReviewStatus.Normalise(d.Session.ReviewStatus);
            return s;
        }

        private static readonly string[] Order = { "Single", "Front", "Left", "Right" };

        public static string LabelFor(string key) => key == "Single" ? "Photograph" : key == "Left" ? "Left side" : key == "Right" ? "Right side" : key == "Front" ? "Front" : key;

        public static V6SessionSummary From(Dictionary<string, UnifiedAssessment> map, bool single, IList<string> problems = null, IDictionary<string, string> folders = null)
        {
            var s = new V6SessionSummary { Single = single || map.Count == 1 };
            if (problems != null) s.Problems.AddRange(problems);
            foreach (string key in Order.Where(map.ContainsKey).Concat(map.Keys.Where(k => !Order.Contains(k))))
            {
                UnifiedAssessment a = map[key];
                string folder = folders != null && folders.TryGetValue(key, out string f) ? f : null;
                double[] face = null;
                try { face = folder != null && FaceBoxProvider != null ? FaceBoxProvider(folder) : null; } catch { }
                ClinicalSummary c = ClinicalSummary.From(a, face);
                var v = new V6ViewSummary
                {
                    View = key, Label = LabelFor(key), Assessment = a, Clinical = c, Headline = c.Title, ReviewRequired = c.Category != ClinicalCategory.ConditionDetected && c.Category != ClinicalCategory.NoConditionDetected,
                    ConditionLines = c.Findings.Select(x => x.Condition + ": " + ClinicalSummary.FindingLine(x)).ToList(),
                    FlagLines = c.ReviewReasons.Distinct().ToList(),
                    BoxesLine = AreasLine(c), Origin = folder != null ? V6Archive.ReadOrigin(folder) : null
                };
                s.Views.Add(v);
            }
            s.Clinical = ClinicalSummary.ForScan(s.Views.Select(v => new KeyValuePair<string, ClinicalSummary>(v.Label, v.Clinical)).ToList());
            s.PresentUnion = s.Clinical.DetectedFindings.Select(x => x.Condition).ToList();
            s.AllNoFindings = s.Views.Count > 0 && s.Views.All(v => v.Clinical.Category == ClinicalCategory.NoConditionDetected);
            s.ReviewRecommended = s.Clinical.Category == ClinicalCategory.NeedsReview || s.Clinical.Category == ClinicalCategory.Retake || s.Clinical.Category == ClinicalCategory.NotCompleted;
            s.Headline = s.Clinical.Title;
            s.ShortLabel = s.Clinical.CategoryLabel + (s.PresentUnion.Count > 0 ? ": " + string.Join(", ", s.PresentUnion) : "");
            s.Reanalysed = s.Views.Any(v => v.Origin == "Re-analysis");
            if (!s.Single && s.Views.Select(v => string.Join("|", v.Clinical.DetectedFindings.Select(x => x.Condition))).Distinct().Count() > 1)
                s.Notes.Add("The photographs do not all show the same findings; each photograph is listed separately.");
            if (s.Reanalysed)
                s.Notes.Add("This scan was re-analyzed from its saved photographs.");
            return s;
        }

        /// <summary>"Areas marked: forehead, right cheek (Acne); nose (Hyperpigmentation)" or "No areas marked".</summary>
        public static string AreasLine(ClinicalSummary c)
        {
            var parts = c.Findings.Where(f => f.Regions.Count > 0).Select(f => f.RegionsText + " (" + f.Condition + ")").ToList();
            return parts.Count == 0 ? "No areas marked on the face" : "Areas marked: " + string.Join("; ", parts);
        }
    }
}
