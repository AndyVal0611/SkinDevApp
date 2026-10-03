// ResearchDashboardPage.xaml.cs - research summaries computed only from stored data.
// Each chart is one series of counts: single-hue horizontal bars, sorted, with the
// count and share written next to the bar (so the chart doubles as its own table).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class ResearchDashboardPage : Page
    {
        private static readonly System.Windows.Media.Brush BarBrush = Ui.Brush("#3A5A4C");
        private static readonly System.Windows.Media.Brush TrackBrush = Ui.Brush("#F1EFEC");

        public ResearchDashboardPage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Research Dashboard");
            if (!AppSession.IsResearcher)
            {
                StatGrid.Children.Add(Ui.Card(Ui.Text("The research dashboard is available to signed-in researchers / administrators only.", 14, false, Ui.Bad)));
                return;
            }
            Loaded += (s, e) => Render();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AppSession.IsResearcher) Render();
        }

        private void Render()
        {
            StatGrid.Children.Clear();
            ChartGrid.Children.Clear();
            try
            {
                Stat("Total participants", StudyRepository.TotalParticipants());
                Stat("Total scan sessions", StudyRepository.TotalSessions());
                Stat("Captured images", StudyRepository.TotalCapturedImages(), "original views (each also has an analysed copy)");
                Stat("Grad-CAM++ maps", StudyRepository.TotalAttributionMaps());
                Stat("Completed analyses", StudyRepository.CompletedAnalyses(), "sessions with every expected view");
                Stat("Pending researcher verification", StudyRepository.PendingVerificationCount());
                Stat("Pending dermatologist validation", StudyRepository.PendingDermatologistCount());
                Stat("Model versions seen", StudyRepository.AllModelVersions().Count);
                Stat("Scans with consent for future model use", StudyRepository.SessionsWithFutureUseConsent(), "latest consent ticked the optional permission");
                int withdrawn = StudyRepository.WithdrawnParticipants();
                Stat("Withdrawn participants", withdrawn, "excluded from every figure on this page");

                Chart("AI classification distribution", "Overall AI class per scan session", StudyRepository.AiClassDistribution());
                Chart("Researcher reference distribution", "Latest researcher assessment per session", StudyRepository.ResearcherDistribution());
                Chart("Dermatologist reference distribution", "Latest completed validation per session", StudyRepository.DermatologistDistribution());
                Chart("AI vs researcher agreement", "Overall AI class vs latest researcher label", StudyRepository.AiVsResearcherAgreement());
                Chart("AI vs dermatologist agreement", "Overall AI class vs latest completed validation", StudyRepository.AiVsDermatologistAgreement());
                Chart("View completion rate", "Views captured per scan session", StudyRepository.ViewCompletion());
                Chart("Age distribution", "Participants by age group", StudyRepository.AgeDistribution(), sortByCount: false);
                Chart("Sex distribution", "Participants", StudyRepository.SexDistribution());
                Chart("Fitzpatrick distribution (manual / self-reported)", "Collected at registration; not AI-derived", StudyRepository.FitzpatrickManualDistribution(), sortByCount: false);
                Chart("Model-version distribution", "Scan sessions per model (run / ONNX hash)", StudyRepository.ModelVersionDistribution());

                var reserved = new StackPanel();
                reserved.Children.Add(Ui.Text("Subgroup analysis by AI Fitzpatrick / skin-tone category", 15, true));
                reserved.Children.Add(Ui.Text("Reserved. " + StudyText.FitzpatrickAiDetail +
                    " AI-derived skin-tone statistics will appear here only after a validated model is integrated.", 12, false, Ui.Muted));
                ChartGrid.Children.Add(Ui.Card(reserved));
            }
            catch (Exception ex)
            {
                StatGrid.Children.Add(Ui.Card(Ui.Text("Could not read the database: " + ex.Message, 13, false, Ui.Bad)));
            }
        }

        private void Stat(string label, int value, string note = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(label, 12, true, Ui.Muted, new Thickness(0)));
            sp.Children.Add(Ui.Text(value.ToString("N0"), 28, true, Ui.Ink, new Thickness(0, 4, 0, 0)));
            if (note != null) sp.Children.Add(Ui.Text(note, 10.5, false, Ui.Muted, new Thickness(0)));
            StatGrid.Children.Add(Ui.Card(sp));
        }

        private void Chart(string title, string subtitle, List<CountRow> rows, bool sortByCount = true)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(title, 15, true, Ui.Ink, new Thickness(0)));
            sp.Children.Add(Ui.Text(subtitle, 11, false, Ui.Muted, new Thickness(0, 0, 0, 10)));

            int total = rows.Sum(r => r.Count);
            if (total == 0)
            {
                sp.Children.Add(Ui.Text("No data stored yet.", 12, false, Ui.Muted));
                ChartGrid.Children.Add(Ui.Card(sp));
                return;
            }

            IEnumerable<CountRow> ordered = sortByCount ? rows.OrderByDescending(r => r.Count) : (IEnumerable<CountRow>)rows;
            int max = rows.Max(r => r.Count);

            foreach (CountRow r in ordered)
            {
                double share = 100.0 * r.Count / total;
                var line = new Grid { Margin = new Thickness(0, 0, 0, 6), ToolTip = r.Label + ": " + r.Count + " (" + share.ToString("0.0") + "% of " + total + ")" };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });

                var label = Ui.Text(r.Label, 12, false, Ui.Ink, new Thickness(0, 0, 8, 0));
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextWrapping = TextWrapping.NoWrap;
                line.Children.Add(label);

                var track = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(r.Count, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, max - r.Count), GridUnitType.Star) });
                track.Children.Add(new Border { Background = BarBrush, CornerRadius = new CornerRadius(0, 4, 4, 0) });
                var rest = new Border { Background = TrackBrush };
                Grid.SetColumn(rest, 1);
                track.Children.Add(rest);
                Grid.SetColumn(track, 1);
                line.Children.Add(track);

                var val = Ui.Text(r.Count + "  (" + share.ToString("0") + "%)", 12, true, Ui.Ink, new Thickness(8, 0, 0, 0));
                Grid.SetColumn(val, 2);
                line.Children.Add(val);
                sp.Children.Add(line);
            }
            sp.Children.Add(Ui.Text("n = " + total, 10.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            ChartGrid.Children.Add(Ui.Card(sp));
        }
    }
}
