// HomePage.xaml.cs - Dashboard / Home: navigation and system readiness only (no live AI output).
using System;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Dashboard", showHome: false, showBack: false);
            WelcomeTxt.Text = AppSession.IsResearcher
                ? "Signed in as researcher / admin. All screens are available."
                : "Operator mode. Register participants, record consent and run scans. Sign in as a researcher for records and settings.";
            BuildNav();
            ShowCurrentParticipant();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshHealth();
        }

        private async System.Threading.Tasks.Task RefreshHealth()
        {
            HealthHost.Content = Ui.Text("Checking camera, AI model, Grad-CAM++ service and database…", 12, false, Ui.Muted);
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
            CurrentParticipantTxt.Text = "Current participant: " + id + (p != null ? "  ·  " + p.FullName : "") +
                                         ".  Skin Analysis continues with this participant.";
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

            AddTile("PARTICIPANTS / RECORDS", "Search by PatientID, profiles, scan history", r, () => Nav.Go(new PatientRecordPage()));
            AddTile("RESEARCH DASHBOARD", "Summaries from stored data only", r, () => Nav.Go(new ResearchDashboardPage()));
            AddTile("PENDING VERIFICATION", PendingText(), r, () => Nav.Go(new PatientRecordPage(pendingOnly: true)));
            AddTile("MODEL INFORMATION", "Model identity, version and hashes", r, () => Nav.Go(new ModelInfoPage()));
            AddTile("SETTINGS", "Camera, capture, AI and Grad-CAM++ (audited)", r, () => Nav.Go(new SettingsPage()));
            AddTile("ABOUT", "What this system is and is not", true, ShowAbout);
            if (r) AddTile("LEGACY REPORT LOG", "Old PatientLogs table (read-only console)", true, () => Nav.Go(new UserDashboardForm()));
        }

        private static string PendingText()
        {
            try { return StudyRepository.PendingVerificationCount() + " scan(s) awaiting researcher assessment"; }
            catch { return "Scans awaiting researcher assessment"; }
        }

        private void AddTile(string title, string subtitle, bool enabled, Action go)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(title, 14, true, Ui.Ink, new Thickness(0)));
            sp.Children.Add(Ui.Text(enabled ? subtitle : "Researcher / admin only — sign in as a researcher", 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
            var b = new Button { Style = (Style)FindResource("NavTile"), Content = sp, IsEnabled = enabled };
            b.Click += (s, e) => go();
            NavGrid.Children.Add(b);
        }

        private static void ShowAbout()
        {
            MessageBox.Show(
                "PrecisionSkin / LUMYVUE\n\n" +
                "A research prototype that captures standardised Front, Left and Right facial images, scores each with a " +
                "four-class skin-image classifier (Acne, Hyperpigmentation, Eczema, Normal) and shows class-specific " +
                "Grad-CAM++ attribution maps.\n\n" +
                "• Model Class Scores are softmax outputs, not independent disease probabilities.\n" +
                "• Grad-CAM++ maps show model attribution, not lesion segmentation.\n" +
                "• The Fitzpatrick / skin-tone AI module is not yet available.\n\n" +
                StudyText.PrototypeNotice,
                "About PrecisionSkin", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show("Participant " + participantId + " was not found.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (p.Status == "Withdrawn")
            {
                MessageBox.Show(p.ParticipantID + " has withdrawn from the study. No new scans or consent can be recorded for this participant.",
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
