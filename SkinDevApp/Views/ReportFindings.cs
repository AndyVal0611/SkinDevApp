// ============================================================================
// ReportFindings.cs  -  namespace SkinDevApp.Views   (no UI code)
//
// Decides what the printable Research Analysis Report shows, using ONLY values
// already stored for the scan (class scores, fusion, capture quality, saved maps).
//
//  * Major Findings text  - every sentence is generated from stored numbers.
//  * Evidence selection   - which Grad-CAM++ maps are printed. The complete set
//                           (3 originals + 12 maps) always stays in the scan record
//                           and in the Comparison Gallery.
//
// Selection is NOT cherry-picking: besides the primary-class map, evidence is
// added automatically whenever a view disagrees, a class score differs strongly
// between views, a competing class has a substantial score, the overall result
// has low confidence, or a capture needs a quality check.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class EvidenceItem
    {
        public CaptureViewRow View { get; set; }
        public AttributionMapRow Map { get; set; }
        public string ClassName { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
        public bool IsPrimary { get; set; }
    }

    public sealed class ReportFindings
    {
        /// <summary>A non-top class at/above this score (percent) in any view is shown as a competing class.</summary>
        public const double CompetingMin = 15.0;
        /// <summary>Overall score (percent) below which the result is called low-confidence.</summary>
        public const double LowConfidence = 60.0;
        public const int MaxEvidence = 6;

        public bool HasResult { get; private set; }
        public bool Single { get; private set; }
        public string OverallClass { get; private set; }
        public double? OverallScore { get; private set; }

        /// <summary>Why a reviewer should look at the individual angles (empty = nothing to flag).</summary>
        public List<string> Flags { get; } = new List<string>();
        public bool ReviewRecommended => Flags.Count > 0;
        public bool ViewDisagreement { get; private set; }

        public List<EvidenceItem> Evidence { get; } = new List<EvidenceItem>();
        public int Omitted { get; private set; }
        public List<string> ViewsWithoutMaps { get; } = new List<string>();

        public List<string> Lines { get; } = new List<string>();

        public static string Label(string cls) => cls == "Normal" ? "Normal Skin" : cls;

        private static double Score(CaptureViewRow v, string cls) =>
            v.Scores.AsArray()[Array.IndexOf(StudyText.Classes, cls)];

        private static string P(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";

        public static ReportFindings Build(SessionDetail d)
        {
            var f = new ReportFindings();
            ScanSessionRow s = d.Session;
            f.Single = s.ScanMode == "Single";
            f.OverallClass = s.OverallPredictedClass;
            f.OverallScore = s.OverallScore;

            List<CaptureViewRow> views = d.Views.Where(v => v.Scores != null).ToList();
            f.HasResult = f.OverallClass != null && views.Count > 0;
            if (!f.HasResult)
            {
                f.Lines.Add("No view produced a usable AI result for this scan.");
                return f;
            }

            string top = f.OverallClass;
            string[] others = StudyText.Classes.Where(c => c != top).ToArray();

            // ---- flags ------------------------------------------------------------
            f.ViewDisagreement = s.ViewDisagreement;
            if (!f.Single)
            {
                if (s.ViewDisagreement)
                {
                    f.Flags.Add("View disagreement detected — review recommended.");
                    if (d.Fusion != null) foreach (string n in d.Fusion.Notes) if (!n.StartsWith("Only ")) f.Flags.Add(n);
                }
                if (s.ViewsCompleted < s.ViewsExpected)
                    f.Flags.Add("Incomplete scan: " + s.ViewsCompleted + " of " + s.ViewsExpected + " views captured.");
            }
            if (f.OverallScore.HasValue && f.OverallScore.Value < LowConfidence)
                f.Flags.Add("Low-confidence primary prediction (" + P(f.OverallScore.Value) + ").");

            var badQuality = views.Where(v => !string.IsNullOrEmpty(v.QualityStatus) && v.QualityStatus != "Good").ToList();
            foreach (CaptureViewRow v in badQuality)
                f.Flags.Add(v.ViewType + " view: " + v.QualityStatus.Replace("Check: ", "capture quality needs checking (") + (v.QualityStatus.StartsWith("Check: ") ? ")." : "."));

            // highest competing class over all views
            CaptureViewRow compView = null; string compClass = null; double compScore = -1;
            foreach (CaptureViewRow v in views)
                foreach (string c in others)
                {
                    double sc = Score(v, c);
                    if (sc > compScore) { compScore = sc; compView = v; compClass = c; }
                }
            if (compScore >= CompetingMin)
                f.Flags.Add("Competing class: " + Label(compClass) + " " + P(compScore) + " in the " + compView.ViewType + " view.");

            // ---- evidence selection -------------------------------------------------
            var seen = new HashSet<string>();
            void Add(CaptureViewRow v, string cls, string reason, bool primary = false)
            {
                string key = v.ViewType + "|" + cls;
                if (seen.Contains(key)) return;
                AttributionMapRow m = v.Maps.FirstOrDefault(x => x.TargetClass == cls && !string.IsNullOrEmpty(x.OverlayPath));
                if (m == null)
                {
                    if (!f.ViewsWithoutMaps.Contains(v.ViewType)) f.ViewsWithoutMaps.Add(v.ViewType);
                    return;
                }
                seen.Add(key);
                if (f.Evidence.Count >= MaxEvidence) { f.Omitted++; return; }
                f.Evidence.Add(new EvidenceItem { View = v, Map = m, ClassName = cls, Score = Score(v, cls), Reason = reason, IsPrimary = primary });
            }

            // 1. primary class: Front is the reference view; otherwise the view that scores it highest
            Func<CaptureViewRow, bool> hasTopMap = v => v.Maps.Any(m => m.TargetClass == top && !string.IsNullOrEmpty(m.OverlayPath));
            CaptureViewRow primaryView = views.Where(hasTopMap)
                .OrderBy(v => v.ViewType == "Front" ? 0 : 1).ThenByDescending(v => Score(v, top)).FirstOrDefault();
            if (primaryView != null) Add(primaryView, top, "Primary predicted class (" + primaryView.ViewType + " view)", primary: true);
            else foreach (CaptureViewRow v in views) if (!f.ViewsWithoutMaps.Contains(v.ViewType)) f.ViewsWithoutMaps.Add(v.ViewType);

            if (top != "Normal")
            {
                CaptureViewRow strongest = views.Where(v => v != primaryView && hasTopMap(v)).OrderByDescending(v => Score(v, top)).FirstOrDefault();
                if (strongest != null) Add(strongest, top, "Strongest " + Label(top) + " score in a second view");
            }

            // 2. a view whose own top class differs from the overall class
            foreach (CaptureViewRow v in views.Where(v => v.Scores.PredictedClass != top))
                Add(v, v.Scores.PredictedClass, "View disagreement: this view predicts " + Label(v.Scores.PredictedClass) + ", overall result is " + Label(top));

            // 3. a class whose score differs strongly between views
            if (views.Count > 1)
                foreach (string c in StudyText.Classes)
                {
                    double hi = views.Max(v => Score(v, c)), lo = views.Min(v => Score(v, c));
                    if (hi - lo >= SkinDevApp.Scanning.ViewFusion.DisagreementSpread)
                    {
                        // show the view that departs from the overall result: the lowest score for the
                        // overall class, the highest score for any other class
                        CaptureViewRow hv = c == top ? views.First(v => Score(v, c) == lo) : views.First(v => Score(v, c) == hi);
                        Add(hv, c, Label(c) + " score differs by " + (hi - lo).ToString("0", CultureInfo.InvariantCulture) + " points between views (" +
                                   hv.ViewType + " view: " + Score(hv, c).ToString("0.0", CultureInfo.InvariantCulture) + "%)");
                    }
                }

            // 4. a competing class with a substantial score
            if (compScore >= CompetingMin)
                foreach (string c in others)
                {
                    CaptureViewRow hv = views.OrderByDescending(v => Score(v, c)).First();
                    if (Score(hv, c) >= CompetingMin)
                        Add(hv, c, "Competing class: " + Label(c) + " " + P(Score(hv, c)) + " (" + hv.ViewType + " view)");
                }

            // 5. low confidence: show the runner-up class too
            if (f.OverallScore.HasValue && f.OverallScore.Value < LowConfidence && compClass != null)
                Add(compView, compClass, "Low-confidence result: strongest competing class");

            // 6. a capture that needs a quality check
            foreach (CaptureViewRow v in badQuality)
                Add(v, v.Scores.PredictedClass, "Capture quality needs checking (" + v.ViewType + " view)");

            // ---- major findings text (stored values only) -----------------------------
            f.Lines.Add("Overall AI classification: " + Label(top) + (f.OverallScore.HasValue
                ? " (" + P(f.OverallScore.Value) + (f.Single ? " Model Class Score)" : " pooled Model Class Score)") : ""));

            foreach (CaptureViewRow v in d.Views)
                f.Lines.Add(v.ViewType + ": " + (v.Scores == null ? "not analysed" : Label(v.Scores.PredictedClass) + " — " + P(v.Scores.Confidence)));

            if (!f.Single)
            {
                int distinct = views.Select(v => v.Scores.PredictedClass).Distinct().Count();
                f.Lines.Add("Multi-view consistency: " + (views.Count < 2 ? "only one view available."
                    : distinct == 1 && !s.ViewDisagreement ? "all " + views.Count + " views produced the same primary class."
                    : distinct == 1 ? "same primary class in every view, but a class score differs strongly between views."
                    : "the views produced different primary classes — review each angle."));
            }

            if (compClass != null)
                f.Lines.Add("Highest competing class: " + Label(compClass) + " — " + P(compScore) + (f.Single ? "." : " in the " + compView.ViewType + " view."));

            f.Lines.Add("Capture quality: " + (badQuality.Count == 0
                ? (f.Single ? "Good." : "Good across all " + views.Count + " analysed view(s).")
                : string.Join("; ", badQuality.Select(v => v.ViewType + " — " + v.QualityStatus)) + "."));

            if (f.Evidence.Count == 0)
                f.Lines.Add("Grad-CAM++ evidence shown: none — attribution maps were not available for this scan.");
            else
                f.Lines.Add("Grad-CAM++ evidence shown: " + string.Join("; ", f.Evidence.Select(e => e.View.ViewType + " " + Label(e.ClassName))) +
                            (f.Omitted > 0 ? " (+" + f.Omitted + " more retained in the electronic record)" : "") + ".");

            return f;
        }
    }
}
