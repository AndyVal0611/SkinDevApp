// DermatologistReviewWindow.cs - clinical review summary for the licensed dermatologist.
//
// Same stored data as the technical Researcher pages, presented without raw AI metrics: who/when, one plain AI-assisted summary,
// a per-view table, the three views with the original image first (and toggles for the AI-identified region of interest and Grad-CAM++),
// then the dermatologist's OWN assessment. The AI output is never presented as a diagnosis and never pre-fills the assessment.
// Everything shown is read from the saved scan session; nothing is re-run and no example values are hard-coded.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class DermatologistReviewWindow : Window
    {
        // stored vocabulary (kept compatible with the existing dermatologist records and dashboards)
        private static readonly string[] AgreementShown = { "Agree", "Partially agree", "Disagree", "Unable to determine" };
        private static readonly string[] AgreementStored = { "Agree", "Partially agree", "Disagree", "Uncertain" };
        private static readonly string[] ReferenceShown = { "Acne", "Eczema", "Hyperpigmentation", "Normal Skin", "Other / Different finding", "Unable to assess" };
        private static readonly string[] ReferenceStored = { "Acne", "Eczema", "Hyperpigmentation", "Normal", "Other / uncertain", "Unable to assess" };
        private static readonly string[] LocalizationOptions = { "Relevant", "Partially relevant", "Not relevant", "No localization available", "Unable to assess" };
        private static readonly string[] GradCamOptions = { "Useful", "Partially useful", "Not useful", "Not interpretable", "Not available" };

        public const string Disclaimer =
            "LUMYVUE / PrecisionSkin provides AI-generated classifications and visual evidence based on the condition categories included in its trained models. " +
            "These outputs are intended to assist research and review and do not constitute a medical diagnosis. " +
            "Clinical interpretation and diagnosis remain the responsibility of a qualified healthcare professional.";

        private readonly string _sessionId;
        private SessionDetail _d;

        // evidence viewer state
        private string _viewName;
        private string _mode = "Original";
        private StackPanel _viewerHost;

        // assessment form
        private readonly List<RadioButton> _refRadios = new List<RadioButton>();
        private ComboBox _agree, _localization, _gradcam, _status;
        private TextBox _notes, _front, _left, _right, _id, _name, _cred;

        public DermatologistReviewWindow(string scanSessionId)
        {
            _sessionId = scanSessionId;
            Title = "LUMYVUE - Dermatologist Review";
            Width = 1180;
            Height = 900;
            MinWidth = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Brush("#FBF9F6");
            Build();
        }

        private static string Shown(string cls) => cls == "Normal" ? "Normal Skin" : cls;

        // ------------------------------------------------------------------ build --

        private void Build()
        {
            _d = StudyRepository.GetSessionDetail(_sessionId);
            var root = new StackPanel { Margin = new Thickness(26, 20, 26, 28) };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            Content = scroll;

            if (_d == null)
            {
                root.Children.Add(Ui.Text("Scan not found", 20, true, Ui.Bad));
                return;
            }

            root.Children.Add(Ui.Text("Dermatologist Review", 24, true));
            root.Children.Add(Ui.Text("Clinical Review Summary  ·  AI-assisted research output (not a diagnosis)", 13, false, Ui.Muted, new Thickness(0, 2, 0, 12)));

            root.Children.Add(IdentityCard());
            root.Children.Add(SummaryCard());
            root.Children.Add(PerViewTable());

            _viewerHost = new StackPanel();
            root.Children.Add(_viewerHost);
            _viewName = _d.Views.Select(v => v.ViewType).FirstOrDefault();
            RenderViewer();

            root.Children.Add(AssessmentCard());
            root.Children.Add(DisclaimerCard());
            root.Children.Add(HistoryCard());
        }

        // ------------------------------------------------------------- identity --

        private UIElement IdentityCard()
        {
            ScanSessionRow s = _d.Session;
            Participant p = _d.Participant;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Patient / Participant Information", 15, true));
            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("PatientID", s.ParticipantID ?? "unlinked"),
                Ui.KV("Participant name", p != null ? p.FullName : "—"),
                Ui.KV("Age / Sex", p == null ? "—" : (p.Age.HasValue ? p.Age + " years" : "age not recorded") + (string.IsNullOrEmpty(p.Sex) ? "" : "  ·  " + p.Sex)),
                Ui.KV("ScanSessionID", s.ScanSessionID),
                Ui.KV("Scan date / time", s.StartedAt ?? "—"),
                Ui.KV("Scan completeness", ViewsText())
            }, 150));
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private string ViewsText()
        {
            string[] order = { "Front", "Left", "Right" };
            var have = new HashSet<string>(_d.Views.Select(v => v.ViewType), StringComparer.OrdinalIgnoreCase);
            if (_d.Session.ScanMode == "Single") return "Single image";
            return string.Join("  ·  ", order.Select(o => o + (have.Contains(o) ? " ✓" : " — missing")));
        }

        // -------------------------------------------------------------- summary --

        private UIElement SummaryCard()
        {
            ScanSessionRow s = _d.Session;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("AI-Assisted Scan Summary", 16, true));

            if (string.IsNullOrEmpty(s.OverallPredictedClass))
            {
                sp.Children.Add(Ui.Text("No usable AI result for this scan.", 15, true, Ui.Bad, new Thickness(0, 8, 0, 0)));
                sp.Children.Add(Ui.Text("The scan did not produce a usable classification. Review the original images and record your own assessment below.", 12.5, false, Ui.Muted));
                return Ui.Card(sp, new Thickness(0, 0, 0, 12));
            }

            string cls = Shown(s.OverallPredictedClass);
            var head = new TextBlock { Margin = new Thickness(0, 8, 0, 4) };
            head.Inlines.Add(new System.Windows.Documents.Run("Primary AI Classification: ") { FontSize = 15, Foreground = Ui.Muted });
            head.Inlines.Add(new System.Windows.Documents.Run(cls) { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.ClassColor(s.OverallPredictedClass)) });
            sp.Children.Add(head);

            bool single = s.ScanMode == "Single";
            sp.Children.Add(Ui.Text("Among the condition categories evaluated by the model, " + cls + " received the strongest overall classification response " +
                                    (single ? "in the captured image." : "across the completed scan views."), 13, false, Ui.Ink));

            var analysed = _d.Views.Where(v => v.Scores != null).Select(v => v.ViewType).ToList();
            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("Model Class Score", s.ScoreText),
                Ui.KV("Views analyzed", analysed.Count == 0 ? "none" : string.Join(", ", analysed))
            }, 150));

            bool differ = _d.Views.Where(v => v.Scores != null).Select(v => v.Scores.PredictedClass).Distinct().Count() > 1;
            if (!single && (s.ViewDisagreement || differ))
                sp.Children.Add(Ui.Pill("View disagreement detected — individual scan views should be reviewed.", Ui.Warn, "#FEF3C7"));

            sp.Children.Add(Ui.Text("Model Class Score is the classifier's output for this category. It is not a clinical probability.", 11, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        // -------------------------------------------------------- per-view table --

        private UIElement PerViewTable()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Per-view summary", 15, true));
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            double[] w = { 1, 2, 1.4, 1.6, 1 };
            foreach (double x in w) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(x, GridUnitType.Star) });

            string[] heads = { "View", "AI Classification", "Model Class Score", "Image Quality", "Review" };
            for (int c = 0; c < heads.Length; c++)
            {
                TextBlock t = Ui.Text(heads[c], 11.5, true, Ui.Muted, new Thickness(0, 0, 0, 6));
                Grid.SetColumn(t, c); Grid.SetRow(t, 0); g.Children.Add(t);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int row = 1;
            foreach (CaptureViewRow v in _d.Views)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string view = v.ViewType;
                string[] cells =
                {
                    view,
                    v.Scores == null ? "no result" : Shown(v.Scores.PredictedClass),
                    v.Scores == null ? "—" : Ui.Pct(v.Scores.Confidence),
                    string.IsNullOrEmpty(v.QualityStatus) ? "not recorded" : v.QualityStatus
                };
                for (int c = 0; c < cells.Length; c++)
                {
                    TextBlock t = Ui.Text(cells[c], 13, c == 1, c == 1 && v.Scores != null ? Ui.Brush(Ui.ClassColor(v.Scores.PredictedClass)) : Ui.Ink, new Thickness(0, 4, 0, 4));
                    Grid.SetColumn(t, c); Grid.SetRow(t, row); g.Children.Add(t);
                }
                Button open = Ui.Btn("View", (o, e) => { _viewName = view; RenderViewer(); }, "SmallButton");
                open.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(open, 4); Grid.SetRow(open, row); g.Children.Add(open);
                row++;
            }
            if (_d.Views.Count == 0) sp.Children.Add(Ui.Text("No captured views are saved for this scan.", 12.5, false, Ui.Muted));
            else sp.Children.Add(g);
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        // ---------------------------------------------------------------- viewer --

        private void RenderViewer()
        {
            _viewerHost.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Captured views", 15, true));

            var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            foreach (CaptureViewRow v in _d.Views)
            {
                string name = v.ViewType;
                Button b = Ui.Btn(name, (o, e) => { _viewName = name; RenderViewer(); }, name == _viewName ? "PrimaryButton" : "SecondaryButton");
                b.Margin = new Thickness(0, 0, 8, 0);
                bar.Children.Add(b);
            }
            sp.Children.Add(bar);

            CaptureViewRow cur = _d.View(_viewName);
            if (cur == null)
            {
                sp.Children.Add(Ui.Text("No image is saved for this view.", 12.5, false, Ui.Muted));
                _viewerHost.Children.Add(Ui.Card(sp, new Thickness(0, 0, 0, 12)));
                return;
            }

            string[] modes = { "Original", "Affected Area / Localization", "Grad-CAM++" };
            var modeBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (string m in modes)
            {
                string mode = m;
                Button b = Ui.Btn(m, (o, e) => { _mode = mode; RenderViewer(); }, mode == _mode ? "PrimaryButton" : "SmallButton");
                b.Margin = new Thickness(0, 0, 8, 0);
                modeBar.Children.Add(b);
            }
            sp.Children.Add(modeBar);

            string path = null, note, title;
            switch (_mode)
            {
                case "Affected Area / Localization":
                    title = "AI-identified region of interest";
                    if (cur.Localization != null && cur.Localization.Status == "OK")
                    {
                        path = !string.IsNullOrEmpty(cur.Localization.OverlayPath) ? cur.Localization.OverlayPath : cur.Localization.CombinedPath;
                        int n = cur.Lesions.Count;
                        note = (n == 0 ? "The model localized no suspected affected area in this view." : "Model-localized suspected affected area(s): " + n + ". ") +
                               "These are model outputs, not clinically confirmed lesions.";
                    }
                    else note = "No localization is available for this view.";
                    break;
                case "Grad-CAM++":
                    title = "Grad-CAM++ attribution";
                    AttributionMapRow map = cur.Maps.FirstOrDefault(x => cur.Scores != null && x.TargetClass == cur.Scores.PredictedClass) ?? cur.Maps.FirstOrDefault();
                    if (map != null) path = map.OverlayPath;
                    note = map == null
                        ? "Grad-CAM++ is not available for this view."
                        : "Shown for the class \"" + Shown(map.TargetClass) + "\". Grad-CAM++ shows image regions that influenced the classifier's prediction. It does not represent lesion segmentation or a confirmed affected area.";
                    break;
                default:
                    title = "Original captured image";
                    path = !string.IsNullOrEmpty(cur.OriginalImagePath) ? cur.OriginalImagePath : cur.AnalysedImagePath;
                    note = "The original image is unmodified.";
                    break;
            }

            sp.Children.Add(Ui.Text(cur.ViewType + "  ·  " + title, 12.5, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            var bmp = Ui.LoadImage(path, 1100);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform, MaxHeight = 560, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to enlarge" };
                string p2 = path, t2 = cur.ViewType + " - " + title;
                img.MouseLeftButtonUp += (o, e) => ImagePopup.Show(p2, t2);
                sp.Children.Add(new Border { Background = Ui.Brush("#111827"), CornerRadius = new CornerRadius(6), Child = img });
            }
            else sp.Children.Add(new Border { Background = Ui.Brush("#F3F4F6"), CornerRadius = new CornerRadius(6), Padding = new Thickness(20),
                                              Child = Ui.Text(_mode == "Original" ? "The image file could not be found." : "Not available for this view.", 13, false, Ui.Muted) });
            sp.Children.Add(Ui.Text(note, 11.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            _viewerHost.Children.Add(Ui.Card(sp, new Thickness(0, 0, 0, 12)));
        }

        // ------------------------------------------------------------ assessment --

        private static TextBlock Label(string t) => new TextBlock { Text = t, Style = (Style)Application.Current.FindResource("FieldLabel") };
        private static TextBox Field(bool area = false) => new TextBox { Style = (Style)Application.Current.FindResource(area ? "FieldArea" : "Field") };
        private static ComboBox Combo(IEnumerable<string> items)
            => new ComboBox { Style = (Style)Application.Current.FindResource("Combo"), ItemsSource = items.ToList(), SelectedIndex = -1 };

        private UIElement AssessmentCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Licensed Dermatologist Assessment", 17, true));
            sp.Children.Add(Ui.Text("Your own independent assessment. Nothing here is filled in from the AI result, and saving it never changes the AI output.", 12, false, Ui.Muted));

            sp.Children.Add(Label("Dermatologist Reference Assessment *"));
            var wrap = new WrapPanel();
            foreach (string r in ReferenceShown)
            {
                var rb = new RadioButton { Content = r, GroupName = "derm-ref", Margin = new Thickness(0, 4, 18, 4), FontSize = 13 };
                _refRadios.Add(rb);
                wrap.Children.Add(rb);
            }
            sp.Children.Add(wrap);

            sp.Children.Add(Label("Does the AI classification agree with your assessment? *"));
            _agree = Combo(AgreementShown); sp.Children.Add(_agree);

            sp.Children.Add(Label("Are the AI-localized areas relevant to the visible finding?"));
            _localization = Combo(LocalizationOptions); sp.Children.Add(_localization);

            sp.Children.Add(Label("Was the Grad-CAM++ attribution useful for understanding the AI prediction?"));
            _gradcam = Combo(GradCamOptions); sp.Children.Add(_gradcam);

            sp.Children.Add(Label("Dermatologist Notes / Observations"));
            _notes = Field(true); _notes.MinHeight = 110; sp.Children.Add(_notes);

            sp.Children.Add(Label("Notes per view (optional)"));
            var g = new UniformGrid { Columns = 3 };
            _front = Field(true); _left = Field(true); _right = Field(true);
            foreach (var pair in new[] { Tuple.Create("Front", _front), Tuple.Create("Left", _left), Tuple.Create("Right", _right) })
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                col.Children.Add(Ui.Text(pair.Item1, 11.5, true, Ui.Muted));
                pair.Item2.MinHeight = 60;
                col.Children.Add(pair.Item2);
                g.Children.Add(col);
            }
            sp.Children.Add(g);

            sp.Children.Add(Label("Validator"));
            var idRow = new UniformGrid { Columns = 3 };
            _id = Field(); _name = Field(); _cred = Field();
            foreach (var pair in new[] { Tuple.Create("Validator ID *", _id), Tuple.Create("Name (where permitted by protocol)", _name), Tuple.Create("License / credential no. (if required)", _cred) })
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                col.Children.Add(Ui.Text(pair.Item1, 11.5, false, Ui.Muted));
                col.Children.Add(pair.Item2);
                idRow.Children.Add(col);
            }
            sp.Children.Add(idRow);

            sp.Children.Add(Label("Validation status *"));
            _status = Combo(new[] { "Completed", "Pending" }); _status.SelectedIndex = 0; sp.Children.Add(_status);

            Button save = Ui.Btn("SAVE DERMATOLOGIST ASSESSMENT", (o, e) => Save(), "PrimaryButton");
            save.Margin = new Thickness(0, 16, 0, 0);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(save);
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private void Save()
        {
            int refIdx = _refRadios.FindIndex(r => r.IsChecked == true);
            string status = (string)_status.SelectedItem ?? "Pending";
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_id.Text)) missing.Add("validator ID");
            if (status == "Completed" && refIdx < 0) missing.Add("a reference assessment");
            if (status == "Completed" && _agree.SelectedIndex < 0) missing.Add("agreement with the AI classification");
            if (missing.Count > 0) { MessageBox.Show("Please provide " + string.Join(", ", missing) + ".", "LUMYVUE Dermatologist Review"); return; }

            try
            {
                string label = refIdx < 0 ? null : ReferenceStored[refIdx];
                StudyRepository.SaveDermatologistValidation(new DermatologistValidation
                {
                    ScanSessionID = _sessionId,
                    DermatologistID = _id.Text.Trim(),
                    ValidatorName = string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim(),
                    ProfessionalRole = "Licensed Dermatologist",
                    CredentialReference = string.IsNullOrWhiteSpace(_cred.Text) ? null : _cred.Text.Trim(),
                    DermatologistAssessment = label,
                    AgreementWithAI = _agree.SelectedIndex < 0 ? null : AgreementStored[_agree.SelectedIndex],
                    AgreementWithResearcher = _d.Evaluations.Count == 0 ? "No researcher assessment yet" : null,
                    LocalizationRelevance = (string)_localization.SelectedItem,
                    GradCamUsefulness = (string)_gradcam.SelectedItem,
                    Notes = _notes.Text,
                    FrontComment = NullIfEmpty(_front.Text),
                    LeftComment = NullIfEmpty(_left.Text),
                    RightComment = NullIfEmpty(_right.Text),
                    ValidationStatus = status
                }, label == null ? null : new List<string> { label });

                MessageBox.Show("Dermatologist assessment saved (" + status + "). The AI output is unchanged.", "LUMYVUE Dermatologist Review",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the assessment: " + ex.Message, "LUMYVUE Dermatologist Review", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // ------------------------------------------------------ disclaimer / history --

        private UIElement DisclaimerCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("AI-Assisted Research Output", 13, true, Ui.Ink));
            sp.Children.Add(Ui.Text(Disclaimer, 11.5, false, Ui.Muted, new Thickness(0, 3, 0, 0)));
            var b = Ui.Card(sp, new Thickness(0, 0, 0, 12));
            b.Background = Ui.Brush("#F8FAFC");
            return b;
        }

        private UIElement HistoryCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Previous dermatologist assessments for this scan", 14, true));
            if (_d.Validations.Count == 0) sp.Children.Add(Ui.Text("None yet.", 12.5, false, Ui.Muted));
            foreach (DermatologistValidation v in _d.Validations)
                sp.Children.Add(Ui.KeyValues(new[]
                {
                    Ui.KV("Date / status", v.ValidationDate + "  ·  " + v.ValidationStatus),
                    Ui.KV("Reference assessment", v.DermatologistAssessment ?? "—"),
                    Ui.KV("Agreement with AI", v.AgreementWithAI == "Uncertain" ? "Unable to determine" : v.AgreementWithAI ?? "—"),
                    Ui.KV("Localization relevance", v.LocalizationRelevance ?? "—"),
                    Ui.KV("Grad-CAM++ usefulness", v.GradCamUsefulness ?? "—"),
                    Ui.KV("Notes", string.IsNullOrWhiteSpace(v.Notes) ? "—" : v.Notes)
                }, 170));
            return Ui.Card(sp);
        }
    }
}
