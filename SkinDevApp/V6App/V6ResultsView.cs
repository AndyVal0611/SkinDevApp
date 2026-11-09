// ============================================================================
// V6ResultsView.cs  -  namespace SkinDevApp.V6   (WPF presentation of the final model's result)
//
// The clinician-facing result card used on the Results page, and the compact summary used in the patient record, dermatologist review and
// researcher windows. It only READS what was saved (v6_assessment.json next to each photograph); nothing is re-run. Wording comes from
// ClinicalSummary / V6SessionSummary, shared with the thermal receipt and the A4 report, so every surface says the same thing.
//
// Layout: verification status · outcome banner (Condition detected / Needs review / No condition detected) · findings table
// (condition, AI result, certainty, facial regions) · why review is needed · model and date · disclaimer · technical details (collapsed).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkinDevApp.Data;
using SkinDevApp.Imaging;
using SkinDevApp.Views;

namespace SkinDevApp.V6
{
    public static class V6ResultsView
    {
        public const string Disclaimer = ClinicalSummary.Disclaimer;

        /// <summary>Saved final-model results by view type (Single / Front / Left / Right), or null when the scan has none.</summary>
        public static Dictionary<string, UnifiedAssessment> Load(SessionDetail d, out List<string> problems) => V6SessionLoader.Load(d, out problems);

        public static string MarkedAreasPath(CaptureViewRow v) => V6SessionLoader.MarkedAreasPath(v);

        // ------------------------------------------------------------------ colours

        public static string CategoryInk(ClinicalCategory c) =>
            c == ClinicalCategory.ConditionDetected ? "#9B2F25" : c == ClinicalCategory.NeedsReview ? "#8A5A00" : c == ClinicalCategory.NoConditionDetected ? "#3E6B3D" : "#5F5E5A";

        private static string CategoryBg(ClinicalCategory c) =>
            c == ClinicalCategory.ConditionDetected ? "#FBEDEA" : c == ClinicalCategory.NeedsReview ? "#FFF6E6" : c == ClinicalCategory.NoConditionDetected ? "#EEF3E8" : "#F1EFE8";

        private static string CategoryLine(ClinicalCategory c) =>
            c == ClinicalCategory.ConditionDetected ? "#EDB9B1" : c == ClinicalCategory.NeedsReview ? "#F0D49A" : c == ClinicalCategory.NoConditionDetected ? "#CBDCBF" : "#D3D1C7";

        public static string StatusInk(string status) =>
            status == ReviewStatus.Verified ? "#3E6B3D" : status == ReviewStatus.FurtherReview ? "#9B2F25" : "#8A5A00";

        private static string StatusBg(string status) =>
            status == ReviewStatus.Verified ? "#EEF3E8" : status == ReviewStatus.FurtherReview ? "#FBEDEA" : "#FFF6E6";

        private static string FindingInk(ClinicalFinding f)
        {
            if (f.Status == ClinicalSummary.Detected) { int i = V6Conditions.IndexOf(f.Condition); return i >= 0 ? ClassPalette.Hex[i] : "#9B2F25"; }
            if (f.Status == ClinicalSummary.NotDetected) return "#3E6B3D";
            return "#8A5A00";
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>"Pending verification" / "Verified" / "Requires further review" as a coloured chip.</summary>
        public static Border StatusChip(string status)
        {
            status = ReviewStatus.Normalise(status);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Ui.Brush(StatusInk(status)), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = status, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(StatusInk(status)), VerticalAlignment = VerticalAlignment.Center });
            return new Border { Background = Ui.Brush(StatusBg(status)), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        }

