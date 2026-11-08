// ProfileDialog.cs - the signed-in person's own details (they are the reviewing dermatologist).
// Same personal details as the patient registration (name, birthday, sex, birthplace, civil status, citizenship, address, contact), without anything medical,
// plus the professional details. Required once per account, editable later from the dashboard.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class ProfileDialog : Window
    {
        private bool _saved, _signOut;
        private StackPanel _body;
        private readonly Dictionary<string, TextBox> _t = new Dictionary<string, TextBox>();
        private DatePicker _dob;
        private TextBox _age;
        private ComboBox _civil;
        private readonly List<RadioButton> _sex = new List<RadioButton>();

        private ProfileDialog(string title, string intro)
        {
            Title = "LUMYVUE · " + title;
            Width = 900;
            Height = 800;
            MinWidth = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            BorderBrush = Ui.Line; BorderThickness = new Thickness(1);
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("PageWash");
            var root = new DockPanel();
            _body = new StackPanel { Margin = new Thickness(26, 20, 26, 10) };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
            root.Children.Add(scroll);
            Content = root;
            _body.Children.Add(Ui.Text(title, 22, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            _body.Children.Add(Ui.Text(intro, 12.5, false, Ui.Muted, new Thickness(0, 0, 0, 12)));
        }

        // ---------------------------------------------------------------- building blocks

        private static StackPanel Cell(string label, bool required, UIElement input, string hint = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
            var l = new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = label + (required ? " *" : "") };
            cell.Children.Add(l);
            cell.Children.Add(input);
            if (!string.IsNullOrEmpty(hint)) cell.Children.Add(Ui.Text(hint, 11, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            return cell;
        }

        private TextBox Text(string key, string value)
        {
            var t = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Text = value ?? "" };
            _t[key] = t;
            return t;
        }

        private static UniformGrid Row(int cols, params UIElement[] cells)
        {
            var g = new UniformGrid { Columns = cols };
            foreach (var c in cells) g.Children.Add(c);
            return g;
        }

        private void Section(string title, string note, params UIElement[] rows)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(title, 15, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(note)) sp.Children.Add(Ui.Text(note, 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 8)));
            foreach (var r in rows) sp.Children.Add(r);
            _body.Children.Add(Ui.Card(sp, new Thickness(0, 0, 0, 12)));
        }

        private string T(string key) => _t[key].Text;

        private void UpdateAge()
        {
            DateTime? d = _dob.SelectedDate;
            if (!d.HasValue) { _age.Text = ""; return; }
            var p = new AccountProfile { DateOfBirth = d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            _age.Text = p.Age.HasValue ? p.Age.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static ProfileDialog Build(AccountProfile cur, string title, string intro)
        {
            var d = new ProfileDialog(title, intro);

            // ---- personal details (same fields as the patient registration)
            d._dob = new DatePicker { SelectedDate = cur.BirthDate };
            d._age = new TextBox { IsReadOnly = true, Style = (Style)Application.Current.FindResource("Field"), Background = Ui.Brush("#F4F0E5") };
            d._dob.SelectedDateChanged += (o, e) => d.UpdateAge();
            var sexPanel = new WrapPanel();
            foreach (string sx in new[] { "Male", "Female" })
            {
                var rb = new RadioButton { Content = sx, GroupName = "acct-sex", IsChecked = string.Equals(cur.Sex, sx, StringComparison.OrdinalIgnoreCase), Margin = new Thickness(0, 6, 16, 6), FontSize = 14 };
                d._sex.Add(rb); sexPanel.Children.Add(rb);
            }
            d._civil = new ComboBox { Style = (Style)Application.Current.FindResource("Combo") };
            foreach (string cs in new[] { "Single", "Married", "Widowed", "Separated", "Annulled" }) d._civil.Items.Add(cs);
            d._civil.SelectedItem = cur.CivilStatus;
            d.Section("Personal details", "The same details the patient registration asks for.",
                Row(4, Cell("Title", false, d.Text("title", cur.Title ?? "Dr."), "e.g. Dr., Prof."),
                       Cell("Family name", true, d.Text("last", cur.LastName)),
                       Cell("Given name", true, d.Text("first", cur.FirstName)),
                       Cell("Middle initial", false, d.Text("middle", cur.MiddleName))),
                Row(3, Cell("Birthday", true, d._dob, "MM/DD/YYYY, or pick the date from the calendar."),
                       Cell("Age", false, d._age),
                       Cell("Sex", true, sexPanel)),
                Row(3, Cell("Birthplace", true, d.Text("birthplace", cur.Birthplace)),
                       Cell("Civil status", true, d._civil),
                       Cell("Citizenship", true, d.Text("citizenship", cur.Citizenship))));
            d.UpdateAge();

            // ---- contact
            d.Section("Address and contact", null,
                Row(1, Cell("Home address", true, d.Text("address", cur.HomeAddress))),
                Row(3, Cell("Cell phone number", true, d.Text("cell", cur.Phone)),
                       Cell("Home telephone", false, d.Text("tel", cur.HomeTel)),
                       Cell("E-mail address", false, d.Text("email", cur.Email))));

            // ---- professional
            d.Section("Professional details", "Shown on your dashboard and used on your reviews.",
                Row(3, Cell("Specialty or role", true, d.Text("role", cur.JobTitle ?? "Dermatologist")),
                       Cell("Professional registration / license no.", true, d.Text("reg", cur.Registration)),
                       Cell("Clinic or hospital", false, d.Text("clinic", cur.Clinic))),
                Row(1, Cell("Availability", false, d.Text("avail", cur.Availability), "For example Mon-Fri, 9 am to 4 pm")));
            return d;
        }

        private AccountProfile Read(string user) => new AccountProfile
        {
            Username = user, Title = T("title"), LastName = T("last"), FirstName = T("first"), MiddleName = T("middle"),
            DateOfBirth = _dob.SelectedDate.HasValue ? _dob.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            Sex = _sex.FirstOrDefault(r => r.IsChecked == true)?.Content as string, Birthplace = T("birthplace"), CivilStatus = _civil.SelectedItem as string,
            Citizenship = T("citizenship"), HomeAddress = T("address"), Phone = T("cell"), HomeTel = T("tel"), Email = T("email"),
            JobTitle = T("role"), Registration = T("reg"), Clinic = T("clinic"), Availability = T("avail")
        };

        private void Buttons(string user, string saveText, string otherText, bool otherIsSignOut)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 10, 26, 16), HorizontalAlignment = HorizontalAlignment.Left };
            Button save = Ui.Btn(saveText, (o, e) =>
            {
                AccountProfile p = Read(user);
                string err = AccountProfile.Check(p);
                if (err != null) { LumyDialog.Show(err, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                try { AccountProfiles.Save(p); _saved = true; Close(); }
                catch (Exception ex) { LumyDialog.Show("Could not save your details: " + ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Error); }
            }, "PrimaryButton");
            Button other = Ui.Btn(otherText, (o, e) => { _signOut = otherIsSignOut; Close(); }, "GhostButton");
            other.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(save); row.Children.Add(other);
            DockPanel.SetDock(row, Dock.Bottom);
            ((DockPanel)Content).Children.Insert(0, row);       // pinned under the scrolling form
        }

        /// <summary>Asks for the person's details until they are complete. Returns false when they chose to sign out instead.</summary>
        public static bool RequireForCurrentUser(Window owner)
        {
            string user = AppSession.UserId;
            AccountProfile cur = AccountProfiles.Get(user);
            if (cur.IsComplete) return true;
            var d = Build(cur, "Your details",
                "You are the dermatologist who reviews the scans. Please complete your details once. They are saved with your account, shown on your dashboard and used on your reviews. Your password is not part of this form. Fields marked * are required.");
            d.Owner = owner;
            d.Buttons(user, "Save my details", "Sign out", true);
            d.Closing += (o, e) => { if (!d._saved && !d._signOut) e.Cancel = true; };      // required: only Save or Sign out closes it
            d.ShowDialog();
            return d._saved;
        }

        /// <summary>Lets the signed-in person update their details. Returns true when saved.</summary>
        public static bool EditCurrentUser(Window owner)
        {
            string user = AppSession.UserId;
            var d = Build(AccountProfiles.Get(user), "Edit your details", "Update your personal and professional details. Fields marked * are required.");
            d.Owner = owner;
            d.Buttons(user, "Save", "Cancel", false);
            d.ShowDialog();
            return d._saved;
        }

        /// <summary>For tests and previews: the dialog built for a profile (not shown).</summary>
        public static Window BuildForPreview(AccountProfile cur)
        {
            var d = Build(cur, "Your details", "Preview");
            d.Buttons(cur.Username, "Save my details", "Sign out", true);
            return d;
        }
    }
}
