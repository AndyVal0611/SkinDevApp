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
            Child = new TextBlock { Text = f.Status == ClinicalSummary.Possible || f.Status == ClinicalSummary.Inconclusive ? f.Status + " · review" : f.ByDetector ? "Detected · detector" : f.Status,
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
            s.Category == ClinicalCategory.Retake ? "No condition is reported for this photograph because it has to be retaken."
            : "No condition reading is available for this photograph.";

        /// <summary>Condition · AI result · Certainty · Facial regions.</summary>
        public static Grid FindingsTable(ClinicalSummary s, double fontSize = 13)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            if (s.Findings.Count == 0) { g.Children.Add(Ui.Text(NoFindingsText(s), 13, false, Ui.Muted, new Thickness(0, 2, 0, 4))); return g; }
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] head = { "Condition", "Detection status", "Confidence", "Certainty", "Facial regions" };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < head.Length; c++)
            {
                var t = Ui.Text(head[c], 11, true, Ui.Muted, new Thickness(0, 0, 8, 4)); Grid.SetColumn(t, c); g.Children.Add(t);
            }
            var rule = new Border { Height = 1, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumnSpan(rule, 5); g.Children.Add(rule);
            int row = 1;
            foreach (ClinicalFinding f in s.Findings)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int idx = V6Conditions.IndexOf(f.Condition);
                var name = Ui.Text(f.Condition, fontSize, true, idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink, new Thickness(0, 7, 8, 7));
                Border result = ResultPill(f); result.Margin = new Thickness(0, 5, 8, 5);
                var cert = Ui.Text(f.Status == ClinicalSummary.Detected || f.Status == ClinicalSummary.NotDetected ? f.Certainty : "—", fontSize, false, Ui.Ink, new Thickness(0, 7, 8, 7));
                var regions = Ui.Text(RegionText(f), fontSize, false, Ui.Ink, new Thickness(0, 7, 0, 7));
                var conf = Ui.Text(ClinicalSummary.ConfidenceText(f), fontSize, true, Ui.Ink, new Thickness(0, 7, 8, 7));
                Grid.SetRow(name, row); Grid.SetRow(result, row); Grid.SetRow(conf, row); Grid.SetRow(cert, row); Grid.SetRow(regions, row);
                Grid.SetColumn(result, 1); Grid.SetColumn(conf, 2); Grid.SetColumn(cert, 3); Grid.SetColumn(regions, 4);
                g.Children.Add(name); g.Children.Add(result); g.Children.Add(conf); g.Children.Add(cert); g.Children.Add(regions);
                if (row < s.Findings.Count)
                {
                    var r2 = new Border { Height = 1, Background = Ui.Brush("#EFEADC"), VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetRow(r2, row); Grid.SetColumnSpan(r2, 5); g.Children.Add(r2);
                }
                row++;
            }
            return g;
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
                sp.Children.Add(Ui.Text("Model state: " + v.Assessment.State + " · variant " + v.Assessment.Variant + (v.Origin == AIResultStore.OriginReanalysis ? " · re-analyzed later from the saved photograph" : ""),
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
            head.Children.Add(Ui.Text("AI screening result · generated by the system", 16, true, Ui.Ink, new Thickness(0, 2, 0, 0)));
            sp.Children.Add(head);
            sp.Children.Add(OutcomeBanner(s.Clinical));
            sp.Children.Add(Ui.Text("Summary", 12, true, Ui.Muted, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Ui.Text(ClinicalSummary.SummaryLine(s.Clinical), 13, false, Ui.Ink, new Thickness(0, 0, 0, 8)));
            sp.Children.Add(FindingsTable(s.Clinical));
            sp.Children.Add(ReviewNotes(s.Clinical.ReviewReasons, 12.5, s.Clinical.Category == ClinicalCategory.Retake ? "What to do" : null));
            if (!s.Single)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
                chips.Children.Add(Ui.Text("Photographs analyzed:", 12, true, Ui.Ink, new Thickness(0, 3, 8, 0)));
                foreach (string vw in StudyText.Views)
                {
                    bool ok = map.ContainsKey(vw);
                    chips.Children.Add(Ui.Pill((ok ? "✓ " : "✗ ") + V6SessionSummary.LabelFor(vw), ok ? Ui.Good : Ui.Bad, ok ? "#EEF6EC" : "#FDECEC"));
                }
                sp.Children.Add(chips);
            }
            sp.Children.Add(Footer(s, d.Session.AIModel));
            sp.Children.Add(DermatologistPanel(d));
            sp.Children.Add(TechnicalDetails(s));
            return Ui.Card(sp);
        }

        /// <summary>A separate, clearly labelled block: what the dermatologist recorded (or that nothing has been recorded yet). Never mixed into the AI result.</summary>
        public static UIElement DermatologistPanel(SessionDetail d)
        {
            DermatologistValidation v = d.Validations.FirstOrDefault(x => x.ValidationStatus != ReviewStatus.ValidationDraft);
            string status = ReviewStatus.Normalise(d.Session.ReviewStatus);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Dermatologist assessment · recorded separately by a person", 13, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            if (v == null)
                sp.Children.Add(Ui.Text("Not reviewed yet. Everything above is the AI's result only. It is not a dermatologist's assessment and not a diagnosis.", 12.5, false, Ui.Muted, new Thickness(0)));
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
            w.Children.Add(Ui.Text("Box colour:", 11, true, Ui.Muted, new Thickness(0, 1, 8, 0)));
            for (int i = 0; i < V6Conditions.Count; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                item.Children.Add(new System.Windows.Shapes.Rectangle { Width = 14, Height = 10, Stroke = Ui.Brush(ClassPalette.Hex[i]), StrokeThickness = 2, Margin = new Thickness(0, 3, 6, 0) });
                item.Children.Add(Ui.Text(V6Conditions.Names[i], 11, false, Ui.Ink, new Thickness(0)));
                w.Children.Add(item);
            }
            return w;
        }

        /// <summary>The compact reading under one photograph on the Results page.</summary>
        public static UIElement ViewColumn(UnifiedAssessment a, string recordFolder = null)
        {
            double[] face = null;
            try { face = recordFolder != null && V6SessionSummary.FaceBoxProvider != null ? V6SessionSummary.FaceBoxProvider(recordFolder) : null; } catch { }
            ClinicalSummary c = ClinicalSummary.From(a, face);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(c.Title, 13, true, Ui.Brush(CategoryInk(c.Category)), new Thickness(0, 8, 0, 4)));
            foreach (ClinicalFinding f in c.Findings)
            {
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) };
                int idx = V6Conditions.IndexOf(f.Condition);
                line.Inlines.Add(new System.Windows.Documents.Run(f.Condition + ": ") { FontWeight = FontWeights.SemiBold, Foreground = idx >= 0 ? Ui.Brush(ClassPalette.Hex[idx]) : Ui.Ink });
                line.Inlines.Add(new System.Windows.Documents.Run(ClinicalSummary.ConfidenceText(f) + " · " + ClinicalSummary.FindingLine(f)) { Foreground = Ui.Ink });
                sp.Children.Add(line);
            }
            sp.Children.Add(Ui.Text(c.MarkedAreas == 0 ? "No areas marked" : c.MarkedAreas + (c.MarkedAreas == 1 ? " area marked: " : " areas marked: ") +
                                    string.Join(", ", c.Findings.Where(x => x.BoxCount > 0).Select(x => x.Condition + " " + x.BoxCount)), 11, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            foreach (string r in c.ReviewReasons.Distinct().Take(3)) sp.Children.Add(Ui.Text("•  " + r, 11, false, Ui.Brush("#8A5A00"), new Thickness(0, 2, 0, 0)));
            foreach (string n in c.Notes) sp.Children.Add(Ui.Text(n, 11, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            return sp;
        }

        /// <summary>The clinical summary of a whole scan for the patient record, dermatologist review and researcher windows.</summary>
        public static StackPanel SummaryPanel(V6SessionSummary s)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            Border chip = StatusChip(s.ReviewStatus); DockPanel.SetDock(chip, Dock.Right); chip.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(chip);
            head.Children.Add(Ui.Text("AI screening result", 14, true, Ui.Ink, new Thickness(0, 2, 0, 0)));
            sp.Children.Add(head);
            sp.Children.Add(OutcomeBanner(s.Clinical));
            sp.Children.Add(FindingsList(s.Clinical));
            sp.Children.Add(ReviewNotes(s.Clinical.ReviewReasons, 12, s.Clinical.Category == ClinicalCategory.Retake ? "What to do" : null));
            sp.Children.Add(Footer(s, null));
            sp.Children.Add(TechnicalDetails(s));
            return sp;
        }
    }
}
