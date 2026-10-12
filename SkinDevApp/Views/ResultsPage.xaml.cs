// ResultsPage.xaml.cs - Final Results for one saved scan session (live or historical).
// Everything shown is read from the database / saved files: nothing is re-run, so a
// historical scan always shows the output of the model that produced it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SkinDevApp.Data;
using SkinDevApp.Scanning;

namespace SkinDevApp.Views
{
    public partial class ResultsPage : Page
    {
        private readonly string _sessionId;
        private SessionDetail _d;

        // per-view image holders, so the view toggle (Original / Grad-CAM++ / Localization / Combined) can swap the picture
        private readonly List<KeyValuePair<CaptureViewRow, Border>> _viewThumbs = new List<KeyValuePair<CaptureViewRow, Border>>();
        private string _viewMode = "Original";

        public ResultsPage(string scanSessionId)
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Results");
            _sessionId = scanSessionId;
            Loaded += (s, e) => Render();
        }

        private void Render()
        {
            MainColumn.Children.Clear();
            SideColumn.Children.Clear();
            _viewThumbs.Clear();
            _viewMode = "Original";

            try { _d = StudyRepository.GetSessionDetail(_sessionId); }
            catch (Exception ex)
            {
                MainColumn.Children.Add(Ui.Card(Ui.Text("Could not load the scan: " + ex.Message, 13, false, Ui.Bad)));
                return;
            }
            if (_d == null)
            {
                MainColumn.Children.Add(Ui.Card(Ui.Text("Scan " + _sessionId + " was not found in the database.", 13, false, Ui.Bad)));
                return;
            }

            ScanSessionRow s = _d.Session;
            TitleTxt.Text = "Results · " + s.DisplayId;
            SubtitleTxt.Text = (s.ParticipantID ?? "Scan without a patient" + (string.IsNullOrEmpty(s.LegacyPatientLabel) ? "" : " (" + s.LegacyPatientLabel + ")")) +
                               (_d.Participant != null ? "  ·  " + _d.Participant.FullName : "") +
                               "  ·  " + Fmt.When(s.StartedAt) + "  ·  " + (_d.Model != null ? _d.Model.ShortLabel : "Unknown model") +
                               "  ·  " + (s.ScanStatus == "complete" ? "Complete" : s.ScanStatus == "incomplete" ? "Incomplete" : s.ScanStatus) + (s.Finalized ? "" : " (not finalised)");

            MainColumn.Children.Add(OverallCard());
            MainColumn.Children.Add(PerViewCard());
            BuildSide();
        }

        // ------------------------------------------------------------ overall --

        private double[] OverallPercents()
        {
            FusedResult f = _d.Fusion;
            if (f != null && f.PooledPercent != null && f.PooledPercent.Count > 0)
                return StudyText.Classes.Select(c => { double v; return f.PooledPercent.TryGetValue(c, out v) ? v : 0; }).ToArray();
            CaptureViewRow only = _d.Views.FirstOrDefault(v => v.Scores != null);
            return only != null ? only.Scores.AsArray() : new double[4];
        }

        private Border OverallCard()
        {
            ScanSessionRow s = _d.Session;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(s.ScanMode == "Single" ? "Result for the photograph" : "Overall result of the three photographs", 16, true));

