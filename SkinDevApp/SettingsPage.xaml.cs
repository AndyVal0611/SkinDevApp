// SettingsPage.xaml.cs - camera and capture preferences, accounts, and the change history.
// Layout follows a "settings" pattern: tabs on top, then rows with the name and a short explanation on the left and the control on the right.
// The image-analysis, heatmap and lesion-marking values are fixed for the study (see ScanSettings) and are not shown here.
// Saved to SystemSettings; StudyRepository.SaveSettings audits every changed value.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AForge.Video.DirectShow;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class SettingsPage : Page
    {
        private const string AutoCamera = "Automatic (prefers the Logitech BRIO)";

        // friendly names for the settings audit log
        private static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>
        {
            { "camera.name", "Camera" }, { "camera.width", "Camera width" }, { "camera.height", "Camera height" },
            { "capture.auto", "Automatic capture" }, { "capture.review_before_finish", "Review photographs before results" },
            { "capture.stable_frames", "Steady frames needed" }, { "capture.hold_ms", "Hold-still time" },
            { "capture.cooldown_s", "Pause between photographs" }, { "capture.min_sharpness", "Minimum sharpness" },
            { "capture.min_brightness", "Minimum brightness" }, { "capture.max_brightness", "Maximum brightness" },
            { "pose.front_max_yaw", "Front view: largest head turn" }, { "pose.side_min_yaw", "Side views: smallest head turn" }
        };

        private readonly Dictionary<string, TextBox> _num = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, CheckBox> _bool = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, string> _label = new Dictionary<string, string>();
        private readonly List<Tuple<string, UIElement, bool>> _tabs = new List<Tuple<string, UIElement, bool>>();   // title, content, shows Save bar
        private ComboBox _camera;
        private StackPanel _historyHost;

        public SettingsPage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Settings");
            if (!AppSession.IsResearcher)
            {
                ContentHost.Content = Ui.Card(Ui.Text("Please sign in to open the settings.", 14, false, Ui.Bad));
                FooterBar.Visibility = Visibility.Collapsed;
                return;
            }

            _tabs.Add(Tuple.Create("Camera", BuildCameraTab(), true));
            _tabs.Add(Tuple.Create("Capture", BuildCaptureTab(), true));
            _tabs.Add(Tuple.Create("Accounts", BuildAccountsTab(), false));
            _tabs.Add(Tuple.Create("Change history", BuildHistoryTab(), false));

            Fill(ScanSettings.Load());
            ShowTab(0);
        }

        // ------------------------------------------------------------------ tabs --

        private void ShowTab(int index)
        {
            TabStrip.Children.Clear();
            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                bool on = i == index;
                var label = new TextBlock
                {
                    Text = _tabs[i].Item1,
                    FontSize = 14,
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = on ? Ui.Ink : Ui.Muted,
                    Margin = new Thickness(2, 6, 2, 10)
                };
                var tab = new Border
                {
                    Child = label,
                    Margin = new Thickness(0, 0, 28, -1),
                    BorderBrush = on ? Ui.Brush("#6E7560") : System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent
                };
                tab.MouseLeftButtonUp += (s, e) => ShowTab(idx);
                TabStrip.Children.Add(tab);
            }

            ContentHost.Content = _tabs[index].Item2;
            FooterBar.Visibility = _tabs[index].Item3 ? Visibility.Visible : Visibility.Collapsed;
            if (_tabs[index].Item1 == "Change history") RefreshHistory();
        }

        // ------------------------------------------------------------------ rows --

        /// <summary>A white card holding rows separated by thin lines.</summary>
        private Border RowCard(IEnumerable<UIElement> rows)
        {
            var sp = new StackPanel();
            foreach (UIElement r in rows) sp.Children.Add(r);
            var card = Ui.Card(sp, new Thickness(8, 0, 8, 12));
            card.Padding = new Thickness(26, 6, 26, 6);
            card.VerticalAlignment = VerticalAlignment.Top;
            return card;
        }

        /// <summary>Name and explanation on the left, the control on the right.</summary>
        private UIElement Row(string title, string description, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 230 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Ui.Text(title, 13.5, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(description)) left.Children.Add(Ui.Text(description, 11.5, false, Ui.Muted, new Thickness(0)));
            g.Children.Add(left);

            var host = new ContentControl { Content = control, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(host, 1);
            g.Children.Add(host);

            return new Border
            {
                Child = g,
                Padding = new Thickness(0, 16, 0, 16),
                BorderBrush = Ui.Brush("#EFE7DA"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        private UIElement NumRow(string key, string title, string description, double width = 130)
        {
            var box = new TextBox { Style = (Style)FindResource("Field"), Height = 36, Width = width };
            _num[key] = box;
            _label[key] = title;
            return Row(title, description, box);
        }

        private UIElement BoolRow(string key, string title, string description)
        {
            var box = new CheckBox { Content = "On", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
            _bool[key] = box;
            _label[key] = title;
            return Row(title, description, box);
        }

        private UIElement Note(string text)
        {
            return new Border { Padding = new Thickness(0, 14, 0, 12), Child = Ui.Text(text, 11.5, false, Ui.Muted, new Thickness(0)) };
        }

        // ------------------------------------------------------------ camera tab --

        private UIElement BuildCameraTab()
        {
            _camera = new ComboBox { Style = (Style)FindResource("Combo"), Height = 38, Width = 340 };
            var names = new List<string> { AutoCamera };
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                for (int i = 0; i < devices.Count; i++) names.Add(devices[i].Name);
            }
            catch { }
            _camera.ItemsSource = names;
            _label["camera.name"] = "Camera";

            return RowCard(new[]
            {
                Row("Camera", "The Logitech BRIO is chosen automatically when it is connected. Change this only if the study uses another camera.", _camera),
                NumRow("camera.width", "Picture width (pixels)", "The closest supported camera mode is used. Default 640."),
                NumRow("camera.height", "Picture height (pixels)", "Default 480."),
                Note("Frame rate and exposure follow the camera driver, which keeps every photograph consistent."),
                NumRow("preview.zoom_min", "Smallest zoom of the live picture", "1 shows the whole picture. Values below 1 let it be shrunk on the screen. Zooming only changes what is shown, never the photograph that is taken. Default 1."),
                NumRow("preview.zoom_max", "Largest zoom of the live picture", "How far the picture can be enlarged by pinching with two fingers or using the + button. Default 4."),
                BoolRow("preview.zoom_buttons", "Show the zoom buttons", "Show + and − buttons on the live picture. Pinching with two fingers always works.")
            });
        }

        // ----------------------------------------------------------- capture tab --

        private UIElement BuildCaptureTab()
        {
            return RowCard(new[]
            {
                BoolRow("capture.auto", "Automatic capture", "Take each photograph automatically once the face is steady. The manual button always stays available."),
                BoolRow("capture.review_before_finish", "Review photographs before the results", "Show each photograph so it can be accepted or retaken before the results appear."),
                NumRow("capture.stable_frames", "Steady frames needed", "How many steady readings in a row are needed before a photograph is taken. Default 8."),
                NumRow("capture.hold_ms", "Hold-still time (milliseconds)", "How long the face must stay still before capture. Default 1200."),
                NumRow("capture.cooldown_s", "Pause between photographs (seconds)", "Prevents duplicate photographs. Default 4."),
                NumRow("capture.min_sharpness", "Minimum sharpness", "A photograph must be at least this sharp to be accepted. Default 25."),
                NumRow("capture.min_brightness", "Minimum brightness", "Average brightness from 0 (black) to 255 (white). Default 45."),
                NumRow("capture.max_brightness", "Maximum brightness", "Average brightness from 0 (black) to 255 (white). Default 215."),
                NumRow("pose.front_max_yaw", "Front view: largest head turn", "How far the head may turn and still count as facing the camera. Default 0.12."),
                NumRow("pose.side_min_yaw", "Side views: smallest head turn", "How far the head must turn for a left or right photograph. Default 0.20."),
                Note("Image analysis, heatmap and lesion-marking settings are fixed for this study and cannot be changed here.")
            });
        }

        // ---------------------------------------------------------- accounts tab --

        private TextBlock _acctMsg, _acctList;
        private PasswordBox _curPw, _newPw, _newPw2, _addPw, _addPw2;
        private TextBox _addUser;

        private static PasswordBox Pw() => new PasswordBox { Style = (Style)Application.Current.FindResource("FieldPassword"), Height = 36, Width = 300, Margin = new Thickness(0, 0, 0, 8) };

        private static StackPanel Labeled(params Tuple<string, UIElement>[] items)
        {
            var sp = new StackPanel();
            foreach (var it in items)
            {
                sp.Children.Add(Ui.Text(it.Item1, 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 3)));
                sp.Children.Add(it.Item2);
            }
            return sp;
        }

        private UIElement BuildAccountsTab()
        {
            _acctList = Ui.Text("", 13, false, Ui.Ink, new Thickness(0));

            _curPw = Pw(); _newPw = Pw(); _newPw2 = Pw();
            var change = Labeled(Tuple.Create("Current password", (UIElement)_curPw), Tuple.Create("New password", (UIElement)_newPw), Tuple.Create("Confirm new password", (UIElement)_newPw2));
            Button changeBtn = Ui.Btn("Change password", (s, e) => ChangeMyPassword(), "SecondaryButton", 190);
            changeBtn.HorizontalAlignment = HorizontalAlignment.Left;
            change.Children.Add(changeBtn);

            _addUser = new TextBox { Style = (Style)FindResource("Field"), Height = 36, Width = 300, Margin = new Thickness(0, 0, 0, 8) };
            _addPw = Pw(); _addPw2 = Pw();
            var add = Labeled(Tuple.Create("Username", (UIElement)_addUser),
                              Tuple.Create("Password (at least " + AuthService.MinPasswordLength + " characters)", (UIElement)_addPw),
                              Tuple.Create("Confirm password", (UIElement)_addPw2));
            Button addBtn = Ui.Btn("Add account", (s, e) => AddAccount(), "SecondaryButton", 190);
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;
            add.Children.Add(addBtn);

            _acctMsg = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 12, 0, 6));

            RefreshAccounts();
            return RowCard(new UIElement[]
            {
                Row("Accounts", "Everyone listed here can sign in and use every screen.", _acctList),
                Row("Change my password", "You are signed in as " + (string.IsNullOrEmpty(AppSession.UserId) ? "(no account)" : AppSession.UserId) + ".", change),
                Row("Add an account", "Create an account for another dermatologist or assistant. Passwords are stored only as salted hashes.", add),
                new Border { Child = _acctMsg }
            });
        }

        private void RefreshAccounts()
        {
            try { _acctList.Text = string.Join(",  ", AuthService.Usernames()); }
            catch (Exception ex) { _acctList.Text = "The accounts could not be read: " + ex.Message; }
        }

        private void AcctMessage(string text, bool ok)
        {
            _acctMsg.Foreground = ok ? Ui.Good : Ui.Bad;
            _acctMsg.Text = text;
        }

        private void ChangeMyPassword()
        {
            if (string.IsNullOrEmpty(AppSession.UserId)) { AcctMessage("Please sign in first.", false); return; }
            if (_newPw.Password != _newPw2.Password) { AcctMessage("The two new passwords do not match.", false); return; }
            try
            {
                AuthService.ChangePassword(AppSession.UserId, _curPw.Password, _newPw.Password);
                _curPw.Clear(); _newPw.Clear(); _newPw2.Clear();
                AcctMessage("Your password was changed.", true);
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        private void AddAccount()
        {
            if (_addPw.Password != _addPw2.Password) { AcctMessage("The two passwords do not match.", false); return; }
            try
            {
                AuthService.CreateAccount(_addUser.Text, _addPw.Password, AppSession.ActorId);
                AcctMessage("The account \"" + _addUser.Text.Trim() + "\" was created.", true);
                _addUser.Clear(); _addPw.Clear(); _addPw2.Clear();
                RefreshAccounts();
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        // ----------------------------------------------------------- history tab --

        private UIElement BuildHistoryTab()
        {
            _historyHost = new StackPanel();
            return RowCard(new UIElement[]
            {
                Row("Recent changes", "Every change to the camera and capture settings is recorded here with the date, time and person.", _historyHost)
            });
        }

        private static string Describe(AuditEntry a)
        {
            // details look like "old -> new | model facial_only"
            string d = a.Details ?? "";
            int bar = d.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) d = d.Substring(0, bar);
            string[] parts = d.Split(new[] { " -> " }, StringSplitOptions.None);
            string name;
            if (!Friendly.TryGetValue(a.EntityID ?? "", out name)) name = a.EntityID;
            string from = parts.Length > 1 ? parts[0] : "", to = parts.Length > 1 ? parts[1] : d;
            if (from == "(default)") from = "the default";
            return name + ": " + (parts.Length > 1 ? "changed from " + from + " to " + to : to);
        }

        private void RefreshHistory()
        {
            _historyHost.Children.Clear();
            try
            {
                List<AuditEntry> rows = StudyRepository.RecentAudit(200)
                    .Where(a => a.Entity == "SystemSettings" && Friendly.ContainsKey(a.EntityID ?? "")).Take(25).ToList();
                if (rows.Count == 0) _historyHost.Children.Add(Ui.Text("No settings have been changed yet.", 12.5, false, Ui.Muted, new Thickness(0)));
                foreach (AuditEntry a in rows)
                {
                    var line = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                    line.Children.Add(Ui.Text(Describe(a), 12.5, true, Ui.Ink, new Thickness(0)));
                    line.Children.Add(Ui.Text(Fmt.When(a.At) + "  ·  " + a.Actor, 11, false, Ui.Muted, new Thickness(0)));
                    _historyHost.Children.Add(line);
                }
            }
            catch (Exception ex) { _historyHost.Children.Add(Ui.Text("The history could not be read: " + ex.Message, 12, false, Ui.Bad, new Thickness(0))); }
        }

        // ---------------------------------------------------------------- data --

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void Fill(ScanSettings s)
        {
            _camera.SelectedItem = string.IsNullOrEmpty(s.CameraName) ? AutoCamera : s.CameraName;
            if (_camera.SelectedItem == null) _camera.SelectedIndex = 0;

            _num["camera.width"].Text = s.CameraWidth.ToString(CultureInfo.InvariantCulture);
            _num["camera.height"].Text = s.CameraHeight.ToString(CultureInfo.InvariantCulture);
            _num["preview.zoom_min"].Text = F(s.PreviewZoomMin);
            _num["preview.zoom_max"].Text = F(s.PreviewZoomMax);
            _bool["preview.zoom_buttons"].IsChecked = s.PreviewZoomButtons;
            _bool["capture.auto"].IsChecked = s.AutoCapture;
            _bool["capture.review_before_finish"].IsChecked = s.ReviewBeforeFinish;
            _num["capture.stable_frames"].Text = s.StableFramesRequired.ToString(CultureInfo.InvariantCulture);
            _num["capture.hold_ms"].Text = s.HoldStillMs.ToString(CultureInfo.InvariantCulture);
            _num["capture.cooldown_s"].Text = F(s.CooldownSeconds);
            _num["capture.min_sharpness"].Text = F(s.MinSharpness);
            _num["capture.min_brightness"].Text = F(s.MinBrightness);
            _num["capture.max_brightness"].Text = F(s.MaxBrightness);
            _num["pose.front_max_yaw"].Text = F(s.FrontMaxAbsYaw);
            _num["pose.side_min_yaw"].Text = F(s.SideMinAbsYaw);
        }

        private bool Read(out ScanSettings s, out string error)
        {
            s = new ScanSettings();          // the fixed (AI / heatmap / detector) values keep their built-in defaults
            error = null;
            var bad = new List<string>();

            int I(string k, int lo, int hi)
            {
                int v;
                if (!int.TryParse(_num[k].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(_label[k] + " must be a whole number from " + lo + " to " + hi); return lo; }
                return v;
            }
            double D(string k, double lo, double hi)
            {
                double v;
                if (!double.TryParse(_num[k].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(_label[k] + " must be a number from " + F(lo) + " to " + F(hi)); return lo; }
                return v;
            }

            string cam = _camera.SelectedItem as string;
            s.CameraName = cam == null || cam == AutoCamera ? "" : cam;
            s.CameraWidth = I("camera.width", 160, 4096);
            s.CameraHeight = I("camera.height", 120, 2160);
            s.PreviewZoomMin = D("preview.zoom_min", 0.5, 1);
            s.PreviewZoomMax = D("preview.zoom_max", 1.5, 8);
            s.PreviewZoomButtons = _bool["preview.zoom_buttons"].IsChecked == true;
            s.AutoCapture = _bool["capture.auto"].IsChecked == true;
            s.ReviewBeforeFinish = _bool["capture.review_before_finish"].IsChecked == true;
            s.StableFramesRequired = I("capture.stable_frames", 1, 60);
            s.HoldStillMs = I("capture.hold_ms", 0, 10000);
            s.CooldownSeconds = D("capture.cooldown_s", 0.5, 30);
            s.MinSharpness = D("capture.min_sharpness", 0, 1000);
            s.MinBrightness = D("capture.min_brightness", 0, 255);
            s.MaxBrightness = D("capture.max_brightness", 0, 255);
            s.FrontMaxAbsYaw = D("pose.front_max_yaw", 0.02, 0.5);
            s.SideMinAbsYaw = D("pose.side_min_yaw", 0.05, 0.8);

            if (s.MinBrightness >= s.MaxBrightness) bad.Add("The minimum brightness must be lower than the maximum brightness");
            if (s.FrontMaxAbsYaw >= s.SideMinAbsYaw) bad.Add("The front-view head turn must be smaller than the side-view head turn");

            if (bad.Count > 0) { error = string.Join(". ", bad) + "."; return false; }
            return true;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            ScanSettings s; string error;
            if (!Read(out s, out error)) { StatusTxt.Foreground = Ui.Bad; StatusTxt.Text = error; return; }
            try
            {
                int n = s.Save();
                StatusTxt.Foreground = Ui.Good;
                StatusTxt.Text = n == 0 ? "Nothing changed." : (n == 1 ? "1 setting was" : n + " settings were") + " saved at " + DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US")) + ".";
            }
            catch (Exception ex)
            {
                StatusTxt.Foreground = Ui.Bad;
                StatusTxt.Text = "The settings could not be saved: " + ex.Message;
            }
        }

        private void DefaultsBtn_Click(object sender, RoutedEventArgs e)
        {
            Fill(new ScanSettings());
            StatusTxt.Foreground = Ui.Muted;
            StatusTxt.Text = "The default values are shown. Press Save settings to apply them.";
        }
    }
}
