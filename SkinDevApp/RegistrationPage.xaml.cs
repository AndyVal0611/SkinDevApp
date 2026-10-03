using System;
using System.Windows;
using System.Windows.Controls;

namespace SkinDevApp.Views
{
    public partial class RegistrationPage : Page
    {
        public RegistrationPage()
        {
            InitializeComponent();
            GeneratePatientId();
        }

        private void GeneratePatientId()
        {
            // Auto-generate Participant ID (Halimbawa: PS-0001)
            string uniqueId = "PS-" + new Random().Next(1000, 9999).ToString();
            ParticipantIdTxt.Text = uniqueId;
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            // Validation para masigurong napunan ang mga pangunahing impormasyon
            if (string.IsNullOrWhiteSpace(FullNameTxt.Text) || string.IsNullOrWhiteSpace(AgeTxt.Text))
            {
                MessageBox.Show("Please fill in the required participant information (Full Name and Age).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Dito mo maaaring i-save ang data sa SQLite database patungo sa Participants at SkinProfiles tables[cite: 8]
            MessageBox.Show($"Participant {ParticipantIdTxt.Text} profile saved successfully! Proceeding to Consent.", "Registration Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Halimbawa ng pag-navigate patungong ConsentPage:
            // NavigationService.Navigate(new ConsentPage());
        }
    }
}