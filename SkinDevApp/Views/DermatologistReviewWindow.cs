// DermatologistReviewWindow.cs - clinical review summary for the dermatologist.
//
// Same stored data as the technical Researcher pages, presented without raw AI metrics: who/when, one plain AI-assisted summary,
// a per-view table, the three views with the original image first (and toggles for the AI-identified region of interest and Grad-CAM++),
// then the dermatologist's OWN assessment. The AI output is never presented as a diagnosis and never pre-fills the assessment.
// Everything shown is read from the saved scan session; nothing is re-run and no example values are hard-coded.
using System;
using System.Collections.Generic;
using System.IO;
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
            "LUMYVUE provides AI-generated classifications and visual evidence based on the condition categories included in its trained models. " +
            "These outputs are intended to assist research and review and do not constitute a medical diagnosis. " +
            "Clinical interpretation and diagnosis remain the responsibility of a qualified healthcare professional.";

        private readonly string _sessionId;
        private SessionDetail _d;

        // evidence viewer state
        private string _viewName;
        private string _mode = "Original";
        private StackPanel _viewerHost;

        // assessment form
        private static readonly string[] StatusShown = { "Verified - review completed", "Requires further review", "Save as draft (not yet verified)" };
        private static readonly string[] StatusStored = { ReviewStatus.ValidationCompleted, ReviewStatus.ValidationFurtherReview, ReviewStatus.ValidationDraft };
        private ComboBox _status;
        private TextBox _notes, _id, _name, _cred;
        private readonly List<Tuple<string, ComboBox, TextBox>> _condRows = new List<Tuple<string, ComboBox, TextBox>>();

        public DermatologistReviewWindow(string scanSessionId)
        {
            _sessionId = scanSessionId;
            Title = "LUMYVUE · Dermatologist review";
            Width = 1180;
            Height = 900;
            MinWidth = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("PageWash");
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
                root.Children.Add(Ui.Text("This scan could not be found.", 20, true, Ui.Bad));
                return;
            }

            root.Children.Add(Ui.WindowBand("Dermatologist review",
                "Scan " + _d.Session.DisplayId + "  ·  The computer's reading of this scan, for your review. It is a decision aid and not a diagnosis."));

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
            sp.Children.Add(Ui.Text("Patient information", 15, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            sp.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Patient ID", s.ParticipantID ?? "Not assigned to a patient"),
                Ui.KV("Name", p != null ? p.FullName : "—"),
                Ui.KV("Age and sex", p == null ? "—" : (p.Age.HasValue ? p.Age + " years" : "Age not recorded") + (string.IsNullOrEmpty(p.Sex) ? "" : ", " + p.Sex)),
                Ui.KV("Scan", s.DisplayId),
                Ui.KV("Date and time", Fmt.When(s.StartedAt)),
                Ui.KV("Photographs", ViewsText())
            }, 150));
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private string ViewsText()
        {
            string[] order = { "Front", "Left", "Right" };
            var have = new HashSet<string>(_d.Views.Select(v => v.ViewType), StringComparer.OrdinalIgnoreCase);
            if (_d.Session.ScanMode == "Single") return "1 (single image)";
            return string.Join(",  ", order.Select(o => o + (have.Contains(o) ? " ✓" : " (missing)")));
        }

        // -------------------------------------------------------------- summary --

        private UIElement SummaryCard()
        {
            ScanSessionRow s = _d.Session;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Summary of the scan", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));

            // V6 flag on and a saved V6 assessment: ONE unified assessment replaces the V5 classifier, its percentages and the separate detector lines
            SkinDevApp.V6.V6SessionSummary v6 = SkinDevApp.V6.V6SessionSummary.For(_d);
            if (v6 != null)
            {
                sp.Children.Add(SkinDevApp.V6.V6ResultsView.SummaryPanel(v6));
                return Ui.Card(sp, new Thickness(0, 0, 0, 12));
            }

            if (string.IsNullOrEmpty(s.OverallPredictedClass))
            {
                sp.Children.Add(Ui.Text("No usable result for this scan.", 15, true, Ui.Bad, new Thickness(0, 8, 0, 0)));
                sp.Children.Add(Ui.Text("The photographs could not be classified. Please look at the original images and record your own assessment below.", 12.5, false, Ui.Muted));
                return Ui.Card(sp, new Thickness(0, 0, 0, 12));
            }

            bool single = s.ScanMode == "Single";
            List<ViewReading> readings = ClinicalText.Readings(_d);
            string cls = ClinicalText.Name(s.OverallPredictedClass);
            double pct = s.OverallScore ?? 0;

            // headline: the overall category, big, with its strength
            var top = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Ui.Text("The scan most resembles", 12, false, Ui.Muted));
            left.Children.Add(Ui.Text(cls, 30, true, Ui.Brush(Ui.ClassColor(s.OverallPredictedClass)), new Thickness(0, 0, 0, 0)));
            top.Children.Add(left);
            var badge = new Border
            {
                Background = Ui.Brush("#F1EDE0"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 10, 18, 10), VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel { Children = {
                    Ui.Text(char.ToUpper(ClinicalText.Strength(pct)[0]) + ClinicalText.Strength(pct).Substring(1) + " match", 15, true, Ui.Ink, new Thickness(0)),
                    Ui.Text(pct.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% on average", 11.5, false, Ui.Muted, new Thickness(0)) } }
            };
            Grid.SetColumn(badge, 1); top.Children.Add(badge);
            sp.Children.Add(top);

            string headline, detail; int level;
            ClinicalText.Agreement(readings, single, out headline, out detail, out level);
            if (!single) sp.Children.Add(Ui.Callout(headline, detail, level));

            sp.Children.Add(Ui.Text("AI-generated interpretation (shown separately from your assessment; it never changes the saved AI prediction)", 11, true, Ui.Muted, new Thickness(0, 10, 0, 2)));
            InterpretationView.AddTo(sp, SessionInterpretation.For(_d));

            var find = ClinicalText.Findings(readings, s.OverallPredictedClass);
            if (find.Count > 0)
            {
                sp.Children.Add(Ui.Text("What this means for your review", 13, true, Ui.Ink, new Thickness(0, 10, 0, 4)));
                foreach (string f in find)
                    sp.Children.Add(Ui.Text("•  " + f, 12.5, false, Ui.Ink, new Thickness(8, 1, 0, 1)));
            }
            sp.Children.Add(Ui.Text(ClinicalText.HowToReadScores, 11, false, Ui.Muted, new Thickness(0, 10, 0, 0)));
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        // -------------------------------------------------------- per-view table --

        private UIElement PerViewTable()
        {
            if (SkinDevApp.V6.V6SessionSummary.For(_d) != null) return new StackPanel();    // V6: each photograph's reading is already in the summary above
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Reading of each photograph", 15, true));
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            double[] w = { 1, 2, 1.4, 1.6, 1 };
            foreach (double x in w) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(x, GridUnitType.Star) });

            string[] heads = { "Photograph", "Most resembles", "Match strength", "Photo quality", "" };
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
                    view == "Single" ? "Photograph" : view + " view",
                    v.Scores == null ? "No result" : Shown(v.Scores.PredictedClass),
                    v.Scores == null ? "—" : ClinicalText.Strength(v.Scores.Confidence) + " (" + Ui.Pct(v.Scores.Confidence) + ")",
                    string.IsNullOrEmpty(v.QualityStatus) ? "Not recorded" : ClinicalText.PlainQuality(v.QualityStatus)
                };
                for (int c = 0; c < cells.Length; c++)
                {
                    TextBlock t = Ui.Text(cells[c], 13, c == 1, c == 1 && v.Scores != null ? Ui.Brush(Ui.ClassColor(v.Scores.PredictedClass)) : Ui.Ink, new Thickness(0, 4, 0, 4));
                    Grid.SetColumn(t, c); Grid.SetRow(t, row); g.Children.Add(t);
                }
                Button open = Ui.Btn("Show photo", (o, e) => { _viewName = view; RenderViewer(); }, "SmallButton");
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
                Button b = Ui.Btn(SkinDevApp.V6.V6SessionSummary.LabelFor(name), (o, e) => { _viewName = name; RenderViewer(); }, name == _viewName ? "PrimaryButton" : "SecondaryButton");
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

            bool finalModel = File.Exists(Path.Combine(cur.RecordFolder ?? "", SkinDevApp.V6.V6Archive.FileName));
            string[] modes = finalModel ? new[] { "Original", "Marked areas" } : new[] { "Original", "Affected Area / Localization", "Grad-CAM++" };
            if (!modes.Contains(_mode)) _mode = "Original";
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
            SkinDevApp.V6.UnifiedAssessment fa = finalModel ? SkinDevApp.V6.V6Archive.Read(cur.RecordFolder, out string _) : null;
            switch (_mode)
            {
                case "Marked areas":
                    title = "Areas marked by the lesion detectors";
                    path = SkinDevApp.V6.V6ResultsView.MarkedAreasPath(cur);
                    note = fa == null ? "No AI result is stored for this view." :
                           SkinDevApp.V6.V6SessionSummary.AreasLine(SkinDevApp.V6.ClinicalSummary.From(fa, SkinDevApp.V6.AIResultStore.FaceBoxOf(cur.RecordFolder))) +
                           ". The boxes are possible locations, not confirmed lesions and not a lesion count.";
                    break;
                case "Heat map":
                    string lead = fa == null ? null : SkinDevApp.V6.V6Heatmaps.LeadCondition(fa);
                    title = "Heat map" + (lead == null ? "" : " · " + lead);
                    path = lead == null ? null : SkinDevApp.V6.V6Heatmaps.OverlayPath(cur.RecordFolder, lead);
                    note = path == null ? "No heat map is stored for this view." :
                           "Where the lesion detectors found evidence for " + lead + " (colour = the condition, strength = detector confidence). It is not an outline of the affected skin.";
                    break;
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

            sp.Children.Add(Ui.Text(SkinDevApp.V6.V6SessionSummary.LabelFor(cur.ViewType) + "  ·  " + title, 12.5, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
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
            sp.Children.Add(Ui.Text("Dermatologist review", 17, true));
            sp.Children.Add(Ui.Text("For each condition, choose whether you confirm what the AI reported. Nothing is filled in for you, and saving never changes the AI result.", 12, false, Ui.Muted));

            sp.Children.Add(ConditionVerification());

            sp.Children.Add(Label("Notes (optional)"));
            _notes = Field(true); _notes.MinHeight = 90; sp.Children.Add(_notes);

            sp.Children.Add(Label("Reviewer"));
            var idRow = new UniformGrid { Columns = 3 };
            _id = Field(); _name = Field(); _cred = Field();
            foreach (var pair in new[] { Tuple.Create("Reviewer ID *", _id), Tuple.Create("Name (optional)", _name), Tuple.Create("Registration no. (optional)", _cred) })
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                col.Children.Add(Ui.Text(pair.Item1, 11.5, false, Ui.Muted));
                col.Children.Add(pair.Item2);
                idRow.Children.Add(col);
            }
            sp.Children.Add(idRow);

            sp.Children.Add(Label("Outcome of this review *"));
            _status = Combo(StatusShown); _status.SelectedIndex = 1; sp.Children.Add(_status);
            sp.Children.Add(Ui.Text("Completed needs a Confirmed or Not Confirmed answer for every condition. It moves the scan to the verified records.", 11, false, Ui.Muted, new Thickness(0, 4, 0, 0)));

            Button save = Ui.Btn("Save assessment", (o, e) => Save(), "PrimaryButton");
            save.Margin = new Thickness(0, 16, 0, 0);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(save);
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private readonly Dictionary<string, string> _aiStatus = new Dictionary<string, string>();

        /// <summary>One line per condition: what the AI reported (plain words, read-only) and the reviewer's own answer. Every answer starts as Pending Review.</summary>
        private UIElement ConditionVerification()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };
            _aiStatus.Clear();
            var ai = new Dictionary<string, string>();
            try
            {
                var v6 = SkinDevApp.V6.V6SessionSummary.For(_d);
                if (v6 != null)
                {
                    var all = new List<SkinDevApp.V6.ClinicalFinding>(v6.Clinical.Findings);
                    foreach (var vw in v6.Views) all.AddRange(vw.Clinical.Findings);
                    foreach (var grp in all.GroupBy(f => f.Condition))
                    {
                        string st = grp.Any(f => f.Status == SkinDevApp.V6.ClinicalSummary.Detected) ? SkinDevApp.V6.ClinicalSummary.Detected
                                  : grp.Any(f => f.Status == SkinDevApp.V6.ClinicalSummary.Possible || f.Status == SkinDevApp.V6.ClinicalSummary.Inconclusive) ? SkinDevApp.V6.ClinicalSummary.Possible
                                  : SkinDevApp.V6.ClinicalSummary.NotDetected;
                        _aiStatus[grp.Key] = st;
                        ai[grp.Key] = st == SkinDevApp.V6.ClinicalSummary.Detected ? "AI result: found" : st == SkinDevApp.V6.ClinicalSummary.Possible ? "AI result: possible, not certain" : "AI result: not found";
                    }
                }
            }
            catch { }
            _condRows.Clear();
            foreach (string cond in new[] { "Acne", "Hyperpigmentation", "Eczema" })
            {
                var head = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var combo = Combo(ConditionReview.All); combo.SelectedIndex = 0; combo.Width = 260; DockPanel.SetDock(combo, Dock.Right);
                combo.SelectionChanged += (o, e) => SuggestOutcome();
                head.Children.Add(combo);
                var left = new StackPanel();
                left.Children.Add(Ui.Text(cond, 14, true, Ui.Ink));
                string aiText;
                left.Children.Add(Ui.Text(ai.TryGetValue(cond, out aiText) ? aiText : "AI result: not recorded for this scan", 12, false, Ui.Muted));
                head.Children.Add(left);
                sp.Children.Add(head);
                var comment = Field(true); comment.MinHeight = 44;
                sp.Children.Add(Ui.Text("Comment (optional)", 11, false, Ui.Muted));
                sp.Children.Add(comment);
                sp.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 10, 0, 4) });
                _condRows.Add(Tuple.Create(cond, combo, comment));
            }
            return sp;
        }

        /// <summary>Preselect a sensible outcome from the answers (the reviewer can still change it).</summary>
        private void SuggestOutcome()
        {
            if (_status == null) return;
            bool decided = _condRows.All(r => (string)r.Item2.SelectedItem == ConditionReview.Confirmed || (string)r.Item2.SelectedItem == ConditionReview.NotConfirmed);
            _status.SelectedIndex = decided ? 0 : 1;
        }

        /// <summary>What the reviewer's answers say about the AI result, worked out from those answers only: Agree / Partially agree / Disagree, or null while anything is open.</summary>
        private string DeriveAgreement(out string assessment)
        {
            var confirmed = _condRows.Where(r => (string)r.Item2.SelectedItem == ConditionReview.Confirmed).Select(r => r.Item1).ToList();
            assessment = confirmed.Count == 0 ? "No condition confirmed" : string.Join(", ", confirmed);
            if (_condRows.Any(r => (string)r.Item2.SelectedItem == ConditionReview.Pending || (string)r.Item2.SelectedItem == ConditionReview.FurtherAssessment) || _aiStatus.Count == 0) return null;
            int match = 0;
            foreach (var r in _condRows)
            {
                bool aiFound; string st;
                aiFound = _aiStatus.TryGetValue(r.Item1, out st) && st == SkinDevApp.V6.ClinicalSummary.Detected;
                bool derm = (string)r.Item2.SelectedItem == ConditionReview.Confirmed;
                if (aiFound == derm) match++;
            }
            return match == _condRows.Count ? "Agree" : match == 0 ? "Disagree" : "Partially agree";
        }

        private void Save()
        {
            string status = _status.SelectedIndex < 0 ? ReviewStatus.ValidationDraft : StatusStored[_status.SelectedIndex];
            if (string.IsNullOrWhiteSpace(_id.Text)) { LumyDialog.Show("Please enter your reviewer ID.", "LUMYVUE Dermatologist Review"); return; }
            if (status == ReviewStatus.ValidationCompleted && _condRows.Any(r => (string)r.Item2.SelectedItem == ConditionReview.Pending || (string)r.Item2.SelectedItem == ConditionReview.FurtherAssessment))
            { LumyDialog.Show("A review can be marked completed only when every condition is Confirmed or Not Confirmed. Answer each condition, or choose \"Requires further review\" or \"Save as draft\".", "LUMYVUE Dermatologist Review"); return; }

            try
            {
                string label;
                string agreement = DeriveAgreement(out label);
                StudyRepository.SaveDermatologistValidation(new DermatologistValidation
                {
                    ScanSessionID = _sessionId,
                    DermatologistID = _id.Text.Trim(),
                    ValidatorName = string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim(),
                    ProfessionalRole = "Dermatologist",
                    CredentialReference = string.IsNullOrWhiteSpace(_cred.Text) ? null : _cred.Text.Trim(),
                    DermatologistAssessment = label,
                    AgreementWithAI = agreement,
                    AgreementWithResearcher = _d.Evaluations.Count == 0 ? "No researcher assessment yet" : null,
                    Notes = _notes.Text,
                    ValidationStatus = status,
                    ConditionReviews = _condRows.Select(r => new ConditionReview { Condition = r.Item1, Status = (string)r.Item2.SelectedItem, Observation = NullIfEmpty(r.Item3.Text) }).ToList()
                }, _condRows.Where(r => (string)r.Item2.SelectedItem == ConditionReview.Confirmed).Select(r => r.Item1).ToList());

                LumyDialog.Show("Assessment saved. Scan status: " + (ReviewStatus.FromValidation(status) ?? "unchanged (draft)") + ". The AI result is unchanged.", "LUMYVUE Dermatologist Review",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                LumyDialog.Show("Could not save the assessment: " + ex.Message, "LUMYVUE Dermatologist Review", MessageBoxButton.OK, MessageBoxImage.Error);
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
            foreach (DermatologistValidation v in _d.Validations.Take(3))
            {
                var rows = new List<KeyValuePair<string, string>>
                {
                    Ui.KV("Date / outcome", v.ValidationDate + "  ·  " + (ReviewStatus.FromValidation(v.ValidationStatus) ?? "Draft")),
                    Ui.KV("Confirmed", v.DermatologistAssessment ?? "—"),
                    Ui.KV("Agreement with AI", v.AgreementWithAI == "Uncertain" ? "Not decided" : v.AgreementWithAI ?? "—")
                };
                if (!string.IsNullOrWhiteSpace(v.Notes)) rows.Add(Ui.KV("Notes", v.Notes));
                sp.Children.Add(Ui.KeyValues(rows.ToArray(), 170));
            }
            if (_d.Validations.Count > 3) sp.Children.Add(Ui.Text("Older assessments are kept in the record.", 11.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            return Ui.Card(sp);
        }
    }
}
