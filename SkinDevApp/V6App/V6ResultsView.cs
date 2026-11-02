// ============================================================================
// V6ResultsView.cs  -  namespace SkinDevApp.V6   (WPF presentation; used when the V6 flag is on)
//
// ONE unified assessment replaces the separate "classifier" and "lesion detector" cards, on the Results page and in the windows that used to show
// the V5 classifier (dermatologist review, researcher review, patient record). It only READS what was saved at scan time
// (v6_assessment.json next to record.json); nothing is re-run. The V5 result is not shown as a competing diagnosis here (it stays saved for
// baseline comparison and rollback). Wording lives in V6Text (shared with receipts and the A4 report).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Imaging;
using SkinDevApp.Views;

namespace SkinDevApp.V6
{
    public static class V6ResultsView
    {
        public const string Disclaimer = V6Text.Disclaimer;

        /// <summary>Saved V6 assessments by view type (Single / Front / Left / Right). Null when the flag is off or this scan has none (older scans keep showing what produced them).</summary>
        public static Dictionary<string, UnifiedAssessment> Load(SessionDetail d, out List<string> problems) => V6SessionLoader.Load(d, out problems);

        public static string MarkedAreasPath(CaptureViewRow v) => V6SessionLoader.MarkedAreasPath(v);

        public static string StateHeadline(UnifiedAssessment a) => V6Text.StateHeadline(a);
        public static IEnumerable<string> FlagLines(UnifiedAssessment a) => V6Text.FlagLines(a);

        /// <summary>A verdict pill of one fixed width, so Present / Not found / Uncertain line up in a column.</summary>
        private static FrameworkElement VerdictPill(ConditionAssessment c, int idx)
        {
            Border pill = Ui.Pill(V6Text.VerdictText(c), VerdictBrush(c, idx), "#F6F3EA");
            pill.MinWidth = 86; pill.HorizontalAlignment = HorizontalAlignment.Left;
            if (pill.Child is TextBlock tb) tb.HorizontalAlignment = HorizontalAlignment.Center;
            return pill;
        }

        private static System.Windows.Media.Brush StateBrush(UnifiedAssessment a) =>
            a.State == V6States.FindingsSupported || a.State == V6States.MultipleFindingsSupported ? Ui.Ink :
            a.State == V6States.NoFindingsSupported ? Ui.Good :
            a.State == V6States.InferenceError ? Ui.Bad : Ui.Warn;

        private static System.Windows.Media.Brush VerdictBrush(ConditionAssessment c, int idx) =>
            c.Verdict == "present" ? Ui.Brush(ClassPalette.Hex[idx]) : c.Verdict == "absent" ? Ui.Good : Ui.Warn;