            if (string.IsNullOrEmpty(s.OverallPredictedClass))
            {
                sp.Children.Add(Ui.Text("None of the photographs could be analysed. Please retake the scan.", 13, false, Ui.Bad));
                return Ui.Card(sp);
            }

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            top.Children.Add(Ui.Text("The scan most resembles:  ", 14, false, Ui.Muted, new Thickness(0)));
            top.Children.Add(Ui.Text(ClinicalText.Name(s.OverallPredictedClass), 22, true, Ui.Brush(Ui.ClassColor(s.OverallPredictedClass)), new Thickness(0)));
            top.Children.Add(Ui.Text("   " + (s.OverallScore.HasValue ? char.ToUpper(ClinicalText.Strength(s.OverallScore.Value)[0]) + ClinicalText.Strength(s.OverallScore.Value).Substring(1) + " match (" + s.ScoreText + ")" : s.ScoreText), 13, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(top);

            sp.Children.Add(Ui.Text("How closely the photographs resemble each category", 12.5, true, Ui.Ink, new Thickness(0, 4, 0, 6)));
            sp.Children.Add(Ui.ScoreBars(OverallPercents()));

            // completeness
            if (s.ScanMode != "Single")
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
                chips.Children.Add(Ui.Text("Photographs analysed:  ", 12.5, true, Ui.Ink, new Thickness(0, 3, 0, 0)));
                foreach (string v in StudyText.Views)
                {
                    bool ok = _d.View(v)?.Scores != null;
                    chips.Children.Add(Ui.Pill((ok ? "✓ " : "✗ ") + v, ok ? Ui.Good : Ui.Bad, ok ? "#EEF6EC" : "#FDECEC"));
                }
                sp.Children.Add(chips);

                // do the photographs agree with each other?
                string ah, ad; int al;
                ClinicalText.Agreement(ClinicalText.Readings(_d), false, out ah, out ad, out al);
                sp.Children.Add(Ui.Callout(ah, ad, al));
                sp.Children.Add(Ui.Text(ClinicalText.HowToReadScores, 11, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            }

            return Ui.Card(sp);
        }

        // ----------------------------------------------------------- per view --

        private Border PerViewCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(_d.Session.ScanMode == "Single" ? "The photograph" : "Reading of each photograph", 16, true));

            // Two independent pieces of evidence for the SAME frame: Grad-CAM++ (explains the classifier) and the lesion detector (candidate locations).
            var toggle = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            foreach (string mode in new[] { "Original", "Grad-CAM++", "Localization", "Combined" })
            {
                var rb = new RadioButton
                {
                    Content = mode == "Grad-CAM++" ? "Heat map" : mode == "Localization" ? "Marked areas" : mode == "Combined" ? "Both" : mode,
                    GroupName = "viewmode",
                    IsChecked = mode == "Original",
                    Margin = new Thickness(0, 0, 18, 0),
                    FontSize = 12.5,
                    ToolTip = mode == "Combined" ? "The predicted class's Grad-CAM++ overlay with the candidate lesion boxes on top. The two outputs stay independent."
                            : mode == "Grad-CAM++" ? "Which image areas influenced the classifier's score for the predicted class (attribution, not a lesion map)."
                            : mode == "Localization" ? "Candidate lesion locations from a separate research detector."
                            : "The exact frame that was analysed."
                };
                string m = mode;
                rb.Checked += (s, e) => { _viewMode = m; RefreshThumbs(); };
                toggle.Children.Add(rb);
            }
            sp.Children.Add(toggle);

            var grid = new UniformGrid { Columns = Math.Max(1, _d.Session.ScanMode == "Single" ? 1 : 3) };
            string[] names = _d.Session.ScanMode == "Single" ? new[] { "Single" } : StudyText.Views;
            foreach (string name in names)
            {
                CaptureViewRow v = _d.View(name);
                var col = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                col.Children.Add(Ui.Text(name == "Single" ? "Photograph" : name + " view", 13, true));
                Border thumb = Ui.Thumb(v?.AnalysedImagePath ?? v?.OriginalImagePath, 150, v?.Scores != null ? Ui.ClassColor(v.Scores.PredictedClass) : "#E5E7EB",
                    v == null ? "Not captured" : null);
                col.Children.Add(thumb);
                if (v != null) _viewThumbs.Add(new KeyValuePair<CaptureViewRow, Border>(v, thumb));

                if (v?.Scores != null)
                {
                    ViewReading rd = ClinicalText.Readings(_d).First(x => x.View == v.ViewType);
                    col.Children.Add(Ui.Text(ClinicalText.Top(rd), 13, true,
                        Ui.Brush(Ui.ClassColor(v.Scores.PredictedClass)), new Thickness(0, 6, 0, 3)));
                    col.Children.Add(Ui.Text(ClinicalText.Meaning(rd), 11.5, false, Ui.Ink, new Thickness(0, 0, 0, 3)));
                    col.Children.Add(Ui.Text(ClinicalText.NextClosest(rd), 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
                    col.Children.Add(Ui.ScoreBars(v.Scores.AsArray(), 6, compact: true));
                    col.Children.Add(Ui.Text("Photo quality: " + ClinicalText.PlainQuality(v.QualityStatus), 10.5, false,
                        v.QualityStatus == "Good" ? Ui.Good : Ui.Warn, new Thickness(0, 2, 0, 0)));
                    col.Children.Add(Ui.Text(v.Maps.Count > 0 ? "Heat map saved (shows what the computer looked at)" : "Heat map not available for this photograph",
                        10.5, false, v.Maps.Count > 0 ? Ui.Muted : Ui.Warn, new Thickness(0)));
                    bool locWarn;
                    col.Children.Add(Ui.Text(LocalizationLine(v, out locWarn), 10.5, false, locWarn ? Ui.Warn : Ui.Muted, new Thickness(0, 2, 0, 0)));
                }
                else
                {
                    col.Children.Add(Ui.Text(v == null ? "This photograph was not taken." : "This photograph could not be analysed.", 12, false, Ui.Bad, new Thickness(0, 6, 0, 0)));
                }
                grid.Children.Add(col);
            }
            sp.Children.Add(grid);
                        sp.Children.Add(Ui.Text("Marked areas are places that may contain lesions (acne red, hyperpigmentation blue, eczema orange). " +
                                    "Some lesions may be missed or mislabelled, so the boxes are not a lesion count and not a diagnosis.",
                                    10.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            return Ui.Card(sp);
        }

        // ------------------------------------------------------ view toggle --

        private string PathFor(CaptureViewRow v, string mode)
        {
            switch (mode)
            {
                case "Grad-CAM++":
                    string pred = v.Scores != null ? v.Scores.PredictedClass : null;
                    AttributionMapRow map = v.Maps.FirstOrDefault(x => x.TargetClass == pred);
                    return map != null ? map.OverlayPath : null;
                case "Localization":
                    return v.Localization != null && v.Localization.Status == "OK" ? v.Localization.OverlayPath : null;
                case "Combined":
                    return v.Localization != null && v.Localization.Status == "OK" ? v.Localization.CombinedPath : null;
                default:
                    return v.AnalysedImagePath ?? v.OriginalImagePath;
            }
        }

        private void RefreshThumbs()
        {
            foreach (var kv in _viewThumbs)
            {
                string path = PathFor(kv.Key, _viewMode);
                var src = path != null ? Ui.LoadImage(path, 480) : null;
                kv.Value.Child = src != null
                    ? (UIElement)new System.Windows.Controls.Image { Source = src, Stretch = System.Windows.Media.Stretch.Uniform }
                    : Ui.Text(_viewMode == "Original" ? "image not available" : _viewMode + " is not available for this view", 11, false, Ui.Muted, new Thickness(8));
            }
        }

        private static string LocalizationLine(CaptureViewRow v, out bool warn)
        {
            warn = false;
            LocalizationRunRow r = v.Localization;
            if (r == null) return "Localization: not available (scan saved before this feature)";
            if (r.Status == "Failed") { warn = true; return "Localization failed: " + (r.ErrorText ?? "unknown error") + " (scan and Grad-CAM++ are unaffected)"; }
            if (r.Status != "OK") return "Localization was not run for this view";
            if (v.Lesions.Count == 0) return "Marked areas: none found";
            var parts = v.Lesions.GroupBy(x => x.ClassName).Select(g => g.Key + " " + g.Count());
            return "Marked areas: " + v.Lesions.Count + " (" + string.Join(", ", parts) + ")";
        }

        // --------------------------------------------------------------- side --

        private void BuildSide()
        {
            bool r = AppSession.IsResearcher;
            var actions = new StackPanel();
            actions.Children.Add(Ui.Text("What would you like to do next?", 15, true));

            void Add(string text, RoutedEventHandler h, string style = "SecondaryButton", bool enabled = true)
            {
                Button b = Ui.Btn(text, h, style);
                b.Margin = new Thickness(0, 0, 0, 8);
                b.IsEnabled = enabled;
                actions.Children.Add(b);
            }

            bool hasFolder = !string.IsNullOrEmpty(_d.Session.SessionFolder) && System.IO.Directory.Exists(_d.Session.SessionFolder);
            if (r)
            {
                Add("Compare photographs and heat maps", (s, e) => OpenGallery(), "SecondaryButton", hasFolder);
                Add("Add researcher notes", (s, e) => OpenVerification(), "PrimaryButton");
                Add("Dermatologist review", (s, e) =>
                {
                    new DermatologistReviewWindow(_sessionId) { Owner = Application.Current.MainWindow }.ShowDialog();
                    Render();
                }, "SecondaryButton");
            }
            Add("Create report", (s, e) => ResearchReportWindow.Open(_sessionId), "AccentButton");
            if (r && !string.IsNullOrEmpty(_d.Session.ParticipantID))
                Add("Patient record", (s, e) => Nav.Go(new PatientRecordPage(_d.Session.ParticipantID)));
            if (!string.IsNullOrEmpty(_d.Session.ParticipantID))
                Add("New scan for this patient", (s, e) => Workflow.ContinueToScan(_d.Session.ParticipantID));
            Add("Back to home", (s, e) => Nav.Home());
            SideColumn.Children.Add(Ui.Card(actions));

            // Human assessments (kept separate from the AI output)
            var human = new StackPanel();
            human.Children.Add(Ui.Text("Clinical review", 15, true));
            ResearcherEvaluation ev = _d.Evaluations.FirstOrDefault();
            DermatologistValidation dv = _d.Validations.FirstOrDefault(x => x.ValidationStatus == "Completed") ?? _d.Validations.FirstOrDefault();
            human.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Researcher", ev == null ? "Not added yet" : ev.ResearcherClassification + " (" + ev.AgreementWithAI + ")"),
                Ui.KV("Dermatologist", dv == null ? "Not reviewed yet" : dv.DermatologistAssessment + " — " + dv.ValidationStatus.ToLowerInvariant())
            }, 110, 12.5));
            human.Children.Add(Ui.Text("Reviews are saved separately and never change the computer's result.", 10.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            SideColumn.Children.Add(Ui.Card(human));

            // Notice
            var notice = new Border { Style = (Style)FindResource("NoticeBox"), Margin = new Thickness(8) };
            var n = new StackPanel();
            n.Children.Add(Ui.Text("Research prototype", 13, true));
            n.Children.Add(Ui.Text(StudyText.PrototypeNotice, 11.5, false, Ui.Ink));
            notice.Child = n;
            SideColumn.Children.Add(notice);
        }

        private void OpenGallery()
        {
            try
            {
                var g = new ComparisonGalleryWindow(_d.Session.SessionFolder) { Owner = Application.Current.MainWindow };
                g.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("The comparison could not be opened: " + ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenVerification()
        {
            var w = new ResearcherVerificationWindow(_sessionId) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            Render();
        }
    }
}
