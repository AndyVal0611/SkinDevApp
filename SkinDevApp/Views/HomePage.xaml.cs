// HomePage.xaml.cs - Dashboard / Home: navigation and system readiness only (no live AI output).
using System;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public partial class HomePage : Page
    {
        private double _pageWidth = 1400;

        private void ApplyCardColumns()
        {
            if (CardsGrid != null) CardsGrid.Columns = _pageWidth < 1000 || CardsGrid.Children.Count < 2 ? 1 : 2;
        }

        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            _pageWidth = e.NewSize.Width;
            if (Sidebar != null) Sidebar.Width = _pageWidth < 1300 ? 210 : 244;
            ApplyCardColumns();
            if (StatsGrid != null) StatsGrid.Columns = e.NewSize.Width < 1000 ? 2 : 4;
        }

        public HomePage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Dashboard", showHome: false, showBack: false);
            UpdateGreeting();
            UpdateClock();
            _clock.Tick += (o, e) => UpdateClock();
            Loaded += (o, e) => { UpdateClock(); _clock.Start(); };
            Unloaded += (o, e) => _clock.Stop();
            BuildNav();
            ShowCurrentParticipant();
        }

        private readonly System.Windows.Threading.DispatcherTimer _clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>"SUNDAY, 8 NOVEMBER 2026  ·  6:41:07 PM" - the date and the live time.</summary>
        private void UpdateClock()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            DateTime n = DateTime.Now;
            DateTxt.Text = n.ToString("dddd, d MMMM yyyy", ci).ToUpperInvariant() + "   ·   " + n.ToString("h:mm:ss tt", ci);
        }

        private void UpdateGreeting()
        {
            AccountProfile me = AccountProfiles.Get(AppSession.UserId);
            string name = me.IsComplete ? ((me.Title ?? "").Trim() + " " + me.LastName.Trim()).Trim() : AppSession.UserId;      // e.g. "Dr. Bautista"
            GreetingTxt.Text = "Good Day" + (string.IsNullOrEmpty(name) ? "!" : ", " + name + "!");
            WelcomeTxt.Text = "Have a nice " + DateTime.Now.DayOfWeek + "!";
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // everyone who signs in gives their details once; they are saved with the account and shown on this dashboard
            if (!string.IsNullOrEmpty(AppSession.UserId) && !AccountProfiles.Get(AppSession.UserId).IsComplete)
            {
                if (!ProfileDialog.RequireForCurrentUser(Application.Current.MainWindow)) { Nav.SignOut(); return; }
                UpdateGreeting();
                ShowCurrentParticipant();
            }
            await RefreshHealth();
        }

        private async System.Threading.Tasks.Task RefreshHealth()
        {
            HealthHost.Content = SystemHealth.StripLoading();
            try
            {
                SystemHealth h = await SystemHealth.CheckAsync();
                HealthHost.Content = SystemHealth.Strip(h);
            }
            catch (Exception ex)
            {
                HealthHost.Content = Ui.Text("System check failed: " + ex.Message, 12, false, Ui.Bad);
            }
        }

        private async void RecheckBtn_Click(object sender, RoutedEventArgs e) => await RefreshHealth();

        private void ShowCurrentParticipant()
        {
            CardsGrid.Children.Clear();
            BuildStats();
            CardsGrid.Children.Add(WaitingCard());
            UIElement patient = PatientCard();             // only shown while a patient is selected
            if (patient != null) CardsGrid.Children.Add(patient);
            ApplyCardColumns();
            RecentHost.Content = RecentCard();
        }

        private void ClearParticipantBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSession.CurrentParticipantId = null;
            ShowCurrentParticipant();
        }

        // ------------------------------------------------------------ overview (real counts and lists from the saved records)

        private static int Safe(Func<int> f) { try { return f(); } catch { return 0; } }

        private void BuildStats()
        {
            StatsGrid.Children.Clear();
            Action<string, string, string, Action> tile = (label, value, note, go) =>
            {
                var sp = new StackPanel();
                sp.Children.Add(Ui.Text(label.ToUpperInvariant(), 11, true, Ui.Muted, new Thickness(0, 0, 0, 2)));
                var num = new TextBlock { Text = value, FontSize = 34, FontFamily = (FontFamily)FindResource("DisplayFont"), Foreground = Ui.Ink };
                sp.Children.Add(num);
                Border card = Ui.Card(sp, new Thickness(8));
                if (go != null) { card.Cursor = System.Windows.Input.Cursors.Hand; card.MouseLeftButtonUp += (o, e) => go(); }
                StatsGrid.Children.Add(card);
            };
            int pending = Safe(StudyRepository.PendingVerificationCount);
            tile("Scans today", Safe(StudyRepository.ScansToday).ToString(), "started today", null);
            tile("Waiting for review", pending.ToString(), pending == 1 ? "scan needs your review" : "scans need your review", () => Nav.Go(new PatientRecordPage(pendingOnly: true)));
            tile("Verified", Safe(StudyRepository.VerifiedScans).ToString(), "scans reviewed and completed", null);
            tile("Patients", Safe(StudyRepository.TotalParticipants).ToString(), "registered", () => Nav.Go(new PatientRecordPage()));
        }

        private static string Who(ScanSessionRow s)
        {
            Participant p = null;
            if (!string.IsNullOrEmpty(s.ParticipantID)) { try { p = StudyRepository.GetParticipant(s.ParticipantID); } catch { } }
            return p != null ? p.FullName : (string.IsNullOrEmpty(s.ParticipantID) ? "Unassigned scan" : s.ParticipantID);
        }

        private UIElement ScanRow(ScanSessionRow s, bool withReview)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string id = s.ScanSessionID;
            if (withReview)
            {
                Button rv = Ui.Btn("Review", (o, e) => { new DermatologistReviewWindow(id) { Owner = Application.Current.MainWindow }.ShowDialog(); ShowCurrentParticipant(); BuildNav(); }, "PrimaryButton");
                rv.Height = 44; rv.Margin = new Thickness(0, 0, 8, 0);
                right.Children.Add(rv);
            }
            Button open = Ui.Btn("Open", (o, e) => Nav.Go(new ResultsPage(id)), "GhostButton");
            open.Height = 44;
            right.Children.Add(open);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
            text.Children.Add(Ui.Text(Who(s), 13.5, true, Ui.Ink, new Thickness(0)));
            string res = string.IsNullOrWhiteSpace(s.AITitle) ? "Result pending" : s.AITitle;
            text.Children.Add(Ui.Text(res + "  ·  " + Fmt.When(s.StartedAt), 12, false, Ui.Muted, new Thickness(0, 1, 0, 0)));
            row.Children.Add(text);
            return new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        }

        private UIElement WaitingCard()
        {
            List<ScanSessionRow> pending = new List<ScanSessionRow>();
            try { pending = StudyRepository.PendingVerification(); } catch { }
            var body = new StackPanel();
            if (pending.Count == 0) body.Children.Add(Ui.Text("Nothing is waiting. Every scan has been reviewed.", 13, false, Ui.Ink, new Thickness(0, 4, 0, 4)));
            foreach (ScanSessionRow s in pending.Take(5)) body.Children.Add(ScanRow(s, true));
            if (pending.Count > 5)
            {
                Button all = Ui.Btn("See all " + pending.Count, (o, e) => Nav.Go(new PatientRecordPage(pendingOnly: true)), "GhostButton");
                all.Margin = new Thickness(0, 10, 0, 0); all.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(all);
            }
            return CardShell("WAITING FOR YOUR REVIEW", pending.Count == 0 ? "All caught up" : pending.Count + (pending.Count == 1 ? " scan" : " scans"),
                             "Scans waiting for review. Those marked for further review appear first.", body);
        }

        private UIElement RecentCard()
        {
            List<ScanSessionRow> recent = new List<ScanSessionRow>();
            try { recent = StudyRepository.RecentSessions(6); } catch { }
            var body = new StackPanel();
            if (recent.Count == 0) body.Children.Add(Ui.Text("No scans yet. Register a patient and start a skin analysis.", 13, false, Ui.Ink, new Thickness(0, 4, 0, 4)));
            foreach (ScanSessionRow s in recent) body.Children.Add(ScanRow(s, false));
            return CardShell("RECENT ACTIVITY", "Latest scans", null, body);
        }

        private static Border CardShell(string eyebrow, string title, string subtitle, UIElement body, Brush chipInk = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(eyebrow, 11, true, Ui.Muted, new Thickness(0, 0, 0, 4)));
            sp.Children.Add(Ui.Text(title, 20, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(subtitle)) sp.Children.Add(Ui.Text(subtitle, 12.5, false, Ui.Muted, new Thickness(0, 0, 0, 10)));
            sp.Children.Add(body);
            Border b = Ui.Card(sp, new Thickness(8));
            return b;
        }

        private UIElement MyProfileCard()
        {
            AccountProfile me = AccountProfiles.Get(AppSession.UserId);
            Func<string, string> v = x => string.IsNullOrWhiteSpace(x) ? "Not provided" : x;
            var body = new StackPanel();
            body.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Age and sex", me.Age.HasValue ? me.Age + " years" + (string.IsNullOrEmpty(me.Sex) ? "" : ", " + me.Sex) : "Not provided"),
                Ui.KV("Registration", v(me.Registration)),
                Ui.KV("Clinic", v(me.Clinic)),
                Ui.KV("Cell phone", v(me.Phone)),
                Ui.KV("E-mail", v(me.Email)),
                Ui.KV("Address", v(me.HomeAddress)),
                Ui.KV("Availability", v(me.Availability)),
                Ui.KV("Username", AppSession.UserId)
            }, 110, 12.5));
            Button edit = Ui.Btn("Edit my details", (o, e) =>
            {
                if (ProfileDialog.EditCurrentUser(Application.Current.MainWindow)) { UpdateGreeting(); ShowCurrentParticipant(); }
            }, "GhostButton");
            edit.Margin = new Thickness(0, 12, 0, 0);
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(edit);
            return CardShell("DERMATOLOGIST · YOUR PROFILE", me.IsComplete ? me.FullName : AppSession.UserId, me.IsComplete ? me.JobTitle : "Details not completed yet", body);
        }

        private UIElement PatientCard()
        {
            string id = AppSession.CurrentParticipantId;
            Participant p = null;
            if (!string.IsNullOrEmpty(id)) { try { p = StudyRepository.GetParticipant(id); } catch { } }
            var body = new StackPanel();
            if (p == null) return null;
            List<ScanSessionRow> scans = new List<ScanSessionRow>();
            try { scans = StudyRepository.SessionsForParticipant(id) ?? scans; } catch { }
            ScanSessionRow last = scans.OrderByDescending(x => x.StartedAt).FirstOrDefault();
            body.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Patient ID", p.ParticipantID),
                Ui.KV("Age and sex", (p.Age.HasValue ? p.Age + " years" : "Age not recorded") + (string.IsNullOrEmpty(p.Sex) ? "" : ", " + p.Sex)),
                Ui.KV("Contact", string.IsNullOrWhiteSpace(p.Contact) ? "Not provided" : p.Contact),
                Ui.KV("Registered", Fmt.When(p.RegisteredAt)),
                Ui.KV("Scans on record", scans.Count.ToString()),
                Ui.KV("Latest scan", last == null ? "No scan yet" : (string.IsNullOrWhiteSpace(last.AITitle) ? "Result pending" : last.AITitle) + " · " + Fmt.When(last.StartedAt))
            }, 110, 12.5));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Ui.Btn("Open record", (o, e) => Nav.Go(new PatientRecordPage(id)), "PrimaryButton"));
            Button clear = Ui.Btn("Clear", ClearParticipantBtn_Click, "GhostButton");
            clear.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(clear);
            body.Children.Add(row);
            return CardShell("PATIENT", p.FullName, "Current patient", body);
        }

        private void BuildNav()
        {
            SideNav.Children.Clear();
            bool r = AppSession.IsResearcher;
            AddSide("Icon.Home", "Dashboard", null, true);
            AddSide("Icon.Users", "Patient records", () => Nav.Go(new PatientRecordPage()), r);
            AddSide("Icon.Clock", "Waiting for review", () => Nav.Go(new PatientRecordPage(pendingOnly: true)), r, PendingCount());
            AddSide("Icon.Archive", "Records", () => { if (ReauthDialog.Confirm("Records")) Nav.Go(new RecordsDatabasePage()); }, r);
            AddSide("Icon.Layers", "Model information", () => Nav.Go(new ModelInfoPage()), r);
            AddSide("Icon.Sliders", "Settings", () => Nav.Go(new SettingsPage()), r);
            AddSide("Icon.User", "My details", () => { if (ProfileDialog.EditCurrentUser(Application.Current.MainWindow)) { UpdateGreeting(); ShowCurrentParticipant(); } }, true);
            AddSide("Icon.Info", "About LUMYVUE", ShowAbout, true);
        }

        private static int PendingCount()
        {
            try { return StudyRepository.PendingVerificationCount(); } catch { return 0; }
        }

        private void AddSide(string icon, string title, Action go, bool enabled, int badge = 0)
        {
            var row = new DockPanel();
            var ic = Ui.Icon(icon, 20, Ui.Brush("#F7F5E9"));
            ic.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(ic);
            if (badge > 0)
            {
                var chip = new Border { Background = Ui.Brush("#DDBAAE"), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                chip.Child = new TextBlock { Text = badge > 99 ? "99+" : badge.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink };
                DockPanel.SetDock(chip, Dock.Right);
                row.Children.Add(chip);
            }
            row.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = go == null ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = (Style)FindResource("SideNavBtn"), Content = row, IsEnabled = enabled };
            if (go == null) b.Background = Ui.Brush("#33FFFFFF");
            else b.Click += (s2, e2) => go();
            SideNav.Children.Add(b);
        }

        private void ShutdownBtn_Click(object sender, RoutedEventArgs e) => DeviceShutdown.Ask();

        private static string PendingText()
        {
            try { int n = StudyRepository.PendingVerificationCount(); return n == 1 ? "1 scan is waiting for review" : n + " scans are waiting for review"; }
            catch { return "Scans waiting for review"; }
        }

        private static void ShowAbout()
        {
            new AboutWindow { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        private void NewRegistrationBtn_Click(object sender, RoutedEventArgs e)
        {
            Nav.Go(new RegistrationPage());
        }

        private void SkinAnalysisBtn_Click(object sender, RoutedEventArgs e)
        {
            string id = AppSession.CurrentParticipantId;
            if (string.IsNullOrEmpty(id))
            {
                var pick = new ParticipantPickerWindow { Owner = Application.Current.MainWindow };
                if (pick.ShowDialog() != true) return;
                id = pick.SelectedParticipantId;
            }
            Workflow.ContinueToScan(id);
        }
    }

    /// <summary>The registration -> consent -> scan route, with the consent gate.</summary>
    public static class Workflow
    {
        public static void ContinueToScan(string participantId)
        {
            Participant p = StudyRepository.GetParticipant(participantId);
            if (p == null)
            {
                LumyDialog.Show("Patient " + participantId + " was not found.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (p.Status == "Withdrawn")
            {
                LumyDialog.Show(p.ParticipantID + " is marked as withdrawn. No new scans or consent can be recorded for this patient.",
                    "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppSession.CurrentParticipantId = p.ParticipantID;
            Consent k = StudyRepository.GetLatestConsent(p.ParticipantID);
            if (k == null || !k.AllowsScanning)
                Nav.Go(new ConsentPage(p.ParticipantID));
            else
                Nav.Go(new ClientDashboardForm());
        }

        /// <summary>True when the participant has a current consent that allows image acquisition.</summary>
        public static bool HasScanConsent(string participantId)
        {
            if (string.IsNullOrEmpty(participantId)) return false;
            Participant p = StudyRepository.GetParticipant(participantId);
            if (p == null || p.Status == "Withdrawn") return false;
            Consent k = StudyRepository.GetLatestConsent(participantId);
            return k != null && k.AllowsScanning;
        }
    }
}