        // ------------------------------------------------------------------ cards
        public static Border OverallCard(SessionDetail d, Dictionary<string, UnifiedAssessment> map, List<string> problems)
        {
            var sp = new StackPanel();
            var head = new WrapPanel();
            head.Children.Add(Ui.Text("Unified AI assessment", 16, true, null, new Thickness(0, 0, 10, 0)));
            sp.Children.Add(head);
            sp.Children.Add(Ui.Text(d.Session.ScanMode == "Single" ? "One result for this photograph, from one trained model that combines the whole-photograph model and the lesion detectors."
                                                                    : "One result per photograph, each from one trained model that combines the whole-photograph model and the lesion detectors. The photographs are not merged into a single verdict.", 12, false, Ui.Muted, new Thickness(0, 2, 0, 8)));

            if (map.Count == 1 || d.Session.ScanMode == "Single") sp.Children.Add(Body(map.Values.First()));
            else
            {
                string[] order = StudyText.Views.Where(map.ContainsKey).ToArray();
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                foreach (string _ in order) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int r = 0; r <= V6Conditions.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int j = 0; j < order.Length; j++) { var t = Ui.Text(order[j] + " view", 12.5, true); Grid.SetColumn(t, j + 1); grid.Children.Add(t); }
                for (int i = 0; i < V6Conditions.Count; i++)
                {
                    var name = Ui.Text(V6Conditions.Names[i], 13, true, Ui.Brush(ClassPalette.Hex[i]), new Thickness(0, 4, 0, 4)); Grid.SetRow(name, i + 1); grid.Children.Add(name);
                    for (int j = 0; j < order.Length; j++)
                    {
                        ConditionAssessment c = map[order[j]].Conditions.Length > i ? map[order[j]].Conditions[i] : null;
                        FrameworkElement cell = c == null ? Ui.Text("-", 12.5) : VerdictPill(c, i);
                        cell.HorizontalAlignment = HorizontalAlignment.Left; cell.Margin = new Thickness(0, 3, 0, 3);
                        Grid.SetRow(cell, i + 1); Grid.SetColumn(cell, j + 1); grid.Children.Add(cell);
                    }
                }
                sp.Children.Add(grid);

                // as in V5: which of the three photographs were analysed
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
                chips.Children.Add(Ui.Text("Photographs analysed:  ", 12.5, true, Ui.Ink, new Thickness(0, 3, 0, 0)));
                foreach (string vw in StudyText.Views)
                {
                    bool ok = map.ContainsKey(vw);
                    chips.Children.Add(Ui.Pill((ok ? "✓ " : "✗ ") + vw, ok ? Ui.Good : Ui.Bad, ok ? "#EEF6EC" : "#FDECEC"));
                }
                sp.Children.Add(chips);
                int review = map.Values.Count(a => a.ReviewRequired);
                if (review > 0) sp.Children.Add(Ui.Callout("Review recommended", review + " of " + map.Count + " photographs need a closer look (see each photograph below).", 1));
                if (map.Values.Select(a => string.Join("|", a.PresentConditions.OrderBy(x => x))).Distinct().Count() > 1)
                    sp.Children.Add(Ui.Text("The photographs do not all show the same findings. Each photograph is reported on its own below.", 11.5, false, Ui.Warn, new Thickness(0, 4, 0, 0)));
                else if (map.Count > 1)
                    sp.Children.Add(Ui.Text("The analysed photographs show the same findings.", 11.5, false, Ui.Good, new Thickness(0, 4, 0, 0)));
            }
            foreach (string p in problems) sp.Children.Add(Ui.Text(p, 11, false, Ui.Bad, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Ui.Text(Disclaimer, 10.5, false, Ui.Muted, new Thickness(0, 8, 0, 0)));
            return Ui.Card(sp);
        }

