// ResearcherVerificationWindow.xaml.cs - AI prediction next to two independent human layers:
// the researcher reference assessment and the licensed dermatologist validation.
// Both are append-only rows in their own tables; the AI rows are never modified.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class ResearcherVerificationWindow : Window
    {
        private static readonly string[] ReferenceLabels =
            { "Acne", "Eczema", "Hyperpigmentation", "Normal", "Other / uncertain", "Unusable image" };
        private static readonly string[] Agreement = { "Agree", "Partially agree", "Disagree", "Uncertain" };

        private readonly string _sessionId;
        private SessionDetail _d;

        // researcher form
        private WrapPanel _rLabels;
        private ComboBox _rAgree;
        private TextBox _rNotes, _rFront, _rLeft, _rRight, _rId;

        // dermatologist form
        private WrapPanel _dLabels;
        private ComboBox _dAgreeAi, _dAgreeRes, _dStatus, _dFitz;
        private TextBox _dId, _dName, _dCred, _dNotes;

        public ResearcherVerificationWindow(string scanSessionId)
        {
            InitializeComponent();
            _sessionId = scanSessionId;
            Load();
        }

        private void Load()
        {
            _d = StudyRepository.GetSessionDetail(_sessionId);
            if (_d == null)
            {
                TitleTxt.Text = "Scan not found";
                return;
            }

            ScanSessionRow s = _d.Session;
            TitleTxt.Text = "Researcher Verification — " + s.DisplayId;
            SubtitleTxt.Text = "PatientID " + (s.ParticipantID ?? "unlinked") + "  ·  ScanSessionID " + s.ScanSessionID +
                               "  ·  " + s.StartedAt + "  ·  Model " + (_d.Model != null ? _d.Model.ShortLabel : "unknown");

            BuildAiColumn();
            BuildResearcherForm();
            BuildDermForm();
            BuildHistory();
        }

        // ------------------------------------------------------------ AI side --

        private void BuildAiColumn()
        {
            AiColumn.Children.Clear();
            ScanSessionRow s = _d.Session;

            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("AI Prediction (read-only)", 16, true));
            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("Overall AI result", s.OverallPredictedClass == null ? "no usable result" : s.OverallPredictedClass + "  " + s.ScoreText),
                Ui.KV("Views", s.ScanMode == "Single" ? "single capture" : s.CompletionText + " captured"),
                Ui.KV("View agreement", s.ScanMode == "Single" ? "n/a" : (s.ViewDisagreement ? "Disagreement — review each angle" : "Consistent"))
            }, 140));

            var row = new UniformGrid { Columns = Math.Max(1, _d.Views.Count), Margin = new Thickness(0, 10, 0, 0) };
            foreach (CaptureViewRow v in _d.Views)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                col.Children.Add(Ui.Text(v.ViewType, 12.5, true));
                Border thumb = Ui.Thumb(v.AnalysedImagePath ?? v.OriginalImagePath, 120,
                    v.Scores != null ? Ui.ClassColor(v.Scores.PredictedClass) : "#E5E7EB");
                thumb.Cursor = System.Windows.Input.Cursors.Hand;
                thumb.ToolTip = "Open the original image";
                string original = v.OriginalImagePath;
                thumb.MouseLeftButtonUp += (o, e) => OpenFile(original);
                col.Children.Add(thumb);
                if (v.Scores != null)
                {
                    col.Children.Add(Ui.Text(v.Scores.PredictedClass + " " + Ui.Pct(v.Scores.Confidence), 12, true,
                        Ui.Brush(Ui.ClassColor(v.Scores.PredictedClass)), new Thickness(0, 4, 0, 2)));
                    col.Children.Add(Ui.ScoreBars(v.Scores.AsArray(), 5, compact: true));
                }
                row.Children.Add(col);
            }
            sp.Children.Add(row);

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            bool hasFolder = !string.IsNullOrEmpty(s.SessionFolder) && System.IO.Directory.Exists(s.SessionFolder);
            Button gallery = Ui.Btn("Original images + 4 CAMs per view", (o, e) => OpenGallery(), "SmallButton");
            gallery.IsEnabled = hasFolder;
            buttons.Children.Add(gallery);
            Button folder = Ui.Btn("Open scan folder", (o, e) => OpenFile(s.SessionFolder), "SmallButton");
            folder.IsEnabled = hasFolder;
            buttons.Children.Add(folder);
            sp.Children.Add(buttons);

            sp.Children.Add(Ui.Text(StudyText.ScoresNote + " " + StudyText.AttributionNote, 10.5, false, Ui.Muted, new Thickness(0, 10, 0, 0)));
            AiColumn.Children.Add(Ui.Card(sp));
        }

        private static void OpenFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Could not open: " + ex.Message, "LUMYVUE"); }
        }

        private void OpenGallery()
        {
            try { new ComparisonGalleryWindow(_d.Session.SessionFolder) { Owner = this }.Show(); }
            catch (Exception ex) { MessageBox.Show("Could not open the gallery: " + ex.Message, "LUMYVUE"); }
        }

        // ------------------------------------------------------- form helpers --

        private static TextBlock Label(string t) =>
            new TextBlock { Text = t, Style = (Style)Application.Current.FindResource("FieldLabel") };

        private static TextBox Field(bool area = false) =>
            new TextBox { Style = (Style)Application.Current.FindResource(area ? "FieldArea" : "Field") };

        private static ComboBox Combo(IEnumerable<string> items, int selected = -1)
        {
            var c = new ComboBox { Style = (Style)Application.Current.FindResource("Combo"), ItemsSource = items.ToList() };
            if (selected >= 0) c.SelectedIndex = selected;
            return c;
        }

        private static WrapPanel LabelChecks()
        {
            var w = new WrapPanel();
            foreach (string l in ReferenceLabels)
                w.Children.Add(new CheckBox { Content = l, Margin = new Thickness(0, 4, 16, 4), FontSize = 13 });
            return w;
        }

        private static List<string> CheckedLabels(WrapPanel w) =>
            w.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();

        /// <summary>Suggested agreement from the chosen labels vs the AI's overall class (the user can change it).</summary>
        private string SuggestAgreement(List<string> labels)
        {
            string ai = _d.Session.OverallPredictedClass;
            if (labels.Count == 0 || ai == null) return null;
            if (labels.Count == 1 && labels[0] == ai) return "Agree";
            if (labels.Contains(ai)) return "Partially agree";
            if (labels.Contains("Other / uncertain") || labels.Contains("Unusable image")) return "Uncertain";
            return "Disagree";
        }

        private void HookSuggestion(WrapPanel labels, ComboBox agree)
        {
            foreach (CheckBox c in labels.Children.OfType<CheckBox>())
            {
                RoutedEventHandler h = (s, e) =>
                {
                    string sug = SuggestAgreement(CheckedLabels(labels));
                    if (sug != null) agree.SelectedItem = sug;
                };
                c.Checked += h;
                c.Unchecked += h;
            }
        }

        // ----------------------------------------------------- researcher form --

        private void BuildResearcherForm()
        {
            ResearcherForm.Children.Clear();
            ResearcherForm.Children.Add(Ui.Text("Researcher reference assessment", 16, true));
            ResearcherForm.Children.Add(Ui.Text("Select the reference label(s) you assign to this scan. More than one label may be selected if the protocol allows it.",
                11.5, false, Ui.Muted));

            ResearcherForm.Children.Add(Label("Reference assessment *"));
            _rLabels = LabelChecks();
            ResearcherForm.Children.Add(_rLabels);

            ResearcherForm.Children.Add(Label("Agreement with AI *"));
            _rAgree = Combo(Agreement);
            ResearcherForm.Children.Add(_rAgree);
            HookSuggestion(_rLabels, _rAgree);

            ResearcherForm.Children.Add(Label("Researcher notes"));
            _rNotes = Field(area: true);
            ResearcherForm.Children.Add(_rNotes);

            if (_d.Session.ScanMode != "Single")
            {
                ResearcherForm.Children.Add(Label("Per-view Comments (if any)"));
                var g = new Grid();
                for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _rFront = Field(); _rLeft = Field(); _rRight = Field();
                TextBox[] boxes = { _rFront, _rLeft, _rRight };
                for (int i = 0; i < 3; i++)
                {
                    var sp = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0) };
                    sp.Children.Add(Ui.Text(StudyText.Views[i], 11, false, Ui.Muted, new Thickness(0)));
                    sp.Children.Add(boxes[i]);
                    Grid.SetColumn(sp, i);
                    g.Children.Add(sp);
                }
                ResearcherForm.Children.Add(g);
            }

            ResearcherForm.Children.Add(Label("Researcher ID *"));
            _rId = Field();
            _rId.Text = AppSession.ActorId;
            ResearcherForm.Children.Add(_rId);

            Button save = Ui.Btn("SAVE EVALUATION", (s, e) => SaveResearcher(), "PrimaryButton");
            save.Margin = new Thickness(0, 16, 0, 0);
            ResearcherForm.Children.Add(save);
        }

        private void SaveResearcher()
        {
            List<string> labels = CheckedLabels(_rLabels);
            var missing = new List<string>();
            if (labels.Count == 0) missing.Add("a reference assessment");
            if (_rAgree.SelectedItem == null) missing.Add("agreement with AI");
            if (string.IsNullOrWhiteSpace(_rId.Text)) missing.Add("researcher ID");
            if (missing.Count > 0) { MessageBox.Show("Please provide " + string.Join(", ", missing) + ".", "LUMYVUE Verification"); return; }

            try
            {
                StudyRepository.SaveResearcherEvaluation(new ResearcherEvaluation
                {
                    ScanSessionID = _sessionId,
                    ResearcherID = _rId.Text.Trim(),
                    ResearcherClassification = string.Join(", ", labels),
                    AgreementWithAI = (string)_rAgree.SelectedItem,
                    Notes = _rNotes.Text,
                    FrontComment = _rFront?.Text,
                    LeftComment = _rLeft?.Text,
                    RightComment = _rRight?.Text
                }, labels);

                MessageBox.Show("Researcher evaluation saved. The AI prediction is unchanged.", "LUMYVUE Verification",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the evaluation: " + ex.Message, "LUMYVUE Verification", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------- dermatologist form --

        private void BuildDermForm()
        {
            DermForm.Children.Clear();
            DermForm.Children.Add(Ui.Text("Licensed dermatologist validation", 16, true));
            DermForm.Children.Add(Ui.Text("A separate validation layer. It does not replace the researcher assessment and never overwrites the AI prediction.",
                11.5, false, Ui.Muted));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel(); var right = new StackPanel();
            Grid.SetColumn(right, 2);
            g.Children.Add(left); g.Children.Add(right);

            left.Children.Add(Label("Validator ID *"));
            _dId = Field(); left.Children.Add(_dId);
            right.Children.Add(Label("Validator Name (where permitted by protocol)"));
            _dName = Field(); right.Children.Add(_dName);
            left.Children.Add(Label("Professional Role"));
            var role = Field(); role.Text = "Licensed Dermatologist"; role.IsReadOnly = true; role.Background = Ui.Brush("#F3F4F6");
            left.Children.Add(role);
            right.Children.Add(Label("License / Credential No. (if required by protocol)"));
            _dCred = Field(); right.Children.Add(_dCred);
            DermForm.Children.Add(g);

            DermForm.Children.Add(Label("Dermatologist reference assessment *"));
            _dLabels = LabelChecks();
            DermForm.Children.Add(_dLabels);

            var g2 = new Grid();
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l2 = new StackPanel(); var r2 = new StackPanel();
            Grid.SetColumn(r2, 2);
            g2.Children.Add(l2); g2.Children.Add(r2);
            l2.Children.Add(Label("Agreement with AI *"));
            _dAgreeAi = Combo(Agreement); l2.Children.Add(_dAgreeAi);
            r2.Children.Add(Label("Agreement with Researcher (if applicable)"));
            var resOptions = new List<string>(Agreement) { "No researcher assessment yet" };
            _dAgreeRes = Combo(resOptions, _d.Evaluations.Count == 0 ? resOptions.Count - 1 : -1);
            r2.Children.Add(_dAgreeRes);
            DermForm.Children.Add(g2);
            HookSuggestion(_dLabels, _dAgreeAi);

            DermForm.Children.Add(Label("Dermatologist-assigned Fitzpatrick type (reference for validating any experimental skin-tone estimate)"));
            _dFitz = Combo(new[] { "Not assessed", "Type I", "Type II", "Type III", "Type IV", "Type V", "Type VI" }, 0);
            DermForm.Children.Add(_dFitz);

            DermForm.Children.Add(Label("Dermatologist notes"));
            _dNotes = Field(area: true); DermForm.Children.Add(_dNotes);

            DermForm.Children.Add(Label("Validation status *"));
            _dStatus = Combo(new[] { "Completed", "Pending" }, 0);
            DermForm.Children.Add(_dStatus);

            Button save = Ui.Btn("SAVE VALIDATION", (s, e) => SaveDerm(), "PrimaryButton");
            save.Margin = new Thickness(0, 16, 0, 0);
            DermForm.Children.Add(save);
        }

        private void SaveDerm()
        {
            List<string> labels = CheckedLabels(_dLabels);
            string status = (string)_dStatus.SelectedItem ?? "Pending";
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_dId.Text)) missing.Add("validator ID");
            if (status == "Completed" && labels.Count == 0) missing.Add("a reference assessment");
            if (status == "Completed" && _dAgreeAi.SelectedItem == null) missing.Add("agreement with AI");
            if (missing.Count > 0) { MessageBox.Show("Please provide " + string.Join(", ", missing) + ".", "LUMYVUE Validation"); return; }

            try
            {
                StudyRepository.SaveDermatologistValidation(new DermatologistValidation
                {
                    ScanSessionID = _sessionId,
                    DermatologistID = _dId.Text.Trim(),
                    ValidatorName = string.IsNullOrWhiteSpace(_dName.Text) ? null : _dName.Text.Trim(),
                    ProfessionalRole = "Licensed Dermatologist",
                    CredentialReference = string.IsNullOrWhiteSpace(_dCred.Text) ? null : _dCred.Text.Trim(),
                    DermatologistAssessment = labels.Count == 0 ? null : string.Join(", ", labels),
                    AgreementWithAI = (string)_dAgreeAi.SelectedItem,
                    AgreementWithResearcher = (string)_dAgreeRes.SelectedItem,
                    Notes = _dNotes.Text,
                    ValidationStatus = status
                }, labels);
                StudyRepository.SaveDermatologistFitzpatrick(_sessionId, (string)_dFitz.SelectedItem, _dId.Text.Trim());

                MessageBox.Show("Dermatologist validation saved (" + status + "). The AI prediction is unchanged.", "LUMYVUE Validation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the validation: " + ex.Message, "LUMYVUE Validation", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------ history --

        private void BuildHistory()
        {
            HistoryPanel.Children.Clear();
            HistoryPanel.Children.Add(Ui.Text("Researcher evaluations", 15, true));
            if (_d.Evaluations.Count == 0) HistoryPanel.Children.Add(Ui.Text("None yet.", 12, false, Ui.Muted));
            foreach (ResearcherEvaluation e in _d.Evaluations)
            {
                var rows = new List<KeyValuePair<string, string>>
                {
                    Ui.KV("Date", e.EvaluationDate), Ui.KV("Researcher", e.ResearcherID),
                    Ui.KV("Assessment", e.ResearcherClassification), Ui.KV("Agreement with AI", e.AgreementWithAI),
                    Ui.KV("Notes", e.Notes)
                };
                if (!string.IsNullOrWhiteSpace(e.FrontComment)) rows.Add(Ui.KV("Front", e.FrontComment));
                if (!string.IsNullOrWhiteSpace(e.LeftComment)) rows.Add(Ui.KV("Left", e.LeftComment));
                if (!string.IsNullOrWhiteSpace(e.RightComment)) rows.Add(Ui.KV("Right", e.RightComment));
                HistoryPanel.Children.Add(new Border
                {
                    BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6),
                    Child = Ui.KeyValues(rows, 140)
                });
            }

            HistoryPanel.Children.Add(Ui.Text("Dermatologist validations", 15, true, Ui.Ink, new Thickness(0, 16, 0, 4)));
            if (_d.Validations.Count == 0) HistoryPanel.Children.Add(Ui.Text("None yet.", 12, false, Ui.Muted));
            foreach (DermatologistValidation v in _d.Validations)
            {
                HistoryPanel.Children.Add(new Border
                {
                    BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6),
                    Child = Ui.KeyValues(new[]
                    {
                        Ui.KV("Date", v.ValidationDate), Ui.KV("Validator", v.DermatologistID + (v.ValidatorName != null ? " (" + v.ValidatorName + ")" : "")),
                        Ui.KV("Role", v.ProfessionalRole), Ui.KV("Credential ref.", v.CredentialReference),
                        Ui.KV("Assessment", v.DermatologistAssessment), Ui.KV("Agreement with AI", v.AgreementWithAI),
                        Ui.KV("Agreement w/ researcher", v.AgreementWithResearcher), Ui.KV("Status", v.ValidationStatus), Ui.KV("Notes", v.Notes)
                    }, 160)
                });
            }

            var dermFitz = string.IsNullOrEmpty(_d.Session.ParticipantID)
                ? new List<FitzpatrickAssessment>()
                : StudyRepository.FitzpatrickFor(_d.Session.ParticipantID).Where(a => a.SourceType == "Dermatologist").ToList();
            if (dermFitz.Count > 0)
            {
                HistoryPanel.Children.Add(Ui.Text("Dermatologist-assigned Fitzpatrick type", 15, true, Ui.Ink, new Thickness(0, 16, 0, 4)));
                foreach (FitzpatrickAssessment a in dermFitz)
                    HistoryPanel.Children.Add(new Border
                    {
                        BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6),
                        Child = Ui.KeyValues(new[] { Ui.KV("Type", a.ManualType), Ui.KV("Date", a.AssessmentDate), Ui.KV("Status", a.Status) }, 160)
                    });
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}