        /// <summary>The outcome in one coloured panel: label, title and one plain sentence.</summary>
        public static Border OutcomeBanner(ClinicalSummary s)
        {
            string ink = CategoryInk(s.Category);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = s.CategoryLabel.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(ink), Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(new TextBlock { Text = s.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(ink), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            if (!string.IsNullOrEmpty(s.Explanation)) sp.Children.Add(Ui.Text(s.Explanation, 13, false, Ui.Ink, new Thickness(0)));
            return new Border
            {
                Background = Ui.Brush(CategoryBg(s.Category)), BorderBrush = Ui.Brush(CategoryLine(s.Category)), BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 10, 0, 10), Child = sp
            };
        }

        private static Border ResultPill(ClinicalFinding f) => new Border
        {
            Background = Ui.Brush(f.Status == ClinicalSummary.Detected ? "#FBEDEA" : f.Status == ClinicalSummary.NotDetected ? "#EEF3E8" : "#FFF6E6"),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = f.Status == ClinicalSummary.Possible || f.Status == ClinicalSummary.Inconclusive ? f.Status + " · review" : f.Status,
                                    FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(FindingInk(f)) }
        };

        private static string RegionText(ClinicalFinding f) =>
            f.Regions.Count == 0 ? (f.Status == ClinicalSummary.Detected || f.Status == ClinicalSummary.Possible ? "Not localised" : "—") : string.Join("; ", f.Regions);

        /// <summary>The same findings stacked one under another, for narrow panels (patient record, review windows, records screen).</summary>
        public static StackPanel FindingsList(ClinicalSummary s)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            if (s.Findings.Count == 0) { sp.Children.Add(Ui.Text(NoFindingsText(s), 12.5, false, Ui.Muted, new Thickness(0, 2, 0, 4))); return sp; }
            foreach (ClinicalFinding f in s.Findings)
            {
                int idx = V6Conditions.IndexOf(f.Condition);
                var item = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var top = new DockPanel();
                Border pill = ResultPill(f); DockPanel.SetDock(pill, Dock.Right);
                top.Children.Add(pill);
                top.Children.Add(Ui.Text(f.Condition, 13, true, idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink, new Thickness(0, 2, 8, 0)));
                item.Children.Add(top);
                string detail = "Confidence " + ClinicalSummary.ConfidenceText(f) + " · " + (f.Status == ClinicalSummary.Detected || f.Status == ClinicalSummary.NotDetected ? f.Certainty + " certainty" + (f.ByDetector ? " · found by the lesion detector" : "") : "Review needed") +
                                (f.Regions.Count > 0 ? " · " + string.Join("; ", f.Regions) : "");
                item.Children.Add(Ui.Text(detail, 12, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
                sp.Children.Add(item);
                sp.Children.Add(new Border { Height = 1, Background = Ui.Brush("#EFEADC") });
            }
            return sp;
        }

        private static string NoFindingsText(ClinicalSummary s) =>
            s.Category == ClinicalCategory.Retake ? "No condition is reported for this image because it must be retaken."
            : "No result is available for this image.";

        /// <summary>The findings at a glance: condition, detection status and facial regions. Confidence figures are one click away (ConfidenceDetails).</summary>
        public static Grid FindingsTable(ClinicalSummary s, double fontSize = 13)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            if (s.Findings.Count == 0) { g.Children.Add(Ui.Text(NoFindingsText(s), 13, false, Ui.Muted, new Thickness(0, 2, 0, 4))); return g; }
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] head = { "Condition", "Detection status", "Facial regions" };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < head.Length; c++)
            {
                var t = Ui.Text(head[c], 11, true, Ui.Muted, new Thickness(0, 0, 8, 4)); Grid.SetColumn(t, c); g.Children.Add(t);
            }
            var rule = new Border { Height = 1, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumnSpan(rule, 3); g.Children.Add(rule);
            int row = 1;
            foreach (ClinicalFinding f in s.Findings)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int idx = V6Conditions.IndexOf(f.Condition);
                var name = Ui.Text(f.Condition, fontSize, true, idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink, new Thickness(0, 7, 8, 7));
                Border result = ResultPill(f); result.Margin = new Thickness(0, 5, 8, 5);
                var regions = Ui.Text(RegionText(f), fontSize, false, Ui.Ink, new Thickness(0, 7, 0, 7));
                Grid.SetRow(name, row); Grid.SetRow(result, row); Grid.SetRow(regions, row);
                Grid.SetColumn(result, 1); Grid.SetColumn(regions, 2);
                g.Children.Add(name); g.Children.Add(result); g.Children.Add(regions);
                if (row < s.Findings.Count)
                {
                    var r2 = new Border { Height = 1, Background = Ui.Brush("#EFEADC"), VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetRow(r2, row); Grid.SetColumnSpan(r2, 3); g.Children.Add(r2);
                }
                row++;
            }
            return g;
        }

