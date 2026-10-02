// ParticipantPickerWindow.cs - "Skin Analysis" needs an existing participant: enter the PatientID.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class ParticipantPickerWindow : Window
    {
        private readonly TextBox _idBox;
        private readonly TextBlock _result;
        private readonly Button _continue;
        private Participant _found;

        public string SelectedParticipantId => _found != null ? _found.ParticipantID : null;

        public ParticipantPickerWindow()
        {
            Title = "Select participant";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Ui.Brush("#FBF9F6");

            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Ui.Text("Select an existing participant", 18, true));
            sp.Children.Add(Ui.Text("Enter the PatientID printed on the registration (for example PS-0001).", 12, false, Ui.Muted));

            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var search = Ui.Btn("Search", (s, e) => Search(), "SecondaryButton");
            search.Height = 36;
            search.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(search, Dock.Right);
            row.Children.Add(search);
            _idBox = new TextBox { Style = (Style)Application.Current.FindResource("Field") };
            _idBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Search(); };
            row.Children.Add(_idBox);
            sp.Children.Add(row);

            _result = Ui.Text("", 13, false, Ui.Ink, new Thickness(0, 12, 0, 0));
            sp.Children.Add(_result);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var register = Ui.Btn("Register new participant", (s, e) => { DialogResult = false; Nav.Go(new RegistrationPage()); }, "SecondaryButton");
            register.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(register);
            _continue = Ui.Btn("Continue", (s, e) => DialogResult = true, "PrimaryButton");
            _continue.IsEnabled = false;
            buttons.Children.Add(_continue);
            sp.Children.Add(buttons);

            Content = sp;
            Loaded += (s, e) => _idBox.Focus();
        }

        private void Search()
        {
            _found = null;
            _continue.IsEnabled = false;
            string id = StudyRepository.NormaliseParticipantId(_idBox.Text);
            if (id.Length == 0) { _result.Text = "Enter a PatientID."; return; }

            Participant p = StudyRepository.GetParticipant(id);
            if (p == null)
            {
                _result.Foreground = Ui.Bad;
                _result.Text = "Participant not found: " + id + ". Check the ID or register a new participant.";
                return;
            }

            _found = p;
            _result.Foreground = Ui.Ink;
            Consent k = StudyRepository.GetLatestConsent(p.ParticipantID);
            _result.Text = p.ParticipantID + "  ·  " + p.FullName +
                           (p.Age.HasValue ? "  ·  age " + p.Age : "") + (string.IsNullOrEmpty(p.Sex) ? "" : "  ·  " + p.Sex) +
                           "\nConsent: " + (k == null ? "not recorded — the consent screen will open next"
                                            : k.AllowsScanning ? "given " + k.RecordedAt
                                            : k.Decision + " " + k.RecordedAt + " — consent will be asked again");
            _continue.IsEnabled = true;
        }
    }
}
