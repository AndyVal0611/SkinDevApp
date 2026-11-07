// ============================================================================
// RecordsDatabasePage.cs  -  namespace SkinDevApp.Views
//
// "Records & database": the study database inside the application, for signed-in staff. No external database tool and no SQL.
//   Patients              search, register, edit, open the record, start a scan, mark active / withdrawn
//   Scans                 search and filter by verification status and AI outcome; see the AI result; set the verification status;
//                         link an unassigned scan to a patient; open the results, the dermatologist review or the scan folder
//   AI findings           every condition reading of every photograph (result, certainty, facial regions, origin)
//   Verification records  every dermatologist and researcher review
//   Activity log          the audit trail of every change
// Every change uses the same audited repository methods as the rest of the application, so it is reflected everywhere at once.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class RecordsDatabasePage : Page
    {
        private readonly StackPanel _summary = new StackPanel();
        private readonly TabControl _tabs = new TabControl { Margin = new Thickness(0, 4, 0, 0) };
        private readonly List<Action> _reloads = new List<Action>();
        private readonly string _startTab;

        public RecordsDatabasePage(string startTab = null)
        {
            _startTab = startTab;
            Title = "Records & database";
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Ui.Header("Records & database"));
            var body = new StackPanel { Margin = new Thickness(28, 18, 28, 24) };
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;

            if (!AppSession.IsResearcher)
            {
                body.Children.Add(Ui.Card(Ui.Text("The records and database screen is only available to signed-in staff.", 14, false, Ui.Bad)));
                return;
            }
            body.Children.Add(Ui.Card(_summary, new Thickness(0, 0, 0, 12)));
            body.Children.Add(_tabs);
            _tabs.Items.Add(PatientsTab());
            _tabs.Items.Add(ScansTab());
            _tabs.Items.Add(FindingsTab());
            _tabs.Items.Add(VerificationsTab());
            _tabs.Items.Add(AuditTab());
            if (_startTab != null) foreach (TabItem t in _tabs.Items) if ((string)t.Tag == _startTab) _tabs.SelectedItem = t;
            Loaded += (s, e) => { RenderSummary(); foreach (Action a in _reloads) a(); };
            SkinDevApp.V6.RecordsMaintenance.StatusChanged += OnMaintenance;
            Unloaded += (s, e) => SkinDevApp.V6.RecordsMaintenance.StatusChanged -= OnMaintenance;
        }

        private void OnMaintenance() => Dispatcher.BeginInvoke(new Action(RenderSummary));

        // ------------------------------------------------------------------ summary

        private void RenderSummary()
        {
            _summary.Children.Clear();
            var head = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallBtn("Refresh", (s, e) => { RenderSummary(); foreach (Action a in _reloads) a(); }));
            buttons.Children.Add(SmallBtn("Open records folder", (s, e) => OpenFolder(DataLocation.RecordsDir)));
            buttons.Children.Add(SmallBtn("Back up database", (s, e) => Backup()));
            buttons.Children.Add(SmallBtn("Check database", (s, e) => Check()));
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            head.Children.Add(Ui.Text("Study records", 16, true, Ui.Ink, new Thickness(0, 4, 0, 0)));
            _summary.Children.Add(head);
            try
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
                void Stat(string label, int n, string hex)
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 0, 28, 6) };
                    sp.Children.Add(Ui.Text(n.ToString("N0"), 22, true, Ui.Brush(hex), new Thickness(0)));
                    sp.Children.Add(Ui.Text(label, 12, false, Ui.Muted, new Thickness(0)));
                    wrap.Children.Add(sp);
                }
                Stat("patients", RecordsQueries.Count("SELECT COUNT(*) FROM Participants;"), "#3A3B2C");
                Stat("scans", RecordsQueries.Count("SELECT COUNT(*) FROM ScanSessions;"), "#3A3B2C");
                Stat("pending verification", RecordsQueries.Count("SELECT COUNT(*) FROM ScanSessions WHERE COALESCE(ReviewStatus,'Pending verification')='Pending verification' AND ParticipantID IS NOT NULL;"), "#8A5A00");
                Stat("verified", RecordsQueries.Count("SELECT COUNT(*) FROM ScanSessions WHERE ReviewStatus='Verified';"), "#3E6B3D");
                Stat("require further review", RecordsQueries.Count("SELECT COUNT(*) FROM ScanSessions WHERE ReviewStatus='Requires further review';"), "#9B2F25");
                Stat("unassigned scans", RecordsQueries.Count("SELECT COUNT(*) FROM ScanSessions WHERE ParticipantID IS NULL;"), "#5F5E5A");
                Stat("photographs with an AI result", RecordsQueries.Count("SELECT COUNT(*) FROM AIAssessments;"), "#3A3B2C");
                _summary.Children.Add(wrap);
            }
            catch (Exception ex) { _summary.Children.Add(Ui.Text("The database could not be read: " + ex.Message, 13, false, Ui.Bad)); }
            _summary.Children.Add(Ui.Text("Files: " + DataLocation.RecordsDir + "   ·   Database: " + StudyDatabase.DbPath, 11, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            _summary.Children.Add(Ui.Text("Background upkeep: " + SkinDevApp.V6.RecordsMaintenance.Status, 11, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
        }

        private static Button SmallBtn(string text, RoutedEventHandler click)
        {
            Button b = Ui.Btn(text, click, "SmallButton");
            b.Margin = new Thickness(8, 0, 0, 0);
            return b;
        }

        private void Backup()
        {
            try { string f = RecordsQueries.BackupNow(); LumyDialog.Show("A full copy of the database was saved:\n" + f, "LUMYVUE Records", MessageBoxButton.OK, MessageBoxImage.Information); }
            catch (Exception ex) { LumyDialog.Show("The backup could not be made: " + ex.Message, "LUMYVUE Records", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Check()
        {
            try { LumyDialog.Show(RecordsQueries.HealthCheck(), "LUMYVUE Database check", MessageBoxButton.OK, MessageBoxImage.Information); }
            catch (Exception ex) { LumyDialog.Show("The check could not run: " + ex.Message, "LUMYVUE Database check", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                string f = Directory.Exists(folder ?? "") ? folder : DataLocation.Rebase(folder);
                if (!Directory.Exists(f ?? "")) { LumyDialog.Show("This folder is not on this PC:\n" + folder, "LUMYVUE Records"); return; }
                Process.Start("explorer.exe", "\"" + f + "\"");
            }
            catch (Exception ex) { LumyDialog.Show("The folder could not be opened: " + ex.Message, "LUMYVUE Records"); }
        }

        // ------------------------------------------------------------------ generic tab: filters, table, details

        private sealed class TabParts { public DataGrid Grid; public StackPanel Details; public TextBlock Count; }

        private TabItem Tab(string header, string tag, FrameworkElement filters, Func<DataTable> load, Action<DataRowView, StackPanel> details, out Action reload)
        {
            var parts = new TabParts
            {
                Grid = new DataGrid { Style = (Style)FindResource("Grid"), Height = 430, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 8, 0, 0) },
                Details = new StackPanel(),
                Count = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 6, 0, 0))
            };
            var left = new StackPanel();
            left.Children.Add(filters);
            left.Children.Add(parts.Grid);
            left.Children.Add(parts.Count);

            var detailCard = new Border
            {
                Background = Ui.Brush("#FBF9F3"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(16, 0, 0, 0),
                Child = new ScrollViewer { Content = parts.Details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 }
            };
            parts.Details.Children.Add(Ui.Text("Select a row to see its details and actions.", 13, false, Ui.Muted));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            g.Children.Add(left);
            Grid.SetColumn(detailCard, 1);
            g.Children.Add(detailCard);
            Ui.StackWhenNarrow(g, 1100);

            reload = () =>
            {
                try
                {
                    DataTable t = load();
                    if (parts.Grid.Columns.Count == 0)
                        foreach (DataColumn c in t.Columns)
                            if (!c.ColumnName.StartsWith("_"))
                                parts.Grid.Columns.Add(new DataGridTextColumn
                                {
                                    Header = c.ColumnName, Binding = new Binding("[" + c.ColumnName + "]"),
                                    Width = c.ColumnName == "Notes" || c.ColumnName == "Details" || c.ColumnName == "AI summary" || c.ColumnName == "Facial regions"
                                            ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                                    MaxWidth = 420
                                });
                    parts.Grid.ItemsSource = t.DefaultView;
                    parts.Count.Text = t.Rows.Count == 1 ? "1 record" : t.Rows.Count.ToString("N0") + " records";
                }
                catch (Exception ex) { parts.Count.Text = "Could not read the database: " + ex.Message; }
            };
            Action r = reload;
            parts.Grid.SelectionChanged += (s, e) =>
            {
                var row = parts.Grid.SelectedItem as DataRowView;
                parts.Details.Children.Clear();
                if (row == null) { parts.Details.Children.Add(Ui.Text("Select a row to see its details and actions.", 13, false, Ui.Muted)); return; }
                try { details(row, parts.Details); }
                catch (Exception ex) { parts.Details.Children.Add(Ui.Text("Could not show this record: " + ex.Message, 13, false, Ui.Bad)); }
            };
            _reloads.Add(reload);
            return new TabItem { Header = header, Tag = tag, Content = new Border { Padding = new Thickness(0, 12, 0, 0), Child = g } };
        }

        private static TextBox Search(string hint, Action changed)
        {
            var box = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Width = 280, Margin = new Thickness(0, 0, 12, 0), ToolTip = hint };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) => { timer.Stop(); changed(); };
            box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
            return box;
        }

        private static ComboBox Filter(IEnumerable<string> items, Action changed, double width = 210)
        {
            var c = new ComboBox { Style = (Style)Application.Current.FindResource("Combo"), ItemsSource = items.ToList(), SelectedIndex = 0, Width = width, Margin = new Thickness(0, 0, 12, 0) };
            c.SelectionChanged += (s, e) => changed();
            return c;
        }

        private static StackPanel FilterBar(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (UIElement i in items) sp.Children.Add(i);
            return sp;
        }

        private static StackPanel Labelled(string label, UIElement control)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(label, 11, true, Ui.Muted, new Thickness(0, 0, 0, 3)));
            sp.Children.Add(control);
            return sp;
        }

        private static void Rows(StackPanel target, DataRowView row)
        {
            var kv = new List<KeyValuePair<string, string>>();
            foreach (DataColumn c in row.Row.Table.Columns)
                if (!c.ColumnName.StartsWith("_")) kv.Add(Ui.KV(c.ColumnName, Convert.ToString(row[c.ColumnName])));
            target.Children.Add(Ui.InfoRows(kv, 130, 12.5));
        }

        private static string Str(DataRowView row, string col) => row.Row.Table.Columns.Contains(col) ? Convert.ToString(row[col]) : null;

        private static Button Action(string text, RoutedEventHandler click, string style = "SecondaryButton")
        {
            Button b = Ui.Btn(text, click, style);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0, 6, 0, 0);
            return b;
        }

        // ------------------------------------------------------------------ patients

        private TabItem PatientsTab()
        {
            Action reload = null;
            TextBox q = Search("Patient ID or name", () => reload());
            ComboBox st = Filter(new[] { "All", "Active", "Withdrawn" }, () => reload(), 150);
            Button add = Ui.Btn("Register a new patient", (s, e) => Nav.Go(new RegistrationPage()), "PrimaryButton");
            add.VerticalAlignment = VerticalAlignment.Bottom;
            var bar = FilterBar(Labelled("Search", q), Labelled("Status", st), add);
            TabItem tab = Tab("Patients", "patients", bar, () => RecordsQueries.Patients(q.Text, (string)st.SelectedItem), (row, d) =>
            {
                string pid = Str(row, "Patient ID");
                d.Children.Add(Ui.Text(pid + " · " + Str(row, "Name"), 16, true, Ui.Ink, new Thickness(0, 0, 0, 8)));
                Rows(d, row);
                d.Children.Add(Action("Open patient record", (s, e) => Nav.Go(new PatientRecordPage(pid)), "PrimaryButton"));
                d.Children.Add(Action("Edit patient details", (s, e) => Nav.Go(new RegistrationPage(pid))));
                d.Children.Add(Action("Start a new scan for this patient", (s, e) => Workflow.ContinueToScan(pid)));
                bool withdrawn = Str(row, "Status") == "Withdrawn";
                var reason = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Margin = new Thickness(0, 4, 0, 0) };
                d.Children.Add(Ui.Text(withdrawn ? "Re-activate this patient" : "Withdraw this patient from the study", 12.5, true, Ui.Ink, new Thickness(0, 16, 0, 0)));
                d.Children.Add(Ui.Text(withdrawn ? "Their scans are counted again in the statistics." : "Their records are kept but left out of every statistic. A reason is required.", 11, false, Ui.Muted, new Thickness(0)));
                d.Children.Add(reason);
                d.Children.Add(Action(withdrawn ? "Mark as active" : "Mark as withdrawn", (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(reason.Text)) { LumyDialog.Show("Please enter a reason first.", "LUMYVUE Records"); return; }
                    RecordsQueries.SetParticipantStatus(pid, withdrawn ? "Active" : "Withdrawn", reason.Text.Trim());
                    reload(); RenderSummary();
                }, "GhostButton"));
            }, out reload);
            return tab;
        }

        // ------------------------------------------------------------------ scans

        private TabItem ScansTab()
        {
            Action reload = null;
            TextBox q = Search("Scan, patient or AI summary", () => reload());
            ComboBox rv = Filter(new[] { "All" }.Concat(ReviewStatus.All).Concat(new[] { "Unassigned scans" }), () => reload());
            ComboBox ai = Filter(new[] { "All", "Condition detected", "Needs review", "No condition detected", "Retake photograph", "Analysis not completed", "Pending" }, () => reload());
            var bar = FilterBar(Labelled("Search", q), Labelled("Verification", rv), Labelled("AI result", ai));
            TabItem tab = Tab("Scans", "scans", bar, () => RecordsQueries.Scans(q.Text, (string)rv.SelectedItem, (string)ai.SelectedItem), (row, d) =>
            {
                string id = Str(row, "_id");
                SessionDetail sd = StudyRepository.GetSessionDetail(id);
                if (sd == null) { d.Children.Add(Ui.Text("This scan is no longer in the database.", 13, false, Ui.Bad)); return; }
                d.Children.Add(Ui.Text(sd.Session.DisplayId + " · " + (sd.Session.ParticipantID ?? "Unassigned"), 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
                d.Children.Add(Ui.Text(Fmt.When(sd.Session.StartedAt) + " · " + (sd.Session.ScanMode == "Single" ? "1 photograph" : sd.Session.ViewsCompleted + " of " + sd.Session.ViewsExpected + " photographs"),
                                       12, false, Ui.Muted, new Thickness(0, 0, 0, 8)));
                SkinDevApp.V6.V6SessionSummary v6 = null;
                try { v6 = SkinDevApp.V6.V6SessionSummary.For(sd); } catch { }
                if (v6 != null) d.Children.Add(SkinDevApp.V6.V6ResultsView.SummaryPanel(v6));
                else d.Children.Add(Ui.Text("No AI result is stored for this scan yet. Older scans are analysed in the background.", 12.5, false, Ui.Warn));

                d.Children.Add(Action("Open results", (s, e) => Nav.Go(new ResultsPage(id)), "PrimaryButton"));
                d.Children.Add(Action("Dermatologist review", (s, e) => { new DermatologistReviewWindow(id) { Owner = Application.Current.MainWindow }.ShowDialog(); reload(); RenderSummary(); }));
                d.Children.Add(Action("Open scan folder", (s, e) => OpenFolder(sd.Session.SessionFolder), "GhostButton"));

                // verification status (normally set by the dermatologist review; a manual change needs a reason and is logged)
                d.Children.Add(Ui.Text("Verification status", 12.5, true, Ui.Ink, new Thickness(0, 16, 0, 2)));
                var status = new ComboBox { Style = (Style)Application.Current.FindResource("Combo"), ItemsSource = ReviewStatus.All, SelectedItem = ReviewStatus.Normalise(sd.Session.ReviewStatus) };
                var reason = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Margin = new Thickness(0, 6, 0, 0), ToolTip = "Reason for the change (required)" };
                d.Children.Add(status);
                d.Children.Add(Ui.Text("Reason (required, kept in the activity log)", 11, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
                d.Children.Add(reason);
                d.Children.Add(Action("Save status", (s, e) =>
                {
                    string next = (string)status.SelectedItem;
                    if (next == ReviewStatus.Normalise(sd.Session.ReviewStatus)) { LumyDialog.Show("The status is already \"" + next + "\".", "LUMYVUE Records"); return; }
                    if (string.IsNullOrWhiteSpace(reason.Text)) { LumyDialog.Show("Please enter a reason for the change.", "LUMYVUE Records"); return; }
                    StudyRepository.SetReviewStatus(id, next, "manual: " + reason.Text.Trim());
                    LumyDialog.Show("Saved. The scan is now \"" + next + "\" and its files were moved to the matching folder.", "LUMYVUE Records", MessageBoxButton.OK, MessageBoxImage.Information);
                    reload(); RenderSummary();
                }, "GhostButton"));

                if (string.IsNullOrEmpty(sd.Session.ParticipantID))
                {
                    d.Children.Add(Ui.Text("Link to a registered patient", 12.5, true, Ui.Ink, new Thickness(0, 16, 0, 2)));
                    var pid = new TextBox { Style = (Style)Application.Current.FindResource("Field"), ToolTip = "Patient ID, e.g. PS-0004" };
                    d.Children.Add(pid);
                    d.Children.Add(Action("Link scan", (s, e) =>
                    {
                        string p = StudyRepository.NormaliseParticipantId(pid.Text);
                        if (p == null || StudyRepository.GetParticipant(p) == null) { LumyDialog.Show("No registered patient has that ID.", "LUMYVUE Records"); return; }
                        StudyRepository.LinkSessionToParticipant(id, p);
                        reload(); RenderSummary();
                    }, "GhostButton"));
                }
            }, out reload);
            return tab;
        }

        // ------------------------------------------------------------------ findings, reviews, audit

        private TabItem FindingsTab()
        {
            Action reload = null;
            TextBox q = Search("Scan, patient or facial region", () => reload());
            ComboBox cond = Filter(new[] { "All", "Acne", "Hyperpigmentation", "Eczema" }, () => reload(), 180);
            ComboBox st = Filter(new[] { "All", "Detected", "Possible", "Inconclusive", "Not detected" }, () => reload(), 160);
            var bar = FilterBar(Labelled("Search", q), Labelled("Condition", cond), Labelled("AI result", st));
            return Tab("AI findings", "findings", bar, () => RecordsQueries.Findings(q.Text, (string)cond.SelectedItem, (string)st.SelectedItem), (row, d) =>
            {
                d.Children.Add(Ui.Text(Str(row, "Condition") + " · " + Str(row, "AI result"), 16, true, Ui.Ink, new Thickness(0, 0, 0, 8)));
                Rows(d, row);
                d.Children.Add(Ui.Text("A screening finding, not a diagnosis. Origin \"Re-analysis\" means the photograph was taken before the final model was installed and was analysed later.",
                                       11, false, Ui.Muted, new Thickness(0, 8, 0, 0)));
                string id = Str(row, "_id");
                d.Children.Add(Action("Open the scan's results", (s, e) => Nav.Go(new ResultsPage(id)), "PrimaryButton"));
            }, out reload);
        }

        private TabItem VerificationsTab()
        {
            Action reload = null;
            TextBox q = Search("Scan, patient, reviewer or assessment", () => reload());
            ComboBox k = Filter(new[] { "All", "Dermatologist", "Researcher" }, () => reload(), 170);
            var bar = FilterBar(Labelled("Search", q), Labelled("Reviewer type", k));
            return Tab("Verification records", "verifications", bar, () => RecordsQueries.Verifications(q.Text, (string)k.SelectedItem), (row, d) =>
            {
                d.Children.Add(Ui.Text(Str(row, "Reviewer type") + " review · " + Str(row, "Scan"), 16, true, Ui.Ink, new Thickness(0, 0, 0, 8)));
                Rows(d, row);
                d.Children.Add(Ui.Text("Reviews are append-only: a correction is saved as a new review, and the scan's status follows the latest completed dermatologist review.",
                                       11, false, Ui.Muted, new Thickness(0, 8, 0, 0)));
                string id = Str(row, "_id");
                d.Children.Add(Action("Open the scan's results", (s, e) => Nav.Go(new ResultsPage(id)), "PrimaryButton"));
                d.Children.Add(Action("Add a dermatologist review", (s, e) => { new DermatologistReviewWindow(id) { Owner = Application.Current.MainWindow }.ShowDialog(); reload(); RenderSummary(); }));
            }, out reload);
        }

        private TabItem AuditTab()
        {
            Action reload = null;
            TextBox q = Search("Action, record or detail", () => reload());
            return Tab("Activity log", "audit", FilterBar(Labelled("Search", q)), () => RecordsQueries.Audit(q.Text), (row, d) =>
            {
                d.Children.Add(Ui.Text(Str(row, "Action"), 16, true, Ui.Ink, new Thickness(0, 0, 0, 8)));
                Rows(d, row);
            }, out reload);
        }
    }
}