        /// <summary>Optional (closed by default): the confidence and certainty behind each detection status.</summary>
        public static UIElement ConfidenceDetails(ClinicalSummary s)
        {
            if (s.Findings.Count == 0) return new StackPanel();
            var sp = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] head = { "Condition", "Confidence", "Certainty" };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < head.Length; c++) { var t = Ui.Text(head[c], 11, true, Ui.Muted, new Thickness(0, 0, 8, 4)); Grid.SetColumn(t, c); g.Children.Add(t); }
            int row = 1;
            foreach (ClinicalFinding f in s.Findings)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int idx = V6Conditions.IndexOf(f.Condition);
                var cells = new UIElement[]
                {
                    Ui.Text(f.Condition, 12.5, true, idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink, new Thickness(0, 4, 8, 4)),
                    Ui.Text(ClinicalSummary.ConfidenceText(f), 12.5, false, Ui.Ink, new Thickness(0, 4, 8, 4)),
                    Ui.Text(f.Status == ClinicalSummary.Detected || f.Status == ClinicalSummary.NotDetected ? f.Certainty : "—", 12.5, false, Ui.Ink, new Thickness(0, 4, 0, 4))
                };
                for (int k = 0; k < cells.Length; k++) { Grid.SetRow(cells[k], row); Grid.SetColumn(cells[k], k); g.Children.Add(cells[k]); }
                row++;
            }
            sp.Children.Add(g);
            sp.Children.Add(Ui.Text("Confidence is the system's own measure for the finding. It supports the detection status and is not a measure of severity.", 11, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            return new Expander
            {
                Header = new TextBlock { Text = "Show confidence details", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Muted },
                IsExpanded = false, Margin = new Thickness(0, 2, 0, 0), Content = sp
            };
        }

        private static UIElement ReviewNotes(IEnumerable<string> reasons, double size = 12.5, string heading = null)
        {
            var list = reasons.Distinct().ToList();
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            if (list.Count == 0) return sp;
            sp.Children.Add(Ui.Text(heading ?? "Why a review is needed", 12, true, Ui.Brush("#8A5A00"), new Thickness(0, 4, 0, 3)));
            foreach (string r in list) sp.Children.Add(Ui.Text("•  " + r, size, false, Ui.Ink, new Thickness(6, 0, 0, 2)));
            return sp;
        }

        private static UIElement TechnicalDetails(V6SessionSummary s)
        {
            var sp = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
            sp.Children.Add(Ui.Text("Calibrated model probability for each condition, compared with its decision limit. For researchers; the clinical reading above already accounts for these numbers.",
                                    11, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            foreach (V6ViewSummary v in s.Views)
            {
                if (!s.Single) sp.Children.Add(Ui.Text(v.Label, 12, true, Ui.Ink, new Thickness(0, 4, 0, 2)));
                foreach (ConditionAssessment c in v.Assessment.Conditions)
                    sp.Children.Add(Ui.Text(c.Condition + ": " + V6Text.SupportText(c).Replace("AI support", "probability") + " · " + V6Text.DetectorText(c), 11.5, false, Ui.Ink, new Thickness(6, 0, 0, 1)));
                sp.Children.Add(Ui.Text("Model state: " + v.Assessment.State + " · variant " + v.Assessment.Variant + (v.Origin == AIResultStore.OriginReanalysis ? " · re-analyzed later from the saved image" : ""),
                                        11, false, Ui.Muted, new Thickness(6, 2, 0, 2)));
            }
            return new Expander
            {
                Header = new TextBlock { Text = "Technical details (for researchers)", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Muted },
                IsExpanded = false, Margin = new Thickness(0, 8, 0, 0), Content = sp
            };
        }

        private static UIElement Footer(V6SessionSummary s, string modelLabel)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (string n in s.Clinical.Notes) sp.Children.Add(Ui.Text(n, 12, false, Ui.Ink, new Thickness(0, 0, 0, 2)));
            foreach (string n in s.Notes) sp.Children.Add(Ui.Text(n, 12, false, Ui.Muted, new Thickness(0, 0, 0, 2)));
            foreach (string p in s.Problems) sp.Children.Add(Ui.Text(p, 12, false, Ui.Bad, new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(modelLabel)) sp.Children.Add(Ui.Text("Analyzed by: " + modelLabel, 11, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Ui.Text(Disclaimer, 11, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            return sp;
        }

        // ------------------------------------------------------------------ cards

        /// <summary>The result card at the top of the Results page.</summary>
        public static Border OverallCard(SessionDetail d, Dictionary<string, UnifiedAssessment> map, List<string> problems)
        {
            var folders = d.Views.Where(v => !string.IsNullOrEmpty(v.ViewType)).GroupBy(v => v.ViewType).ToDictionary(g => g.Key, g => g.First().RecordFolder);
            V6SessionSummary s = V6SessionSummary.From(map, d.Session.ScanMode == "Single", problems, folders);
            s.ReviewStatus = ReviewStatus.Normalise(d.Session.ReviewStatus);

            var sp = new StackPanel();
            var head = new DockPanel();
            Border chip = StatusChip(s.ReviewStatus); DockPanel.SetDock(chip, Dock.Right); chip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(chip);
            head.Children.Add(Ui.Text("Screening result (AI-generated)", 16, true, Ui.Ink, new Thickness(0, 2, 0, 0)));
            sp.Children.Add(head);
            sp.Children.Add(OutcomeBanner(s.Clinical));
            sp.Children.Add(FindingsTable(s.Clinical));
            sp.Children.Add(ConfidenceDetails(s.Clinical));
            sp.Children.Add(ReviewNotes(s.Clinical.ReviewReasons, 12.5, s.Clinical.Category == ClinicalCategory.Retake ? "What to do" : null));
            sp.Children.Add(DermatologistPanel(d));

            // everything else is one click away, so the first screen shows the outcome, the findings and the review state
            var more = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
            if (!s.Single)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                chips.Children.Add(Ui.Text("Images analyzed:", 12, true, Ui.Ink, new Thickness(0, 3, 8, 0)));
                foreach (string vw in StudyText.Views)
                {
                    bool ok = map.ContainsKey(vw);
                    chips.Children.Add(Ui.Pill((ok ? "✓ " : "✗ ") + V6SessionSummary.LabelFor(vw), ok ? Ui.Good : Ui.Bad, ok ? "#EEF6EC" : "#FDECEC"));
                }
                more.Children.Add(chips);
            }
            more.Children.Add(Footer(s, null));
            more.Children.Add(QualityAndLimits(d, s));
            more.Children.Add(ProbabilityBars(s));
            sp.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "More details", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink },
                IsExpanded = false, Margin = new Thickness(0, 12, 0, 0), Content = more
            });
            return Ui.Card(sp);
        }

        /// <summary>Optional (closed by default): one bar per condition showing the model's probability, with the decision limit marked.</summary>
        public static UIElement ProbabilityBars(V6SessionSummary s)
        {
            var sp = new StackPanel { Margin = new Thickness(4, 8, 0, 0) };
            sp.Children.Add(Ui.Text("Each bar shows how strongly the AI result points to that condition. The thin line marks the level at which a condition is reported as detected. A longer bar does not indicate a more severe condition.",
                                    11.5, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            foreach (V6ViewSummary v in s.Views)
            {
                if (!s.Single) sp.Children.Add(Ui.Text(v.Label, 12, true, Ui.Ink, new Thickness(0, 6, 0, 3)));
                foreach (ConditionAssessment c in v.Assessment.Conditions)
                {
                    int idx = V6Conditions.IndexOf(c.Condition);
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                    var name = Ui.Text(c.Condition, 12.5, true, idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink, new Thickness(0, 0, 8, 0));
                    name.VerticalAlignment = VerticalAlignment.Center;
                    double p = Math.Max(0, Math.Min(1, c.CalibratedProbability)), t = Math.Max(0, Math.Min(1, c.Threshold));
                    var track = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center };
                    track.Children.Add(new Border { Background = Ui.Brush("#EFEADC"), CornerRadius = new CornerRadius(7) });
                    var fill = new Grid();
                    fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(p, 0.0001), GridUnitType.Star) });
                    fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - p, 0.0001), GridUnitType.Star) });
                    fill.Children.Add(new Border { Background = Ui.Brush(idx >= 0 ? ClassPalette.Hex[idx] : "#818263"), CornerRadius = new CornerRadius(7) });
                    track.Children.Add(fill);
                    var mark = new Grid();
                    mark.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(t, 0.0001), GridUnitType.Star) });
                    mark.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - t, 0.0001), GridUnitType.Star) });
                    mark.Children.Add(new Border { Background = Ui.Ink, Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, 0, -2) });
                    track.Children.Add(mark);
                    Grid.SetColumn(track, 1);
                    var pct = Ui.Text(c.CalibratedProbability.ToString("0%") + "  (line at " + c.Threshold.ToString("0%") + ")", 12, false, Ui.Ink, new Thickness(10, 0, 0, 0));
                    pct.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(pct, 2);
                    row.Children.Add(name); row.Children.Add(track); row.Children.Add(pct);
                    sp.Children.Add(row);
                }
            }
            return new Expander
            {
                Header = new TextBlock { Text = "Show the AI's reading as bars (optional)", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink },
                IsExpanded = false, Margin = new Thickness(0, 10, 0, 0), Content = sp
            };
        }

        /// <summary>Global model, lesion detector and fusion side by side for each condition. Only numbers the system really holds are shown.</summary>
        public static UIElement AnalysisBreakdown(V6SessionSummary s)
        {
            var sp = new StackPanel { Margin = new Thickness(4, 6, 0, 0) };
            sp.Children.Add(Ui.Text("Global = whole-face model (MobileNetV2). Localized = lesion detector (YOLOv8) boxes. Fusion = the final decision that combines both. The global figure is the calibrated global-only reading, not a raw model output.",
                                    11, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            foreach (V6ViewSummary v in s.Views)
            {
                if (!s.Single) sp.Children.Add(Ui.Text(v.Label, 12, true, Ui.Ink, new Thickness(0, 6, 0, 2)));
                var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                double[] w = { 130, 120, 220, 220 };
                foreach (double x in w) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(x) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string[] head = { "Condition", "Global (whole face)", "Localized (lesion detector)", "Fusion (final)", "Result" };
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < head.Length; c++) { var t = Ui.Text(head[c], 11, true, Ui.Muted, new Thickness(0, 0, 8, 4)); Grid.SetColumn(t, c); g.Children.Add(t); }
                int row = 1;
                foreach (ConditionAssessment c in v.Assessment.Conditions)
                {
                    ClinicalFinding f = v.Clinical == null ? null : v.Clinical.Findings.FirstOrDefault(x => x.Condition == c.Condition);
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    string global = c.GlobalOnlyProbability > 0 || c.CalibratedProbability > 0 ? c.GlobalOnlyProbability.ToString("0%") + " probability" : "not available";
                    string local = !c.DetectorAvailable ? "No detector for this condition"
                                 : f != null && f.BoxCount > 0 ? f.BoxCount + (f.BoxCount == 1 ? " area marked" : " areas marked") + ", best confidence " + f.BestBoxConfidence.ToString("0%")
                                 : "No area marked";
                    string fusion = c.CalibratedProbability.ToString("0%") + " (limit " + c.Threshold.ToString("0%") + ")";
                    string res = f != null ? f.Status + (f.ByDetector ? " · by lesion detector" : "") : "—";
                    string[] cells = { c.Condition, global, local, fusion, res };
                    for (int k = 0; k < cells.Length; k++)
                    {
                        var t = Ui.Text(cells[k], 12, k == 0 || k == 4, Ui.Ink, new Thickness(0, 4, 8, 4));
                        Grid.SetRow(t, row); Grid.SetColumn(t, k); g.Children.Add(t);
                    }
                    row++;
                }
                sp.Children.Add(g);
                sp.Children.Add(Ui.Text("Explainability maps (Grad-CAM) are not produced by this model; the boxes are the only visual evidence.", 11, false, Ui.Muted, new Thickness(0, 0, 0, 2)));
            }
            return new Expander
            {
                Header = new TextBlock { Text = "How the AI reached this reading (global, localized and fusion)", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink },
                IsExpanded = true, Margin = new Thickness(0, 10, 0, 0), Content = sp
            };
        }

        private static string FlagText(string f)
        {
            if (f.StartsWith("cnn_yolo_conflict_")) return "The findings for " + f.Substring(18) + " are inconclusive.";
            if (f.StartsWith("near_threshold_")) return "The result for " + f.Substring(15) + " is close to the reporting limit.";
            if (f == "ood") return "The image differs from those the system was trained on.";
            return f.Replace('_', ' ');
        }

        /// <summary>Image quality as measured when each photograph was captured, plus the limits of this screening. Nothing is shown for a measure that was not taken.</summary>
        public static UIElement QualityAndLimits(SessionDetail d, V6SessionSummary s)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            sp.Children.Add(Ui.Text("Image quality and limitations", 13, true, Ui.Ink, new Thickness(0, 0, 0, 3)));
            foreach (CaptureViewRow v in d.Views.OrderBy(x => Array.IndexOf(StudyText.Views, x.ViewType)))
            {
                string q = string.IsNullOrWhiteSpace(v.QualityStatus) ? "Not evaluated" : ClinicalText.PlainQuality(v.QualityStatus);
                sp.Children.Add(Ui.Text("•  " + (string.IsNullOrEmpty(v.ViewType) ? "Image" : V6SessionSummary.LabelFor(v.ViewType)) + ": " + q, 12, false, Ui.Ink, new Thickness(6, 0, 0, 1)));
            }
            foreach (string r in s.Views.SelectMany(x => x.Assessment.Flags).Distinct().Take(6))
                sp.Children.Add(Ui.Text("•  Note: " + FlagText(r), 11.5, false, Ui.Muted, new Thickness(6, 0, 0, 1)));
            sp.Children.Add(Ui.Text("Limitations: facial regions are estimated from the detected face area, severity is not graded, and the screening covers only acne, hyperpigmentation and eczema.", 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            return sp;
        }

        /// <summary>A separate, clearly labelled block: what the dermatologist recorded (or that nothing has been recorded yet). Never mixed into the AI result.</summary>
        public static UIElement DermatologistPanel(SessionDetail d)
        {
            DermatologistValidation v = d.Validations.FirstOrDefault(x => x.ValidationStatus != ReviewStatus.ValidationDraft);
            string status = ReviewStatus.Normalise(d.Session.ReviewStatus);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Dermatologist assessment · recorded separately by a person", 13, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            if (v == null)
                sp.Children.Add(Ui.Text("Not yet reviewed. Everything above is the AI result.", 12.5, false, Ui.Muted, new Thickness(0)));
            else
            {
                string who = string.IsNullOrWhiteSpace(v.ValidatorName) ? v.DermatologistID : v.ValidatorName + " (" + v.DermatologistID + ")";
                sp.Children.Add(Ui.InfoRows(new[]
                {
                    Ui.KV("Outcome", ReviewStatus.FromValidation(v.ValidationStatus) ?? status),
                    Ui.KV("Reviewed by", who + " · " + Fmt.When(v.ValidationDate)),
                    Ui.KV("Assessment", string.IsNullOrWhiteSpace(v.DermatologistAssessment) ? "Not recorded" : v.DermatologistAssessment),
                    Ui.KV("Agreement with the AI", string.IsNullOrWhiteSpace(v.AgreementWithAI) ? "Not recorded" : v.AgreementWithAI == "Uncertain" ? "Unable to determine" : v.AgreementWithAI)
                }, 150, 12.5));
            }
            var conds = StudyRepository.LatestConditionReviews(d.Session.ScanSessionID);
            if (v != null && conds.Count > 0)
            {
                sp.Children.Add(Ui.Text("Verification of each condition", 12, true, Ui.Muted, new Thickness(0, 8, 0, 2)));
                foreach (ConditionReview cr in conds.Values.OrderBy(x => Array.IndexOf(V6Conditions.Names, x.Condition)))
                    sp.Children.Add(Ui.Text("•  " + cr.Condition + ": " + cr.Status + (string.IsNullOrWhiteSpace(cr.Observation) ? "" : " — " + cr.Observation), 12.5, false, Ui.Ink, new Thickness(6, 0, 0, 1)));
            }
            return new Border
            {
                Background = Ui.Brush("#F7F5EC"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0), Child = sp
            };
        }

        /// <summary>Colour key for the boxes on the marked-areas picture: one colour per condition.</summary>
        public static UIElement BoxLegend()
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            w.Children.Add(Ui.Text("Box color:", 11, true, Ui.Muted, new Thickness(0, 1, 8, 0)));
            for (int i = 0; i < V6Conditions.Count; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                item.Children.Add(new System.Windows.Shapes.Rectangle { Width = 14, Height = 10, Stroke = Ui.Brush(ClassPalette.Hex[i]), StrokeThickness = 2, Margin = new Thickness(0, 3, 6, 0) });
                item.Children.Add(Ui.Text(V6Conditions.Names[i], 11, false, Ui.Ink, new Thickness(0)));
                w.Children.Add(item);
            }
            return w;
        }

        /// <summary>The compact reading under one photograph on the Results page, in plain words.</summary>
        public static UIElement ViewColumn(UnifiedAssessment a, string recordFolder = null)
        {
            double[] face = null;
            try { face = recordFolder != null && V6SessionSummary.FaceBoxProvider != null ? V6SessionSummary.FaceBoxProvider(recordFolder) : null; } catch { }
            ClinicalSummary c = ClinicalSummary.From(a, face);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(c.Title, 13, true, Ui.Brush(CategoryInk(c.Category)), new Thickness(0, 8, 0, 4)));
            if (c.Category == ClinicalCategory.Retake)
            {
                sp.Children.Add(Ui.Text("This image is not clear enough to interpret. Please retake it.", 12, false, Ui.Ink, new Thickness(0, 0, 0, 2)));
                return sp;
            }
            foreach (ClinicalFinding f in c.Findings)
            {
                string word = f.Status == ClinicalSummary.Detected ? "found" : f.Status == ClinicalSummary.NotDetected ? "not found" : "possible; review recommended";
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) };
                int idx = V6Conditions.IndexOf(f.Condition);
                line.Inlines.Add(new System.Windows.Documents.Run(f.Condition + ": ") { FontWeight = FontWeights.SemiBold, Foreground = idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink });
                line.Inlines.Add(new System.Windows.Documents.Run(word) { Foreground = Ui.Ink });
                sp.Children.Add(line);
            }
            if (c.MarkedAreas > 0)
                sp.Children.Add(Ui.Text(c.MarkedAreas + (c.MarkedAreas == 1 ? " area marked on the image." : " areas marked on the image."), 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            if (c.Category == ClinicalCategory.NeedsReview || c.Findings.Any(x => x.Status == ClinicalSummary.Possible || x.Status == ClinicalSummary.Inconclusive))
                sp.Children.Add(Ui.Text("Dermatologist review is recommended for this image.", 11.5, false, Ui.Brush("#8A5A00"), new Thickness(0, 2, 0, 0)));
            return sp;
        }

        /// <summary>The clinical summary of a whole scan for the patient record, dermatologist review and researcher windows.</summary>
        public static StackPanel SummaryPanel(V6SessionSummary s)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            Border chip = StatusChip(s.ReviewStatus); DockPanel.SetDock(chip, Dock.Right); chip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(chip);
            head.Children.Add(Ui.Text("Screening result (AI-generated)", 14, true, Ui.Ink, new Thickness(0, 2, 0, 0)));
            sp.Children.Add(head);
            sp.Children.Add(OutcomeBanner(s.Clinical));
            sp.Children.Add(FindingsList(s.Clinical));
            sp.Children.Add(ReviewNotes(s.Clinical.ReviewReasons, 12, s.Clinical.Category == ClinicalCategory.Retake ? "What to do" : null));
            sp.Children.Add(Footer(s, null));
            return sp;
        }
    }
}
