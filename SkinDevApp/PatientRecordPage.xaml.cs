// PatientRecordPage.xaml.cs - Participant Records (researcher / admin).
// Search by PatientID -> load information, skin profile, consent, latest result and the
// full analysis history; selecting a scan shows its exact saved data (never re-run).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class PatientRecordPage : Page
    {
        private Participant _p;
        private DataGrid _history;
        private StackPanel _detailHost;
        private bool _ready;

        public PatientRecordPage() : this(null, false) { }
        public PatientRecordPage(string participantId) : this(participantId, false) { }
        public PatientRecordPage(bool pendingOnly) : this(null, pendingOnly) { }

        private PatientRecordPage(string participantId, bool pendingOnly)
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Participant Records");

            if (!AppSession.IsResearcher)
            {
                RecordPanel.Children.Add(Ui.Card(Ui.Text("Participant Records are available to signed-in researchers / administrators only.", 14, false, Ui.Bad)));
                IsEnabled = true;
                SearchBtn.IsEnabled = false;
                return;
            }

            _ready = true;
            RefreshList();

            if (!string.IsNullOrEmpty(participantId)) { IdSearchTxt.Text = participantId; LoadParticipant(participantId); }
            else if (pendingOnly) ShowPending();
            else ShowWelcome();
        }

        // --------------------------------------------------------------- list --

        private void RefreshList()
        {
            if (!_ready) return;
            string status = (FilterStatus.SelectedItem as ComboBoxItem)?.Content as string;
            string date = FilterDate.SelectedDate.HasValue ? FilterDate.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
            List<Participant> list;
            try { list = StudyRepository.SearchParticipants(FilterTxt.Text, date, status == "All" ? "" : status); }
            catch (Exception ex) { ListCountTxt.Text = "Database error: " + ex.Message; return; }

            ParticipantList.Items.Clear();
            foreach (Participant p in list)
            {
                var sp = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
                sp.Children.Add(Ui.Text(p.ParticipantID + "  ·  " + p.FullName, 12.5, true, Ui.Ink, new Thickness(0)));
                sp.Children.Add(Ui.Text("Registered " + (p.RegisteredAt ?? "").Split(' ')[0] + "  ·  " + p.Status, 11, false, Ui.Muted, new Thickness(0)));
                ParticipantList.Items.Add(new ListBoxItem { Content = sp, Tag = p.ParticipantID });
            }
            ListCountTxt.Text = list.Count + " participant(s)";
        }

        private void Filter_Changed(object sender, EventArgs e) => RefreshList();

        private void ParticipantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ParticipantList.SelectedItem as ListBoxItem;
            if (item == null) return;
            IdSearchTxt.Text = (string)item.Tag;
            LoadParticipant((string)item.Tag);
        }

        // ------------------------------------------------------------- search --

        private void IdSearchTxt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SearchBtn_Click(sender, e);
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            string text = (IdSearchTxt.Text ?? "").Trim();
            if (text.Length == 0) return;

            List<Participant> found;
            try { found = StudyRepository.SearchParticipants(text, "", ""); }
            catch (Exception ex)
            {
                RecordPanel.Children.Clear();
                RecordPanel.Children.Add(Ui.Card(Ui.Text("Database error: " + ex.Message, 13, false, Ui.Bad)));
                return;
            }

            if (found.Count == 1) { IdSearchTxt.Text = found[0].ParticipantID; LoadParticipant(found[0].ParticipantID); return; }
            if (found.Count == 0) { LoadParticipant(StudyRepository.NormaliseParticipantId(text)); return; }   // shows the "not found" card

            // several matches: never pick one silently - show them as clickable rows
            _p = null;
            RecordPanel.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(found.Count + " participants match \"" + text + "\"", 18, true));
            sp.Children.Add(Ui.Text("Click the participant to open the record.", 13, false, Ui.Muted));
            foreach (Participant p in found.Take(100))
            {
                string id = p.ParticipantID;
                Button b = Ui.Btn(p.ParticipantID + " — " + p.FullName + (p.Age.HasValue ? " — Age " + p.Age : "") + (string.IsNullOrEmpty(p.Sex) ? "" : " — " + p.Sex)
                                  + (p.Status == "Withdrawn" ? " — WITHDRAWN" : ""),
                                  (s, e) => { IdSearchTxt.Text = id; LoadParticipant(id); }, "SmallButton");
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(b);
            }
            RecordPanel.Children.Add(Ui.Card(sp));
        }

        private void ShowWelcome()
        {
            RecordPanel.Children.Clear();
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Find a participant", 18, true));
            sp.Children.Add(Ui.Text("Type a Patient ID (PS-0001) or part of a participant's name and press SEARCH, or pick a participant from the list. " +
                                    "The record, consent status, latest result and the full analysis history load automatically.", 13, false, Ui.Muted));
            RecordPanel.Children.Add(Ui.Card(sp));
        }

        private void LoadParticipant(string id)
        {
            RecordPanel.Children.Clear();
            _p = null;

            Participant p;
            try { p = StudyRepository.GetParticipant(id); }
            catch (Exception ex)
            {
                RecordPanel.Children.Add(Ui.Card(Ui.Text("Database error: " + ex.Message, 13, false, Ui.Bad)));
                return;
            }

            if (p == null)
            {
                var nf = new StackPanel();
                nf.Children.Add(Ui.Text("Participant not found", 18, true, Ui.Bad));
                nf.Children.Add(Ui.Text("No participant has the ID " + id + ". Check the ID, or register a new participant (a new ID is generated; nothing is created from this search).",
                    13, false, Ui.Ink));
                Button reg = Ui.Btn("REGISTER NEW PARTICIPANT", (s, e) => Nav.Go(new RegistrationPage()), "PrimaryButton");
                reg.HorizontalAlignment = HorizontalAlignment.Left;
                reg.Margin = new Thickness(0, 10, 0, 0);
                nf.Children.Add(reg);
                RecordPanel.Children.Add(Ui.Card(nf));
                return;
            }

            _p = p;
            List<ScanSessionRow> sessions = StudyRepository.SessionsForParticipant(p.ParticipantID);
            SkinProfile sp = StudyRepository.GetSkinProfile(p.ParticipantID) ?? new SkinProfile();
            Consent consent = StudyRepository.GetLatestConsent(p.ParticipantID);

            RecordPanel.Children.Add(LatestCard(sessions.FirstOrDefault()));

            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border info = InfoCard(p, consent);
            Border skin = SkinCard(sp);
            Grid.SetColumn(skin, 1);
            two.Children.Add(info);
            two.Children.Add(skin);
            RecordPanel.Children.Add(two);

            RecordPanel.Children.Add(HistoryCard(sessions));

            _detailHost = new StackPanel();
            RecordPanel.Children.Add(_detailHost);
            if (sessions.Count > 0) _history.SelectedIndex = 0;
            RecordScroll.ScrollToTop();
        }

        // -------------------------------------------------------------- cards --

        private Border LatestCard(ScanSessionRow s)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            if (s != null)
            {
                Button view = Ui.Btn("VIEW ANALYSIS", (o, e) => Nav.Go(new ResultsPage(s.ScanSessionID)), "PrimaryButton");
                view.Height = 36;
                DockPanel.SetDock(view, Dock.Right);
                head.Children.Add(view);
            }
            head.Children.Add(Ui.Text("Latest Analysis", 16, true));
            sp.Children.Add(head);

            if (s == null)
            {
                sp.Children.Add(Ui.Text("No scans yet for this participant.", 13, false, Ui.Muted));
                Button scan = Ui.Btn("Start a scan", (o, e) => Workflow.ContinueToScan(_p.ParticipantID), "SecondaryButton");
                scan.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(scan);
                return Ui.Card(sp);
            }

            var g = new UniformGrid { Columns = 3 };
            g.Children.Add(Ui.KeyValues(new[] { Ui.KV("ScanSessionID", s.DisplayId), Ui.KV("Date / time", s.StartedAt) }, 110));
            g.Children.Add(Ui.KeyValues(new[] { Ui.KV("Overall class", s.OverallPredictedClass ?? "no result"), Ui.KV("Model class score", s.ScoreText) }, 120));
            g.Children.Add(Ui.KeyValues(new[] { Ui.KV("Completeness", s.ScanMode == "Single" ? "single capture" : s.CompletionText + " views"), Ui.KV("Model version", s.ModelLabel) }, 110));
            sp.Children.Add(g);
            return Ui.Card(sp);
        }

        private Border InfoCard(Participant p, Consent consent)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            Button edit = Ui.Btn("Edit", (o, e) => Nav.Go(new RegistrationPage(p.ParticipantID)), "SmallButton");
            DockPanel.SetDock(edit, Dock.Right);
            head.Children.Add(edit);
            head.Children.Add(Ui.Text("Patient Information", 15, true));
            sp.Children.Add(head);

            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("ParticipantID", p.ParticipantID),
                Ui.KV("Full name", p.FullName),
                Ui.KV("Age", p.Age.HasValue ? p.Age.Value.ToString(CultureInfo.InvariantCulture) : null),
                Ui.KV("Date of birth", p.DateOfBirth),
                Ui.KV("Sex", p.Sex),
                Ui.KV("Contact", p.Contact),
                Ui.KV("Registered", p.RegisteredAt + (string.IsNullOrEmpty(p.OperatorID) ? "" : " by " + p.OperatorID)),
                Ui.KV("Record status", p.Status),
                Ui.KV("Consent", consent == null ? "Not recorded"
                    : consent.Decision + " " + consent.RecordedAt + (consent.AllowsScanning ? "" : " (scanning not allowed)") +
                      " · future model use: " + (consent.FutureModelUseConsent ? "yes" : "no"))
            }, 120));

            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button newScan = Ui.Btn("New scan", (o, e) => Workflow.ContinueToScan(p.ParticipantID), "SmallButton");
            Button recConsent = Ui.Btn("Record consent", (o, e) => Nav.Go(new ConsentPage(p.ParticipantID)), "SmallButton");
            newScan.IsEnabled = recConsent.IsEnabled = p.Status != "Withdrawn";
            actions.Children.Add(newScan);
            actions.Children.Add(recConsent);
            sp.Children.Add(actions);
            if (p.Status == "Withdrawn")
                sp.Children.Add(Ui.Text("Withdrawn: this participant's data is excluded from the Research Dashboard, and no new scans or consent can be recorded.",
                    11, true, Ui.Bad, new Thickness(0, 8, 0, 0)));
            return Ui.Card(sp);
        }

        private Border SkinCard(SkinProfile s)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Skin Profile", 15, true));
            string concerns = s.Concerns + (string.IsNullOrWhiteSpace(s.ConcernOther) ? "" : " (" + s.ConcernOther + ")");
            string regions = s.Regions + (string.IsNullOrWhiteSpace(s.RegionOther) ? "" : " (" + s.RegionOther + ")");
            var care = new[] { Tuple.Create("Cleanser", s.Cleanser), Tuple.Create("Moisturizer", s.Moisturizer), Tuple.Create("Sunscreen", s.Sunscreen),
                               Tuple.Create("Acne tx", s.AcneTreatment), Tuple.Create("Eczema tx", s.EczemaTreatment), Tuple.Create("Pigmentation tx", s.PigmentationTreatment),
                               Tuple.Create("Other", s.OtherProducts) }
                .Where(t => !string.IsNullOrWhiteSpace(t.Item2)).Select(t => t.Item1 + ": " + t.Item2);

            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("Skin type", s.GeneralSkinType),
                Ui.KV("Sensitivity", s.Sensitivity),
                Ui.KV("Reported concerns", concerns),
                Ui.KV("Regions", regions),
                Ui.KV("Duration", s.ConcernDuration),
                Ui.KV("Current skincare", string.Join("; ", care)),
                Ui.KV("Recent procedures", s.RecentProcedures),
                Ui.KV("Other responses", s.OtherResponses)
            }, 140));
            sp.Children.Add(Ui.Text("Shown from the saved registration. Editing requires the Edit button and never changes scan results.", 10.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            return Ui.Card(sp);
        }

        private Border HistoryCard(List<ScanSessionRow> sessions)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Analysis History (" + sessions.Count + ")", 16, true));
            sp.Children.Add(Ui.Text("Each visit is a separate scan session. Click a column header to sort; select a row for its saved details.", 11.5, false, Ui.Muted));

            _history = new DataGrid { Style = (Style)FindResource("Grid"), MaxHeight = 260, Margin = new Thickness(0, 6, 0, 0) };
            void Col(string header, string path, double w) =>
                _history.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(w, DataGridLengthUnitType.Star) });
            Col("ScanSessionID", "DisplayId", 1.4);
            Col("Date", "DateText", 1.0);
            Col("Time", "TimeText", 0.6);
            Col("Overall prediction", "OverallPredictedClass", 1.3);
            Col("Score", "ScoreText", 0.7);
            Col("F/L/R", "CompletionText", 0.6);
            Col("Status", "ScanStatus", 0.9);
            Col("Validation", "ValidationStatus", 1.4);
            Col("Model version", "ModelLabel", 1.6);
            _history.ItemsSource = sessions;
            _history.SelectionChanged += (s, e) => ShowDetail(_history.SelectedItem as ScanSessionRow);
            sp.Children.Add(_history);
            return Ui.Card(sp);
        }

        // --------------------------------------------------------- selected scan --

        private void ShowDetail(ScanSessionRow row)
        {
            if (_detailHost == null) return;
            _detailHost.Children.Clear();
            if (row == null) return;

            SessionDetail d = StudyRepository.GetSessionDetail(row.ScanSessionID);
            if (d == null) return;
            _detailHost.Children.Add(DetailCard(d));
            _detailHost.Children.Add(EvidenceCard(d));
        }

        internal static Border DetailCard(SessionDetail d)
        {
            ScanSessionRow s = d.Session;
            var sp = new StackPanel();
            var head = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            buttons.Children.Add(Ui.Btn("Results", (o, e) => Nav.Go(new ResultsPage(s.ScanSessionID)), "SmallButton"));
            buttons.Children.Add(Ui.Btn("Researcher Verification", (o, e) =>
            {
                new ResearcherVerificationWindow(s.ScanSessionID) { Owner = Application.Current.MainWindow }.ShowDialog();
            }, "SmallButton"));
            buttons.Children.Add(Ui.Btn("Dermatologist Review", (o, e) =>
            {
                new DermatologistReviewWindow(s.ScanSessionID) { Owner = Application.Current.MainWindow }.ShowDialog();
            }, "SmallButton"));
            buttons.Children.Add(Ui.Btn("Report", (o, e) => ResearchReportWindow.Open(s.ScanSessionID), "SmallButton"));
            head.Children.Add(buttons);
            head.Children.Add(Ui.Text("Selected Analysis — " + s.DisplayId, 16, true));
            sp.Children.Add(head);

            ModelVersion m = d.Model;
            sp.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("PatientID", s.ParticipantID ?? "unlinked"),
                Ui.KV("ScanSessionID", s.ScanSessionID),
                Ui.KV("Date / time", s.StartedAt + (s.CompletedAt != null ? "  →  " + s.CompletedAt : "")),
                Ui.KV("Model", m == null ? "unknown" : (m.ModelName + " (" + m.Architecture + ") · run " + m.RunName + " · ONNX " + ModelVersion.Short(m.OnnxSha256) +
                                                       " · Keras " + ModelVersion.Short(m.KerasSha256))),
                Ui.KV("Overall predicted class", s.OverallPredictedClass ?? "no result"),
                Ui.KV("View consistency", s.ScanMode == "Single" ? "single capture" : (s.ViewDisagreement ? "⚠ Disagreement — review individual angles" : "Consistent")),
                Ui.KV("Scan status", s.ScanStatus + " · " + (s.ScanMode == "Single" ? "single capture" : s.CompletionText + " views"))
            }, 160));

            var views = new UniformGrid { Columns = Math.Max(1, d.Views.Count), Margin = new Thickness(0, 10, 0, 0) };
            foreach (CaptureViewRow v in d.Views)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                col.Children.Add(Ui.Text(v.ViewType + " result", 12.5, true));
                if (v.Scores == null) col.Children.Add(Ui.Text("not analysed", 11.5, false, Ui.Bad));
                else
                {
                    col.Children.Add(Ui.Text(v.Scores.PredictedClass + " " + Ui.Pct(v.Scores.Confidence), 12, true, Ui.Brush(Ui.ClassColor(v.Scores.PredictedClass))));
                    col.Children.Add(Ui.ScoreBars(v.Scores.AsArray(), 5, compact: true));
                }
                views.Children.Add(col);
            }
            sp.Children.Add(views);
            sp.Children.Add(Ui.Text("Model Class Scores per view (Acne, Hyperpigmentation, Eczema, Normal Skin). " + StudyText.ScoresNote, 10.5, false, Ui.Muted));

            if (d.Evaluations.Count > 0 || d.Validations.Count > 0)
            {
                ResearcherEvaluation ev = d.Evaluations.FirstOrDefault();
                DermatologistValidation dv = d.Validations.FirstOrDefault();
                sp.Children.Add(Ui.KeyValues(new[]
                {
                    Ui.KV("Researcher assessment", ev == null ? "pending" : ev.ResearcherClassification + " · " + ev.AgreementWithAI + " · " + ev.ResearcherID),
                    Ui.KV("Dermatologist validation", dv == null ? "pending" : (dv.DermatologistAssessment ?? "—") + " · " + dv.ValidationStatus + " · " + dv.DermatologistID)
                }, 160));
            }
            return Ui.Card(sp);
        }

        /// <summary>Front / Left / Right tabs: Original + Acne + Eczema + Hyperpigmentation + Normal (15 images for a complete scan).</summary>
        internal static Border EvidenceCard(SessionDetail d)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            bool hasFolder = !string.IsNullOrEmpty(d.Session.SessionFolder) && System.IO.Directory.Exists(d.Session.SessionFolder);
            Button gallery = Ui.Btn("Open comparison gallery", (o, e) =>
            {
                try { new ComparisonGalleryWindow(d.Session.SessionFolder) { Owner = Application.Current.MainWindow }.Show(); }
                catch (Exception ex) { MessageBox.Show("Could not open the gallery: " + ex.Message, "LUMYVUE"); }
            }, "SmallButton");
            gallery.IsEnabled = hasFolder;
            DockPanel.SetDock(gallery, Dock.Right);
            head.Children.Add(gallery);

            int images = d.Views.Sum(v => (string.IsNullOrEmpty(v.AnalysedImagePath ?? v.OriginalImagePath) ? 0 : 1) + v.Maps.Count(m => m.OverlayPath != null));
            head.Children.Add(Ui.Text("Analysis Images and CAM Comparison (" + images + " images)", 16, true));
            sp.Children.Add(head);

            var tabs = new TabControl { Margin = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(0) };
            string[] order = { "Acne", "Eczema", "Hyperpigmentation", "Normal" };       // gallery order requested by the spec
            foreach (CaptureViewRow v in d.Views)
            {
                var row = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(EvidenceCell("Original", v.AnalysedImagePath ?? v.OriginalImagePath, "#9CA3AF", null, v.OriginalImagePath));
                foreach (string cls in order)
                {
                    AttributionMapRow m = v.Maps.FirstOrDefault(x => x.TargetClass == cls);
                    double? score = v.Scores == null ? (double?)null : v.Scores.AsArray()[Array.IndexOf(StudyText.Classes, cls)];
                    string label = (cls == "Normal" ? "Normal Skin" : cls) + " CAM" + (score.HasValue ? "  " + Ui.Pct(score.Value) : "");
                    row.Children.Add(EvidenceCell(label, m?.OverlayPath, Ui.ClassColor(cls), m == null ? "Grad-CAM++ unavailable" : (m.Diffuse ? "diffuse attribution" : m.TopZone), m?.OverlayPath));
                }
                tabs.Items.Add(new TabItem { Header = "  " + v.ViewType.ToUpperInvariant() + "  ", Content = row });
            }
            if (tabs.Items.Count == 0) sp.Children.Add(Ui.Text("No images saved for this scan.", 12, false, Ui.Muted));
            else sp.Children.Add(tabs);

            sp.Children.Add(Ui.Text("The original image is unmodified and always available. " + StudyText.AttributionNote, 10.5, false, Ui.Muted, new Thickness(0, 8, 0, 0)));
            return Ui.Card(sp);
        }

        private static StackPanel EvidenceCell(string title, string imagePath, string colorHex, string note, string openPath)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            Border thumb = Ui.Thumb(imagePath, 120, colorHex);
            thumb.Cursor = Cursors.Hand;
            thumb.ToolTip = "Click to enlarge";
            thumb.MouseLeftButtonUp += (o, e) => ImagePopup.Show(openPath ?? imagePath, title);
            cell.Children.Add(thumb);
            cell.Children.Add(Ui.Text(title, 11, true, Ui.Ink, new Thickness(0, 3, 0, 0)));
            if (!string.IsNullOrEmpty(note)) cell.Children.Add(Ui.Text(note, 10, false, Ui.Muted, new Thickness(0)));
            return cell;
        }

        // ------------------------------------------------------- side buttons --

        private void ShowPending()
        {
            RecordPanel.Children.Clear();
            var sp = new StackPanel();
            List<ScanSessionRow> rows = StudyRepository.PendingVerification();
            sp.Children.Add(Ui.Text("Scans pending researcher verification (" + rows.Count + ")", 16, true));
            sp.Children.Add(Ui.Text("Double-click a scan to open Researcher Verification.", 11.5, false, Ui.Muted));
            sp.Children.Add(SessionGrid(rows, r =>
            {
                new ResearcherVerificationWindow(r.ScanSessionID) { Owner = Application.Current.MainWindow }.ShowDialog();
                ShowPending();
            }));
            RecordPanel.Children.Add(Ui.Card(sp));
        }

        private void ShowUnlinked()
        {
            RecordPanel.Children.Clear();
            var sp = new StackPanel();
            List<ScanSessionRow> rows = StudyRepository.UnlinkedSessions();
            sp.Children.Add(Ui.Text("Unlinked / legacy scans (" + rows.Count + ")", 16, true));
            sp.Children.Add(Ui.Text("Scans saved before PatientIDs existed, or analysed without a participant. Double-click to view; " +
                                    "select one and use 'Link to participant' to file it under a PatientID.", 11.5, false, Ui.Muted));
            DataGrid g = SessionGrid(rows, r => Nav.Go(new ResultsPage(r.ScanSessionID)));
            g.Columns.Insert(1, new DataGridTextColumn { Header = "Old label", Binding = new Binding("LegacyPatientLabel"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            sp.Children.Add(g);

            var link = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var idBox = new TextBox { Style = (Style)FindResource("Field"), Width = 160 };
            Button linkBtn = Ui.Btn("Link to participant", (o, e) =>
            {
                var r = g.SelectedItem as ScanSessionRow;
                string pid = StudyRepository.NormaliseParticipantId(idBox.Text);
                if (r == null || StudyRepository.GetParticipant(pid) == null)
                {
                    MessageBox.Show("Select a scan and enter an existing PatientID.", "LUMYVUE Records");
                    return;
                }
                if (MessageBox.Show("File scan " + r.DisplayId + " under " + pid + "? This is recorded in the audit log.", "LUMYVUE Records",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                StudyRepository.LinkSessionToParticipant(r.ScanSessionID, pid);
                ShowUnlinked();
            }, "SmallButton");
            link.Children.Add(Ui.Text("PatientID: ", 12, true, Ui.Ink, new Thickness(0, 8, 6, 0)));
            link.Children.Add(idBox);
            link.Children.Add(linkBtn);
            sp.Children.Add(link);
            RecordPanel.Children.Add(Ui.Card(sp));
        }

        private DataGrid SessionGrid(List<ScanSessionRow> rows, Action<ScanSessionRow> open)
        {
            var g = new DataGrid { Style = (Style)FindResource("Grid"), MaxHeight = 520, Margin = new Thickness(0, 6, 0, 0) };
            void Col(string header, string path, double w) =>
                g.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(w, DataGridLengthUnitType.Star) });
            Col("ScanSessionID", "DisplayId", 1.4);
            Col("Participant", "ParticipantID", 0.9);
            Col("Date", "StartedAt", 1.3);
            Col("Overall prediction", "OverallPredictedClass", 1.2);
            Col("Score", "ScoreText", 0.6);
            Col("F/L/R", "CompletionText", 0.6);
            Col("Validation", "ValidationStatus", 1.3);
            g.ItemsSource = rows;
            g.MouseDoubleClick += (s, e) => { var r = g.SelectedItem as ScanSessionRow; if (r != null) open(r); };
            return g;
        }

        private void PendingBtn_Click(object sender, RoutedEventArgs e) { if (_ready) ShowPending(); }
        private void UnlinkedBtn_Click(object sender, RoutedEventArgs e) { if (_ready) ShowUnlinked(); }
        private void RegisterBtn_Click(object sender, RoutedEventArgs e) => Nav.Go(new RegistrationPage());

        private async void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var b = (Button)sender;
            b.IsEnabled = false;
            try
            {
                int failed = 0;
                int n = await Task.Run(() => SessionImporter.ImportArchive(out failed));
                MessageBox.Show(n + " saved scan(s) were added to the database" + (failed > 0 ? "; " + failed + " folder(s) could not be read" : "") +
                                ".\n\nScans whose patient label is a registered PatientID were linked; the rest are listed under 'Unlinked / legacy scans'.",
                                "LUMYVUE Import", MessageBoxButton.OK, MessageBoxImage.Information);
                if (_p != null) LoadParticipant(_p.ParticipantID); else ShowUnlinked();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import failed: " + ex.Message, "LUMYVUE Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { b.IsEnabled = true; }
        }
    }
} 