        /// <summary>Headline, the three conditions and why the result is uncertain (if it is).</summary>
        private static UIElement Body(UnifiedAssessment a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(StateHeadline(a), 15, true, StateBrush(a), new Thickness(0, 0, 0, 6)));
            for (int i = 0; i < a.Conditions.Length; i++)
            {
                ConditionAssessment c = a.Conditions[i];
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var name = Ui.Text(c.Condition, 13.5, true, Ui.Brush(ClassPalette.Hex[i]), new Thickness(0, 2, 0, 0)); Grid.SetColumn(name, 0); row.Children.Add(name);
                FrameworkElement pill = VerdictPill(c, i); Grid.SetColumn(pill, 1); row.Children.Add(pill);
                var detail = new StackPanel();
                var bar = new Grid { Height = 7, Margin = new Thickness(0, 6, 0, 3) };
                double p = Math.Max(0.001, Math.Min(0.999, c.CalibratedProbability));
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p, GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - p, GridUnitType.Star) });
                var fill = new Border { Background = Ui.Brush(ClassPalette.Hex[i]), CornerRadius = new CornerRadius(3) }; Grid.SetColumn(fill, 0); bar.Children.Add(fill);
                var track = new Border { Background = Ui.Brush("#E7E2D3"), CornerRadius = new CornerRadius(3) }; Grid.SetColumn(track, 1); bar.Children.Add(track);
                detail.Children.Add(bar);
                detail.Children.Add(Ui.Text(V6Text.SupportText(c) + "  ·  " + V6Text.DetectorText(c), 11, false, Ui.Muted, new Thickness(0)));
                Grid.SetColumn(detail, 2); row.Children.Add(detail);
                sp.Children.Add(row);
            }
            foreach (string line in FlagLines(a).Distinct())
                sp.Children.Add(Ui.Text("• " + line, 11.5, false, Ui.Warn, new Thickness(0, 2, 0, 0)));
            return sp;
        }

        /// <summary>The compact reading under one photograph in the per-photograph card.</summary>
        public static UIElement ViewColumn(UnifiedAssessment a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(StateHeadline(a), 12.5, true, StateBrush(a), new Thickness(0, 6, 0, 4)));
            for (int i = 0; i < a.Conditions.Length; i++)
            {
                ConditionAssessment c = a.Conditions[i];
                var line = new WrapPanel();
                line.Children.Add(Ui.Text(c.Condition + ": ", 11.5, true, Ui.Brush(ClassPalette.Hex[i]), new Thickness(0)));
                line.Children.Add(Ui.Text(V6Text.VerdictText(c) + "  (support " + V6Text.Pct(c.CalibratedProbability) + ")", 11.5, false, Ui.Ink, new Thickness(0)));
                sp.Children.Add(line);
            }
            sp.Children.Add(Ui.Text(V6Text.BoxesLine(a), 10.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            foreach (string line in FlagLines(a).Distinct().Take(3))
                sp.Children.Add(Ui.Text("• " + line, 10.5, false, Ui.Warn, new Thickness(0, 2, 0, 0)));
            return sp;
        }

        /// <summary>Text-only summary of a whole scan for the windows that used to show the V5 result (dermatologist review, researcher review, patient record).</summary>
        public static StackPanel SummaryPanel(V6SessionSummary s)
        {
            var sp = new StackPanel();
            var head = new WrapPanel();
            head.Children.Add(Ui.Text("Unified AI assessment", 13, true, Ui.Ink, new Thickness(0, 0, 8, 0)));
            sp.Children.Add(head);
            sp.Children.Add(Ui.Text(s.Headline, 15, true, s.Single ? StateBrush(s.Views[0].Assessment) : (s.ReviewRecommended ? Ui.Warn : Ui.Ink), new Thickness(0, 4, 0, 6)));
            foreach (V6ViewSummary v in s.Views)
            {
                if (!s.Single) sp.Children.Add(Ui.Text(v.Label + ": " + v.Headline, 12.5, true, StateBrush(v.Assessment), new Thickness(0, 6, 0, 2)));
                for (int i = 0; i < v.Assessment.Conditions.Length; i++)
                {
                    ConditionAssessment c = v.Assessment.Conditions[i];
                    var line = new WrapPanel();
                    line.Children.Add(Ui.Text(c.Condition + ": ", 12, true, Ui.Brush(ClassPalette.Hex[i]), new Thickness(0)));
                    line.Children.Add(Ui.Text(V6Text.VerdictText(c) + "  (" + V6Text.SupportText(c) + "; " + V6Text.DetectorText(c) + ")", 12, false, Ui.Ink, new Thickness(0)));
                    sp.Children.Add(line);
                }
                sp.Children.Add(Ui.Text(v.BoxesLine, 11, false, Ui.Muted, new Thickness(0, 3, 0, 0)));
                foreach (string f in v.FlagLines) sp.Children.Add(Ui.Text("• " + f, 11.5, false, Ui.Warn, new Thickness(0, 2, 0, 0)));
            }
            foreach (string n in s.Notes) sp.Children.Add(Ui.Text(n, 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            foreach (string p in s.Problems) sp.Children.Add(Ui.Text(p, 11, false, Ui.Bad, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Ui.Text(s.Disclaimer, 10.5, false, Ui.Muted, new Thickness(0, 8, 0, 0)));
            return sp;
        }
    }
}
