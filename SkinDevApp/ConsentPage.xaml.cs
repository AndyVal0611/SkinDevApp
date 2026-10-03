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
                ParticipantTxt.Text = "No participant selected. Register or select a participant first.";
                AgreeBtn.IsEnabled = false;
                return;
            }

            ParticipantTxt.Text = "Participant " + p.ParticipantID + "  ·  " + p.FullName;

            if (p.Status == "Withdrawn")
            {
                ParticipantTxt.Text += "  ·  WITHDRAWN";
                PreviousConsentTxt.Text = "This participant has withdrawn from the study. Consent cannot be recorded and no images can be captured. " +
                                          "A researcher can change the record status in Participant Records (Edit) if the withdrawal was entered by mistake.";
                AgreeBtn.IsEnabled = false;
                foreach (CheckBox box in new[] { UnderstoodChk, VoluntaryChk, CaptureChk, ProcessingChk, FutureUseChk }) box.IsEnabled = false;
                HintTxt.Text = "Consent cannot be recorded for a withdrawn participant.";
                return;
            }

            Consent prev = StudyRepository.GetLatestConsent(p.ParticipantID);
            if (prev != null)
                PreviousConsentTxt.Text = "Previous consent record: " + prev.Decision + " on " + prev.RecordedAt +
                    (prev.AllowsScanning ? "." : " (does not allow scanning).") +
                    " Please confirm again for this visit. Required items are marked *; nothing is pre-selected.";
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
                Nav.Go(new PreparationPage());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Consent could not be saved, so scanning cannot start.\n\n" + ex.Message,
                    "LUMYVUE Consent", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeclineBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_participantId) || IsWithdrawnNow()) { Nav.Home(); return; }
            if (MessageBox.Show("Record that the participant declined? No images will be captured.", "LUMYVUE Consent",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try { StudyRepository.SaveConsent(Build("Declined")); }
            catch (Exception ex)
            {
                MessageBox.Show("The decision could not be saved: " + ex.Message, "LUMYVUE Consent",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            AppSession.CurrentParticipantId = null;
            Nav.Home();
        }
    }
}
