// ConsentPage.xaml.cs - research + facial-image consent. Scanning stays blocked until the
// required items are explicitly ticked; every decision is stored with a timestamp.
using System;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class ConsentPage : Page
    {
        private readonly string _participantId;

        public ConsentPage() : this(AppSession.CurrentParticipantId) { }

        public ConsentPage(string participantId)
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Consent");
            _participantId = participantId;

            Participant p = string.IsNullOrEmpty(participantId) ? null : StudyRepository.GetParticipant(participantId);
            if (p == null)
            {
                ParticipantTxt.Text = "No patient is selected. Register or select a patient first.";
                AgreeBtn.IsEnabled = false;
                return;
            }

            ParticipantTxt.Text = "Patient " + p.ParticipantID + "  ·  " + p.FullName;

            if (p.Status == "Withdrawn")
            {
                ParticipantTxt.Text += "  ·  WITHDRAWN";
                PreviousConsentTxt.Text = "This patient has withdrawn from the study. Consent cannot be recorded and no images can be captured. " +
                                          "A researcher can change the record status in Patient records (Edit) if the withdrawal was entered by mistake.";
                AgreeBtn.IsEnabled = false;
                foreach (CheckBox box in new[] { UnderstoodChk, VoluntaryChk, CaptureChk, ProcessingChk, FutureUseChk }) box.IsEnabled = false;
                HintTxt.Text = "Consent cannot be recorded for a withdrawn patient.";
                return;
            }

            Consent prev = StudyRepository.GetLatestConsent(p.ParticipantID);
            if (prev != null)
                PreviousConsentTxt.Text = "Consent was previously recorded on " + SkinDevApp.Views.Fmt.When(prev.RecordedAt) + " (" + prev.Decision + ")" +
                    (prev.AllowsScanning ? "." : "; it does not allow scanning.") +
                    " Please confirm it again for this visit. Items marked * are required, and nothing is pre-selected.";
        }

        private bool IsWithdrawnNow()
        {
            Participant p = string.IsNullOrEmpty(_participantId) ? null : StudyRepository.GetParticipant(_participantId);
            return p != null && p.Status == "Withdrawn";
        }

        private bool RequiredDone =>
            UnderstoodChk.IsChecked == true && VoluntaryChk.IsChecked == true &&
            CaptureChk.IsChecked == true && ProcessingChk.IsChecked == true;

        private void Check_Changed(object sender, RoutedEventArgs e)
        {
            if (AgreeBtn == null) return;
            AgreeBtn.IsEnabled = !string.IsNullOrEmpty(_participantId) && RequiredDone && !IsWithdrawnNow();
            HintTxt.Text = RequiredDone ? "All required items confirmed." : "Tick all required items to continue.";
        }

        private Consent Build(string decision) => new Consent
        {
            ParticipantID = _participantId,
            ResearchInfoUnderstood = UnderstoodChk.IsChecked == true,
            VoluntaryParticipation = VoluntaryChk.IsChecked == true,
            ImageCaptureConsent = CaptureChk.IsChecked == true,
            ImageProcessingConsent = ProcessingChk.IsChecked == true,
            FutureModelUseConsent = FutureUseChk.IsChecked == true,
            Decision = decision,
            OperatorID = AppSession.ActorId
        };

        private void AgreeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequiredDone) return;
            try
            {
                StudyRepository.SaveConsent(Build("Agreed"));
                AppSession.CurrentParticipantId = _participantId;
                Nav.Go(new ClientDashboardForm());
            }
            catch (Exception ex)
            {
                LumyDialog.Show("Consent could not be saved, so scanning cannot start.\n\n" + ex.Message,
                    "LUMYVUE Consent", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeclineBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_participantId) || IsWithdrawnNow()) { Nav.Home(); return; }
            if (LumyDialog.Show("Record that the patient declined? No images will be captured.", "LUMYVUE Consent",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try { StudyRepository.SaveConsent(Build("Declined")); }
            catch (Exception ex)
            {
                LumyDialog.Show("The decision could not be saved: " + ex.Message, "LUMYVUE Consent",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            AppSession.CurrentParticipantId = null;
            Nav.Home();
        }
    }
}
