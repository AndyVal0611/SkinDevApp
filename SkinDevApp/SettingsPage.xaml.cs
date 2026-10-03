// SettingsPage.xaml.cs - researcher/admin settings for camera, capture, AI and Grad-CAM++.
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
        private const string AutoCamera = "Automatic (prefer Logitech BRIO)";

        private readonly Dictionary<string, TextBox> _num = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, CheckBox> _bool = new Dictionary<string, CheckBox>();
        private ComboBox _camera;
        private TextBox _layer;
        private StackPanel _audit;

        public SettingsPage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Settings");
            if (!AppSession.IsResearcher)
            {
                LeftColumn.Children.Add(Ui.Card(Ui.Text("Settings are available to signed-in researchers / administrators only.", 14, false, Ui.Bad)));
                SaveBtn.IsEnabled = false;
                return;
            }
            Build();
            Fill(ScanSettings.Load());
        }

        // --------------------------------------------------------------- build --

        private StackPanel Section(StackPanel column, string title, string note)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(title, 16, true));
            if (note != null) sp.Children.Add(Ui.Text(note, 11, false, Ui.Muted));
            column.Children.Add(Ui.Card(sp));
            return sp;
        }

        private void Num(StackPanel sp, string key, string label, string hint)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var text = new StackPanel();
            text.Children.Add(Ui.Text(label, 12.5, true, Ui.Ink, new Thickness(0)));
            if (hint != null) text.Children.Add(Ui.Text(hint, 10.5, false, Ui.Muted, new Thickness(0)));
            g.Children.Add(text);
            var box = new TextBox { Style = (Style)FindResource("Field"), Height = 32 };
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            sp.Children.Add(g);
            _num[key] = box;
        }

        private void Bool(StackPanel sp, string key, string label)
        {
            var c = new CheckBox { Content = label, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
            sp.Children.Add(c);
            _bool[key] = c;
        }

        private void Build()
        {
            StackPanel cam = Section(LeftColumn, "Camera", "The Logitech BRIO is chosen automatically when present. Change only if the study uses another camera.");
            cam.Children.Add(Ui.Text("Selected camera", 12.5, true, Ui.Ink, new Thickness(0, 8, 0, 4)));
            _camera = new ComboBox { Style = (Style)FindResource("Combo") };
            var names = new List<string> { AutoCamera };
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                for (int i = 0; i < devices.Count; i++) names.Add(devices[i].Name);
            }
            catch { }
            _camera.ItemsSource = names;
            cam.Children.Add(_camera);
            Num(cam, "camera.width", "Resolution width (px)", "closest supported mode is used; default 640");
            Num(cam, "camera.height", "Resolution height (px)", "default 480");
            cam.Children.Add(Ui.Text("FPS follows the camera mode. Exposure / brightness controls are left to the camera driver so acquisition stays consistent.",
                10.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));

            StackPanel cap = Section(LeftColumn, "Capture", "Stability-based auto-capture of Front, Left and Right.");
            Bool(cap, "capture.auto", "Auto-capture ON (manual Snap stays available as a fallback)");
            Bool(cap, "capture.review_before_finish", "Show Image Review (accept / retake per view) before results");
            Num(cap, "capture.stable_frames", "Required stable frames", "consecutive stable readings before capture (default 8)");
            Num(cap, "capture.hold_ms", "Hold-still duration (ms)", "all gates green this long (default 1200)");
            Num(cap, "capture.cooldown_s", "Cooldown between captures (s)", "prevents duplicate captures (default 4)");
            Num(cap, "capture.min_sharpness", "Minimum sharpness", "Laplacian variance (default 25)");
            Num(cap, "capture.min_brightness", "Minimum brightness", "mean grey 0-255 (default 45)");
            Num(cap, "capture.max_brightness", "Maximum brightness", "mean grey 0-255 (default 215)");
            Num(cap, "pose.front_max_yaw", "Pose tolerance: Front max |yaw|", "yaw ratio (default 0.12)");
            Num(cap, "pose.side_min_yaw", "Pose tolerance: Left/Right min |yaw|", "yaw ratio (default 0.20)");

            StackPanel ai = Section(RightColumn, "AI / stability thresholds", "Changing these changes scientific behaviour. Do not alter during a data-collection phase without documenting it.");
            Num(ai, "ai.min_confidence", "Minimum smoothed confidence", "0-1 (default 0.65)");
            Num(ai, "ai.min_margin", "Minimum margin over runner-up", "0-1 (default 0.15)");
            Num(ai, "ai.min_consistency", "Minimum per-frame consistency", "share of the window, 0-1 (default 0.75)");
            Button info = Ui.Btn("Model information", (s, e) => Nav.Go(new ModelInfoPage()), "SmallButton");
            info.HorizontalAlignment = HorizontalAlignment.Left;
            info.Margin = new Thickness(0, 10, 0, 0);
            ai.Children.Add(info);

            StackPanel cam2 = Section(RightColumn, "Grad-CAM++", null);
            Bool(cam2, "gradcam.live", "Live Grad-CAM++ enabled (final per-view maps are always generated)");
            Num(cam2, "gradcam.interval_ms", "Live refresh interval (ms)", "minimum gap between live requests (default 80)");
            Num(cam2, "gradcam.max_age_s", "Max live heatmap age (s)", "older maps are hidden (default 6)");
            Num(cam2, "gradcam.overlay_opacity", "Overlay opacity", "peak opacity 0.05-1 (default 0.60)");
            Num(cam2, "gradcam.timeout_s", "Request timeout (s)", "applies after restarting the app (default 60)");
            cam2.Children.Add(Ui.Text("Target layer (research / debug only)", 12.5, true, Ui.Ink, new Thickness(0, 8, 0, 4)));
            _layer = new TextBox { Style = (Style)FindResource("Field") };
            _layer.ToolTip = "Leave empty to use the service's default layer.";
            cam2.Children.Add(_layer);

            StackPanel audit = Section(RightColumn, "Recent changes (audit log)", null);
            _audit = new StackPanel();
            audit.Children.Add(_audit);
            RefreshAudit();

            BuildAccounts();
        }

        // ------------------------------------------------------ researcher accounts --

        private TextBlock _acctMsg;
        private TextBlock _acctList;
        private PasswordBox _curPw, _newPw, _newPw2, _addPw, _addPw2;
        private TextBox _addUser;

        private static PasswordBox Pw() => new PasswordBox { Height = 32, FontSize = 13, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };

        private void BuildAccounts()
        {
            StackPanel sp = Section(RightColumn, "Researcher accounts", "Local sign-in for the researcher / admin screens. Passwords are stored only as salted hashes.");
            _acctList = Ui.Text("", 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 6));
            sp.Children.Add(_acctList);

            sp.Children.Add(Ui.Text("Change my password (" + (string.IsNullOrEmpty(AppSession.UserId) ? "not signed in" : AppSession.UserId) + ")", 12.5, true, Ui.Ink, new Thickness(0, 6, 0, 4)));
            _curPw = Pw(); _newPw = Pw(); _newPw2 = Pw();
            sp.Children.Add(Ui.Text("Current password", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_curPw);
            sp.Children.Add(Ui.Text("New password", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_newPw);
            sp.Children.Add(Ui.Text("Confirm new password", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_newPw2);
            Button change = Ui.Btn("Change password", (s, e) => ChangeMyPassword(), "SmallButton");
            change.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(change);

            sp.Children.Add(Ui.Text("Add a researcher account", 12.5, true, Ui.Ink, new Thickness(0, 14, 0, 4)));
            _addUser = new TextBox { Style = (Style)FindResource("Field"), Height = 32, Margin = new Thickness(0, 0, 0, 6) };
            _addPw = Pw(); _addPw2 = Pw();
            sp.Children.Add(Ui.Text("Username", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_addUser);
            sp.Children.Add(Ui.Text("Password (at least " + AuthService.MinPasswordLength + " characters)", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_addPw);
            sp.Children.Add(Ui.Text("Confirm password", 11, false, Ui.Muted, new Thickness(0))); sp.Children.Add(_addPw2);
            Button add = Ui.Btn("Add account", (s, e) => AddAccount(), "SmallButton");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(add);

            _acctMsg = Ui.Text("", 11.5, false, Ui.Muted, new Thickness(0, 8, 0, 0));
            sp.Children.Add(_acctMsg);
            RefreshAccounts();
        }

        private void RefreshAccounts()
        {
            try { _acctList.Text = "Accounts: " + string.Join(", ", AuthService.Usernames()); }
            catch (Exception ex) { _acctList.Text = "Could not read accounts: " + ex.Message; }
        }

        private void AcctMessage(string text, bool ok)
        {
            _acctMsg.Foreground = ok ? Ui.Good : Ui.Bad;
            _acctMsg.Text = text;
        }

        private void ChangeMyPassword()
        {
            if (string.IsNullOrEmpty(AppSession.UserId)) { AcctMessage("Sign in with a researcher account first.", false); return; }
            if (_newPw.Password != _newPw2.Password) { AcctMessage("The two new passwords do not match.", false); return; }
            try
            {
                AuthService.ChangePassword(AppSession.UserId, _curPw.Password, _newPw.Password);
                _curPw.Clear(); _newPw.Clear(); _newPw2.Clear();
                AcctMessage("Password changed.", true);
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        private void AddAccount()
        {
            if (_addPw.Password != _addPw2.Password) { AcctMessage("The two passwords do not match.", false); return; }
            try
            {
                AuthService.CreateAccount(_addUser.Text, _addPw.Password, AppSession.ActorId);
                AcctMessage("Account '" + _addUser.Text.Trim() + "' created.", true);
                _addUser.Clear(); _addPw.Clear(); _addPw2.Clear();
                RefreshAccounts();
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        private void RefreshAudit()
        {
            _audit.Children.Clear();
            try
            {
                List<AuditEntry> rows = StudyRepository.RecentAudit(40).Where(a => a.Entity == "SystemSettings").Take(15).ToList();
                if (rows.Count == 0) _audit.Children.Add(Ui.Text("No settings have been changed yet.", 11.5, false, Ui.Muted));
                foreach (AuditEntry a in rows)
                    _audit.Children.Add(Ui.Text(a.At + "  ·  " + a.Actor + "  ·  " + a.EntityID + ": " + a.Details, 11, false, Ui.Ink, new Thickness(0, 1, 0, 1)));
            }
            catch (Exception ex) { _audit.Children.Add(Ui.Text("Could not read the audit log: " + ex.Message, 11.5, false, Ui.Bad)); }
        }

        // ---------------------------------------------------------------- data --

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void Fill(ScanSettings s)
        {
            _camera.SelectedItem = string.IsNullOrEmpty(s.CameraName) ? AutoCamera : s.CameraName;
            if (_camera.SelectedItem == null) _camera.SelectedIndex = 0;

            _num["camera.width"].Text = s.CameraWidth.ToString(CultureInfo.InvariantCulture);
            _num["camera.height"].Text = s.CameraHeight.ToString(CultureInfo.InvariantCulture);
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
            _num["ai.min_confidence"].Text = F(s.MinConfidence);
            _num["ai.min_margin"].Text = F(s.MinMargin);
            _num["ai.min_consistency"].Text = F(s.MinConsistency);
            _bool["gradcam.live"].IsChecked = s.LiveGradCam;
            _num["gradcam.interval_ms"].Text = s.GradCamIntervalMs.ToString(CultureInfo.InvariantCulture);
            _num["gradcam.max_age_s"].Text = F(s.MaxHeatmapAgeSeconds);
            _num["gradcam.overlay_opacity"].Text = F(s.OverlayOpacity);
            _num["gradcam.timeout_s"].Text = s.GradCamTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            _layer.Text = s.GradCamTargetLayer;
        }

        private bool Read(out ScanSettings s, out string error)
        {
            s = new ScanSettings();
            error = null;
            var bad = new List<string>();

            int I(string k, int lo, int hi)
            {
                int v;
                if (!int.TryParse(_num[k].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(k + " (" + lo + "-" + hi + ")"); return lo; }
                return v;
            }
            double D(string k, double lo, double hi)
            {
                double v;
                if (!double.TryParse(_num[k].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(k + " (" + F(lo) + "-" + F(hi) + ")"); return lo; }
                return v;
            }

            string cam = _camera.SelectedItem as string;
            s.CameraName = cam == null || cam == AutoCamera ? "" : cam;
            s.CameraWidth = I("camera.width", 160, 4096);
            s.CameraHeight = I("camera.height", 120, 2160);
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
            s.MinConfidence = D("ai.min_confidence", 0.25, 0.99);
            s.MinMargin = D("ai.min_margin", 0, 0.9);
            s.MinConsistency = D("ai.min_consistency", 0, 1);
            s.LiveGradCam = _bool["gradcam.live"].IsChecked == true;
            s.GradCamIntervalMs = I("gradcam.interval_ms", 0, 10000);
            s.MaxHeatmapAgeSeconds = D("gradcam.max_age_s", 1, 60);
            s.OverlayOpacity = D("gradcam.overlay_opacity", 0.05, 1);
            s.GradCamTimeoutSeconds = I("gradcam.timeout_s", 5, 600);
            s.GradCamTargetLayer = (_layer.Text ?? "").Trim();

            if (s.MinBrightness >= s.MaxBrightness) bad.Add("minimum brightness must be below maximum brightness");
            if (s.FrontMaxAbsYaw >= s.SideMinAbsYaw) bad.Add("Front max yaw must be below Left/Right min yaw");

            if (bad.Count > 0) { error = "Check: " + string.Join("; ", bad); return false; }
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
                StatusTxt.Text = n == 0 ? "No changes." : n + " setting(s) saved and logged at " + DateTime.Now.ToString("HH:mm:ss") + ".";
                RefreshAudit();
            }
            catch (Exception ex)
            {
                StatusTxt.Foreground = Ui.Bad;
                StatusTxt.Text = "Could not save: " + ex.Message;
            }
        }

        private void DefaultsBtn_Click(object sender, RoutedEventArgs e)
        {
            Fill(new ScanSettings());
            StatusTxt.Foreground = Ui.Muted;
            StatusTxt.Text = "Defaults loaded into the form. Press Save settings to apply them.";
        }
    }
}