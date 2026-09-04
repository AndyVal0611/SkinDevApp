using System.Windows;
using System.Windows.Controls;

namespace SkinDevApp.Views
{
    public partial class LoginForm : Page
    {
        public LoginForm()
        {
            InitializeComponent();
        }

        private void PortalModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdminCredentialsPanel == null || ClientRegisterPanel == null || EnterPortalBtn == null) return;

            if (PortalModeCombo.SelectedIndex == 0) // Client / Patient Portal
            {
                AdminCredentialsPanel.Visibility = Visibility.Collapsed;
                ClientRegisterPanel.Visibility = Visibility.Visible;
                EnterPortalBtn.Content = "REGISTER & PROCEED TO KIOSK";
            }
            else // User / Specialist Admin Portal
            {
                ClientRegisterPanel.Visibility = Visibility.Collapsed;
                AdminCredentialsPanel.Visibility = Visibility.Visible;
                EnterPortalBtn.Content = "ENTER CLINIC PORTAL";
            }
        }

        private void EnterPortalBtn_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWin = (MainWindow)Application.Current.MainWindow;

            if (PortalModeCombo.SelectedIndex == 0) // Client Registration Mode
            {
                if (string.IsNullOrWhiteSpace(ClientFullNameTxt.Text))
                {
                    MessageBox.Show("Please enter your full name to proceed with the skin analysis.", "Registration Required", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Save client registration info globally
                DatabaseHelper.CurrentClientName = ClientFullNameTxt.Text.Trim();
                DatabaseHelper.CurrentClientAge = string.IsNullOrWhiteSpace(ClientAgeTxt.Text) ? "N/A" : ClientAgeTxt.Text.Trim();
                DatabaseHelper.CurrentClientContact = string.IsNullOrWhiteSpace(ClientContactTxt.Text) ? "N/A" : ClientContactTxt.Text.Trim();

                mainWin.MainFrame.Navigate(new ClientDashboardForm());
            }
            else // Specialist Admin Login Mode
            {
                if (UsernameTxt.Text.Trim() == "lumyvue_eaf" && PasswordTxt.Password == "eaftrinity")
                {
                    mainWin.MainFrame.Navigate(new UserDashboardForm());
                }
                else
                {
                    MessageBox.Show("Invalid Specialist credentials. Please check your username or password.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}