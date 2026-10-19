// HomePage.xaml.cs - Dashboard / Home: navigation and system readiness only (no live AI output).
using System;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public partial class HomePage : Page
    {
        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (NavGrid != null) NavGrid.Columns = e.NewSize.Width < 760 ? 2 : 3;
        }

        public HomePage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Dashboard", showHome: false, showBack: false);
            int hour = DateTime.Now.Hour;
            string part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            GreetingTxt.Text = part + (string.IsNullOrEmpty(AppSession.UserId) ? "" : ", " + AppSession.UserId);
            DateTxt.Text = DateTime.Now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
            WelcomeTxt.Text = "Register a patient, run a scan, or open the records.";
            BuildNav();
            ShowCurrentParticipant();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
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
            string id = AppSession.CurrentParticipantId;
            if (string.IsNullOrEmpty(id)) { CurrentParticipantBox.Visibility = Visibility.Collapsed; return; }

            Participant p = null;
            try { p = StudyRepository.GetParticipant(id); } catch { }
            CurrentParticipantTxt.Inlines.Clear();
            CurrentParticipantTxt.Inlines.Add(new System.Windows.Documents.Run("Current patient  ") { Foreground = Ui.Muted });
            CurrentParticipantTxt.Inlines.Add(new System.Windows.Documents.Run(id + (p != null ? "  ·  " + p.FullName : "")) { FontWeight = FontWeights.SemiBold });
            CurrentParticipantTxt.Inlines.Add(new System.Windows.Documents.Run("\nSkin analysis continues with this patient.") { Foreground = Ui.Muted, FontSize = 12.5 });
            CurrentParticipantBox.Visibility = Visibility.Visible;
        }

        private void ClearParticipantBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSession.CurrentParticipantId = null;
            ShowCurrentParticipant();
        }

        private void BuildNav()
        {
            NavGrid.Children.Clear();
            bool r = AppSession.IsResearcher;

            AddTile("Icon.Users", "#E7E8CF", "Patient records", "Find a patient, see their scans and results", r, () => Nav.Go(new PatientRecordPage()));
            AddTile("Icon.Chart", "#F8EBE6", "Research dashboard", "Totals and charts from the saved scans", r, () => Nav.Go(new ResearchDashboardPage()));
            AddTile("Icon.Clock", "#EFEADC", "Waiting for review", PendingText(), r, () => Nav.Go(new PatientRecordPage(pendingOnly: true)));
            AddTile("Icon.Layers", "#E7E8CF", "Model information", "Which AI model is in use", r, () => Nav.Go(new ModelInfoPage()));
            AddTile("Icon.Sliders", "#F8EBE6", "Settings", "Camera, capture and accounts", r, () => Nav.Go(new SettingsPage()));
            AddTile("Icon.Info", "#EFEADC", "About LUMYVUE", "What this system is and is not", true, ShowAbout);
            if (r) AddTile("Icon.Archive", "#E7E8CF", "Old report log", "Reports saved by earlier versions (read only)", true, () => Nav.Go(new UserDashboardForm()));
        }

        private static string PendingText()
        {
            try { int n = StudyRepository.PendingVerificationCount(); return n == 1 ? "1 scan is waiting for review" : n + " scans are waiting for review"; }
            catch { return "Scans waiting for review"; }
        }

        private void AddTile(string icon, string tint, string title, string subtitle, bool enabled, Action go)
        {
            var dock = new DockPanel();
            var chevron = Ui.Icon("Icon.ChevronRight", 18, Ui.Brush("#AFA78F"));
            DockPanel.SetDock(chevron, Dock.Right);
            dock.Children.Add(chevron);
            Border badge = Ui.IconBadge(icon, 48, tint, "#5F6048");
            badge.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(badge, Dock.Left);
            dock.Children.Add(badge);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Ui.Text(title, 15, true, Ui.Brush("#2F3024"), new Thickness(0)));
            sp.Children.Add(Ui.Text(enabled ? subtitle : "Researcher / admin only — sign in as a researcher", 12, false, Ui.Muted, new Thickness(0, 3, 0, 0)));
            dock.Children.Add(sp);
            var b = new Button { Style = (Style)FindResource("NavTile"), Content = dock, IsEnabled = enabled };
            b.Click += (s, e) => go();
            NavGrid.Children.Add(b);
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

    /// <summary>The registration -> consent -> preparation -> scan route, with the consent gate.</summary>
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
                LumyDialog.Show(p.ParticipantID + " has withdrawn from the study. No new scans or consent can be recorded for this participant.",
                    "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppSession.CurrentParticipantId = p.ParticipantID;
            Consent k = StudyRepository.GetLatestConsent(p.ParticipantID);
            if (k == null || !k.AllowsScanning)
                Nav.Go(new ConsentPage(p.ParticipantID));
            else
                Nav.Go(new PreparationPage());
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
