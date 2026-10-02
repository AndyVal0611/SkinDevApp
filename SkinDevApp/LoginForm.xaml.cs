using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

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
                EnterPortalBtn.Content = "ENTER OPERATOR MODE";
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
            if (PortalModeCombo.SelectedIndex == 0) // Operator / kiosk mode
            {
                AppSession.SignIn(UserRole.Operator, OperatorIdTxt.Text);
                Nav.Home();
            }
            else // Researcher / admin login
            {
                if (UsernameTxt.Text.Trim() == "lumyvue_eaf" && PasswordTxt.Password == "eaftrinity")
                {
                    AppSession.SignIn(UserRole.Researcher, UsernameTxt.Text.Trim());
                    StudyRepository.Audit("Sign in", "User", UsernameTxt.Text.Trim(), "researcher / admin");
                    Nav.Home();
                }
                else
                {
                    MessageBox.Show("Invalid Specialist credentials. Please check your username or password.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}