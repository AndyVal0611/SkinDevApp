// DatabaseExplorer.cs - "All tables": every table of the LUMYVUE database, browsable inside the app (no SQLite tool needed).
//
//   * The tables are read from the real database (sqlite_master) with their real records; nothing is typed in or mocked.
//   * Search looks in every readable column; click a column header to sort; select a row to see the whole record.
//   * Writing is deliberately narrow: audit trail, AI results, photographs, consents, receipts, models and account passwords are READ-ONLY.
//     Only the tables listed in Rules can be edited / deleted / added to, and every change asks for confirmation, asks for the signed-in
//     password, makes a backup first and is written to the activity log.
//   * The database structure is never changed.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class DatabaseExplorer
    {
        private sealed class Rule
        {
            public string Name, Category;
            public bool CanEdit, CanAdd;
            public string[] Hidden = new string[0];
        }

        private static Rule R(string name, string cat, bool edit = false, bool add = false, params string[] hidden) =>
            new Rule { Name = name, Category = cat, CanEdit = edit, CanAdd = add, Hidden = hidden };

        private const string CPatients = "Patients", CScans = "Scans and images", CAI = "AI results", CReviews = "Reviews", CSystem = "Accounts and system", CLogs = "Logs and receipts";
        private static readonly string[] CategoryOrder = { CPatients, CScans, CAI, CReviews, CSystem, CLogs };

        private static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
        {
            { "Participants", R("Patients", CPatients, edit: true) },
            { "PatientCharts", R("Patient charts", CPatients, edit: true) },
            { "MedicalIntakes", R("Medical intake", CPatients, edit: true) },
            { "SkinProfiles", R("Skin profiles", CPatients, edit: true) },
            { "Consents", R("Consents", CPatients) },
            { "ScanSessions", R("Scans", CScans) },
            { "CaptureViews", R("Images", CScans) },
            { "AnalysisImages", R("Image files", CScans) },
            { "AIAssessments", R("AI results per image", CAI) },
            { "AIFindings", R("AI findings", CAI) },
            { "AIMarkedAreas", R("AI marked areas", CAI) },
            { "AnalysisResults", R("Earlier analysis results", CAI) },
            { "ClassScores", R("Earlier class scores", CAI) },
            { "AttributionMaps", R("Earlier attribution maps", CAI) },
            { "LesionDetections", R("Earlier lesion detections", CAI) },
            { "LocalizationRuns", R("Earlier localization runs", CAI) },
            { "ModelVersions", R("AI model versions", CAI) },
            { "FitzpatrickAssessments", R("Skin type assessments", CAI) },
            { "DermatologistValidations", R("Dermatologist reviews", CReviews, edit: true) },
            { "DermatologistConditionReviews", R("Condition verdicts", CReviews, edit: true) },
            { "AssessmentLabels", R("Assessment labels", CReviews, edit: true, add: true) },
            { "ResearcherAccounts", R("Registered accounts", CSystem, false, false, "Salt", "Hash", "Iterations") },
            { "SystemSettings", R("System settings", CSystem, edit: true, add: true) },
            { "AuditLog", R("Activity log", CLogs) },
            { "Receipts", R("Receipts", CLogs) },
            { "ReceiptPrintAttempts", R("Print attempts", CLogs) }
        };

        private sealed class Col { public string Name, Type; public bool IsPk, NotNull; }

        private sealed class State
        {
            public string Table;
            public Rule Rule;
            public List<Col> Cols = new List<Col>();
            public DataTable Data;
            public bool Backed;
        }

        /// <summary>Tables kept out of the list (the data stays in the database).</summary>
        private static readonly string[] HiddenTables =
        {
            "SkinProfiles",
            // earlier-model (V5) tables: still filled by the capture pipeline, but the final model's results are in the AI tables above
            "AnalysisResults", "AnalysisImages", "AttributionMaps", "ClassScores", "LesionDetections", "LocalizationRuns", "FitzpatrickAssessments"
        };

        private const int MaxRows = 2000;

        // ------------------------------------------------------------------ public entry

        public static TabItem Tab()
        {
            var st = new State();
            var nav = new ListBox { VerticalAlignment = VerticalAlignment.Top, Width = 270, MinHeight = 420, MaxHeight = 560, BorderThickness = new Thickness(1), BorderBrush = Ui.Line, Background = Ui.Brush("#FFFEFB") };
            var search = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Width = 300, Margin = new Thickness(0, 0, 8, 0) };
            var title = Ui.Text("Choose a table on the left", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2));
            var info = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 0, 0, 8));
            var count = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 6, 0, 0));
            var grid = new DataGrid
            {
                Style = (Style)Application.Current.FindResource("Grid"), Height = 380, AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 4, 0, 0)
            };
            var details = new StackPanel();
            var addBtn = Ui.Btn("Add", (o, e) => { }, "SmallButton"); var editBtn = Ui.Btn("Edit", (o, e) => { }, "SmallButton");
            var note = Ui.Text("", 11.5, false, Ui.Muted, new Thickness(0, 6, 0, 0));
            foreach (Button b in new[] { addBtn, editBtn }) { b.Margin = new Thickness(8, 0, 0, 0); b.IsEnabled = false; }

            Action reload = null;
            Action buildNav = null;

            reload = () =>
            {
                if (st.Table == null) return;
                try
                {
                    st.Data = Load(st, search.Text);
                    grid.ItemsSource = null;
                    grid.Columns.Clear();
                    foreach (Col c in st.Cols.Where(c => !st.Rule.Hidden.Contains(c.Name, StringComparer.OrdinalIgnoreCase)))
                        grid.Columns.Add(new DataGridTextColumn { Header = c.Name, Binding = new System.Windows.Data.Binding("[" + c.Name + "]"), SortMemberPath = c.Name, MaxWidth = 340 });
                    grid.ItemsSource = st.Data.DefaultView;
                    int total = Scalar("SELECT COUNT(*) FROM \"" + st.Table + "\";");
                    count.Text = st.Data.Rows.Count == total ? total + " records" : "Showing " + st.Data.Rows.Count + " of " + total + " records" + (st.Data.Rows.Count >= MaxRows ? " (narrow the search to see the rest)" : "");
                }
                catch (Exception ex) { count.Text = "Could not read this table: " + ex.Message; }
                details.Children.Clear();
                addBtn.IsEnabled = st.Rule != null && st.Rule.CanAdd;
                editBtn.IsEnabled = false;
            };

            Action select = () =>
            {
                var item = nav.SelectedItem as ListBoxItem;
                if (item == null || !(item.Tag is string)) return;
                st.Table = (string)item.Tag;
                st.Rule = RuleFor(st.Table);
                st.Cols = Columns(st.Table);
                title.Text = st.Rule.Name;
                info.Text = "Table \"" + st.Table + "\" · " + (st.Rule.CanEdit || st.Rule.CanAdd
                    ? "You can " + string.Join(", ", new[] { st.Rule.CanAdd ? "add" : null, st.Rule.CanEdit ? "edit" : null }.Where(x => x != null)) + " records here (each change asks for confirmation)."
                    : "view only: these records are protected.");
                note.Text = st.Table == "ResearcherAccounts" ? "Passwords are never shown. Accounts are created and managed in Settings." : "";
                search.Text = "";
                reload();
            };

            buildNav = () =>
            {
                nav.Items.Clear();
                var tables = new List<string>();
                try
                {
                    using (var c = StudyDatabase.Open())
                    using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", c))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) tables.Add(r.GetString(0));
                }
                catch (Exception ex) { nav.Items.Add(new ListBoxItem { Content = "Could not read the database: " + ex.Message, IsEnabled = false }); return; }
                foreach (string cat in CategoryOrder)
                {
                    var inCat = tables.Where(t => !HiddenTables.Contains(t, StringComparer.OrdinalIgnoreCase) && RuleFor(t).Category == cat).OrderBy(t => RuleFor(t).Name).ToList();
                    if (inCat.Count == 0) continue;
                    nav.Items.Add(new ListBoxItem { Content = Ui.Text(cat.ToUpperInvariant(), 11, true, Ui.Muted, new Thickness(2, 8, 0, 2)), IsEnabled = false, Focusable = false });
                    foreach (string t in inCat)
                    {
                        int n = 0; try { n = Scalar("SELECT COUNT(*) FROM \"" + t + "\";"); } catch { }
                        var row = new DockPanel();
                        row.Children.Add(Ui.Text(n.ToString("N0"), 12, false, Ui.Muted, new Thickness(8, 0, 4, 0)));
                        DockPanel.SetDock(row.Children[0], Dock.Right);
                        row.Children.Add(Ui.Text(RuleFor(t).Name, 13, false, Ui.Ink, new Thickness(0)));
                        nav.Items.Add(new ListBoxItem { Content = row, Tag = t, Padding = new Thickness(8, 6, 8, 6) });
                    }
                }
            };

            nav.SelectionChanged += (o, e) => select();
            search.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) reload(); };
            var searchBtn = Ui.Btn("Search", (o, e) => reload(), "SmallButton");
            var clearBtn = Ui.Btn("Clear", (o, e) => { search.Text = ""; reload(); }, "SmallButton"); clearBtn.Margin = new Thickness(8, 0, 0, 0);
            var refreshBtn = Ui.Btn("Refresh", (o, e) => { buildNav(); reload(); }, "SmallButton"); refreshBtn.Margin = new Thickness(8, 0, 0, 0);
            grid.SelectionChanged += (o, e) =>
            {
                details.Children.Clear();
                var rv = grid.SelectedItem as DataRowView;
                bool has = rv != null && st.Rule != null;
                editBtn.IsEnabled = has && st.Rule.CanEdit;
                if (!has) return;
                details.Children.Add(Ui.Text("Selected record", 13, true, Ui.Ink, new Thickness(0, 8, 0, 4)));
                details.Children.Add(Ui.InfoRows(st.Cols.Where(c => !st.Rule.Hidden.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(c => new KeyValuePair<string, string>(c.Name, Show(rv.Row[c.Name]))), 190, 12));
            };

            addBtn.Click += (o, e) => { if (st.Table != null && WriteDialog(st, null)) { buildNav(); reload(); } };
            editBtn.Click += (o, e) => { var rv = grid.SelectedItem as DataRowView; if (rv != null && WriteDialog(st, rv.Row)) { buildNav(); reload(); } };

            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(addBtn); right.Children.Add(editBtn);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(search); left.Children.Add(searchBtn); left.Children.Add(clearBtn); left.Children.Add(refreshBtn);
            bar.Children.Add(left);

            var main = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            main.Children.Add(title); main.Children.Add(info); main.Children.Add(bar); main.Children.Add(grid); main.Children.Add(count); main.Children.Add(note); main.Children.Add(details);

            var layout = new DockPanel { Margin = new Thickness(4, 12, 4, 8) };
            DockPanel.SetDock(nav, Dock.Left);
            layout.Children.Add(nav);
            layout.Children.Add(main);

            var tab = new TabItem { Header = "Database tables", Tag = "tables", Content = layout };
            bool built = false;
            tab.Loaded += (o, e) => { if (!built) { built = true; buildNav(); } };
            return tab;
        }

        // ------------------------------------------------------------------ data

        private static Rule RuleFor(string table)
        {
            Rule r;
            return Rules.TryGetValue(table, out r) ? r : R(table, CSystem);      // unknown (future) tables are shown read-only
        }

        private static int Scalar(string sql)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = new SQLiteCommand(sql, c))
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        private static List<Col> Columns(string table)
        {
            var list = new List<Col>();
            using (var c = StudyDatabase.Open())
            using (var cmd = new SQLiteCommand("PRAGMA table_info(\"" + table + "\");", c))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(new Col { Name = r.GetString(1), Type = r.IsDBNull(2) ? "" : r.GetString(2), NotNull = r.GetInt32(3) == 1, IsPk = r.GetInt32(5) > 0 });
            return list;
        }

        private static DataTable Load(State st, string text)
        {
            var visible = st.Cols.Where(c => !st.Rule.Hidden.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            string sel = string.Join(", ", visible.Select(c => "\"" + c.Name + "\""));
            string where = "";
            bool hasText = !string.IsNullOrWhiteSpace(text);
            if (hasText) where = " WHERE " + string.Join(" OR ", visible.Select(c => "CAST(\"" + c.Name + "\" AS TEXT) LIKE @q"));
            string order = st.Cols.Any(c => c.IsPk) ? " ORDER BY " + string.Join(", ", st.Cols.Where(c => c.IsPk).Select(c => "\"" + c.Name + "\" DESC")) : " ORDER BY rowid DESC";
            var dt = new DataTable();
            using (var c = StudyDatabase.Open())
            using (var cmd = new SQLiteCommand("SELECT " + sel + " FROM \"" + st.Table + "\"" + where + order + " LIMIT " + MaxRows + ";", c))
            {
                if (hasText) cmd.Parameters.AddWithValue("@q", "%" + text.Trim() + "%");
                using (var r = cmd.ExecuteReader())
                {
                    // numeric columns keep a numeric type so sorting by them is numeric (10 after 9, not after 1)
                    var kinds = new Type[r.FieldCount];
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        string ty = (visible.FirstOrDefault(x => x.Name == r.GetName(i))?.Type ?? "").ToUpperInvariant();
                        kinds[i] = ty.Contains("INT") ? typeof(long) : (ty.Contains("REAL") || ty.Contains("FLOA") || ty.Contains("DOUB")) ? typeof(double) : typeof(string);
                        dt.Columns.Add(r.GetName(i), kinds[i]);
                    }
                    while (r.Read())
                    {
                        var row = dt.NewRow();
                        for (int i = 0; i < r.FieldCount; i++)
                        {
                            if (r.IsDBNull(i)) { row[i] = kinds[i] == typeof(string) ? (object)"" : DBNull.Value; continue; }
                            object v = r.GetValue(i);
                            if (v is byte[]) row[i] = kinds[i] == typeof(string) ? "[binary " + ((byte[])v).Length + " bytes]" : (object)DBNull.Value;
                            else if (kinds[i] == typeof(long)) { long l; row[i] = long.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), out l) ? (object)l : DBNull.Value; }
                            else if (kinds[i] == typeof(double)) { double d; row[i] = double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? (object)d : DBNull.Value; }
                            else row[i] = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        dt.Rows.Add(row);
                    }
                }
            }
            return dt;
        }

        private static string Show(object v)
        {
            string s = Convert.ToString(v);
            return string.IsNullOrEmpty(s) ? "—" : s;
        }

        // ------------------------------------------------------------------ writing (guarded)

        private static bool Confirm(State st, string what)
        {
            if (LumyDialog.Show(what + "\n\nThis changes the saved records. A backup copy of the database is made first.", "LUMYVUE Database", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
            if (!ReauthDialog.Confirm("Change database records")) return false;
            if (!st.Backed)
            {
                try { RecordsQueries.BackupNow(); st.Backed = true; }
                catch (Exception ex) { LumyDialog.Show("No change was made, because the backup could not be saved: " + ex.Message, "LUMYVUE Database", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
            }
            return true;
        }

        private static string KeyText(State st, DataRow row) =>
            string.Join(", ", st.Cols.Where(c => c.IsPk && row.Table.Columns.Contains(c.Name)).Select(c => c.Name + " = " + row[c.Name]));

        private static object Typed(Col c, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
            string t = (c.Type ?? "").ToUpperInvariant();
            long l; double d;
            if (t.Contains("INT") && long.TryParse(text.Trim(), out l)) return l;
            if ((t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB")) && double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return text;
        }

        /// <summary>Edit (row given) or add (row null). Returns true when the database was changed.</summary>
        private static bool WriteDialog(State st, DataRow row)
        {
            bool add = row == null;
            if (add ? !st.Rule.CanAdd : !st.Rule.CanEdit) return false;
            var fields = st.Cols.Where(c => !st.Rule.Hidden.Contains(c.Name, StringComparer.OrdinalIgnoreCase) && !(c.IsPk && (add ? c.Type.ToUpperInvariant().Contains("INT") : true))).ToList();
            var boxes = new Dictionary<string, TextBox>();
            var w = new Window
            {
                Title = (add ? "Add to " : "Edit ") + st.Rule.Name, Width = 640, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Owner = Application.Current.MainWindow,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("PageWash")
            };
            var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 10) };
            panel.Children.Add(Ui.Text((add ? "Add a record to " : "Edit a record in ") + st.Rule.Name, 18, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            panel.Children.Add(Ui.Text("Leave a box empty to store no value. Keys cannot be changed. The change is checked by the database; if it is not allowed nothing is saved.", 12, false, Ui.Muted, new Thickness(0, 0, 0, 8)));
            foreach (Col c in fields)
            {
                panel.Children.Add(new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = c.Name + (c.NotNull ? " *" : "") + (string.IsNullOrEmpty(c.Type) ? "" : "  (" + c.Type.ToLowerInvariant() + ")") });
                string cur = row != null && !(row[c.Name] is DBNull) ? Convert.ToString(row[c.Name]) : "";
                bool pkLocked = c.IsPk && !add;
                var tb = new TextBox { Style = (Style)Application.Current.FindResource(cur.Length > 80 ? "FieldArea" : "Field"), Text = cur, IsEnabled = !pkLocked };
                boxes[c.Name] = tb;
                panel.Children.Add(tb);
            }
            bool done = false;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 22, 14) };
            buttons.Children.Add(Ui.Btn(add ? "Add record" : "Save changes", (o, e) =>
            {
                var changed = fields.Where(c => !(c.IsPk && !add) && (add ? !string.IsNullOrWhiteSpace(boxes[c.Name].Text) : boxes[c.Name].Text != (row[c.Name] is DBNull ? "" : Convert.ToString(row[c.Name])))).ToList();
                if (changed.Count == 0) { LumyDialog.Show("Nothing was changed.", "LUMYVUE Database"); return; }
                string summary = string.Join("\n", changed.Select(c => "• " + c.Name + ": " + (add ? "" : Show(row[c.Name]) + " → ") + (string.IsNullOrWhiteSpace(boxes[c.Name].Text) ? "(empty)" : boxes[c.Name].Text)));
                if (!Confirm(st, (add ? "Add this record to \"" : "Save these changes in \"") + st.Rule.Name + "\"?\n" + summary)) return;
                try
                {
                    using (var c = StudyDatabase.Open())
                    using (var tx = c.BeginTransaction())
                    {
                        SQLiteCommand cmd;
                        if (add)
                            cmd = new SQLiteCommand("INSERT INTO \"" + st.Table + "\" (" + string.Join(", ", changed.Select(x => "\"" + x.Name + "\"")) + ") VALUES (" + string.Join(", ", changed.Select((x, i) => "@v" + i)) + ");", c, tx);
                        else
                        {
                            var pks = st.Cols.Where(x => x.IsPk).ToList();
                            if (pks.Count == 0) { LumyDialog.Show("This table has no key, so a single record cannot be targeted.", "LUMYVUE Database"); return; }
                            cmd = new SQLiteCommand("UPDATE \"" + st.Table + "\" SET " + string.Join(", ", changed.Select((x, i) => "\"" + x.Name + "\" = @v" + i)) + " WHERE " + string.Join(" AND ", pks.Select((p, i) => "\"" + p.Name + "\" = @k" + i)) + ";", c, tx);
                            for (int i = 0; i < pks.Count; i++) cmd.Parameters.AddWithValue("@k" + i, row[pks[i].Name]);
                        }
                        for (int i = 0; i < changed.Count; i++) cmd.Parameters.AddWithValue("@v" + i, Typed(changed[i], boxes[changed[i].Name].Text));
                        int n = cmd.ExecuteNonQuery();
                        cmd.Dispose();
                        if (n != 1) { tx.Rollback(); LumyDialog.Show("Nothing was saved.", "LUMYVUE Database"); return; }
                        StudyRepository.Audit(c, tx, add ? "Database record added" : "Database record edited", st.Table, add ? "" : KeyText(st, row), string.Join("; ", changed.Select(x => x.Name)));
                        tx.Commit();
                    }
                    done = true; w.Close();
                }
                catch (Exception ex) { LumyDialog.Show("The database did not accept this change, so nothing was saved:\n" + ex.Message, "LUMYVUE Database", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }, "PrimaryButton"));
            var cancel = Ui.Btn("Cancel", (o, e) => w.Close(), "GhostButton"); cancel.Margin = new Thickness(10, 0, 0, 0);
            buttons.Children.Add(cancel);
            var dock = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(buttons);
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel });
            w.Content = dock;
            w.ShowDialog();
            return done;
        }
    }
}
