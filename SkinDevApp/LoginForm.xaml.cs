using System;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public partial class LoginForm : Page
    {
        private bool _firstRun;

        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // Portrait screens: hide the brand panel so the form gets the full width.
            bool portrait = e.NewSize.Height > e.NewSize.Width || e.NewSize.Width < 760;
            BrandPanel.Visibility = portrait ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            BrandCol.MinWidth = portrait ? 0 : 340;
            BrandCol.Width = portrait ? new System.Windows.GridLength(0) : new System.Windows.GridLength(5, System.Windows.GridUnitType.Star);
        }

        public LoginForm()
        {
            InitializeComponent();
            RefreshMode();
        }

        private void PortalModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshMode();
        }

        /// <summary>Shows the fields that belong to the chosen mode (and the first-run setup when no account exists).</summary>
        private void RefreshMode()
        {
            if (AdminCredentialsPanel == null || ClientRegisterPanel == null || EnterPortalBtn == null || FirstRunPanel == null) return;

            if (PortalModeCombo.SelectedIndex == 0) // Operator / kiosk
            {
                AdminCredentialsPanel.Visibility = Visibility.Collapsed;
                ClientRegisterPanel.Visibility = Visibility.Visible;
                EnterPortalBtn.Content = "Enter operator mode";
                return;
            }

            ClientRegisterPanel.Visibility = Visibility.Collapsed;
            AdminCredentialsPanel.Visibility = Visibility.Visible;

            try { _firstRun = !AuthService.HasAccounts(); }
            catch (Exception ex)
            {
                _firstRun = false;
                LoginHintTxt.Text = "The account database could not be read: " + ex.Message;
            }

            FirstRunPanel.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
            if (_firstRun)
            {
                LoginHintTxt.Text = "No researcher account exists yet. Create the first account now (password at least " +
                                    AuthService.MinPasswordLength + " characters). Keep it safe: it protects participant records and settings.";
                EnterPortalBtn.Content = "Create account and sign in";
            }
            else
            {
                if (string.IsNullOrEmpty(LoginHintTxt.Text) || LoginHintTxt.Text.StartsWith("No researcher account")) LoginHintTxt.Text = "";
                EnterPortalBtn.Content = "Sign in";
            }
        }

        private void EnterPortalBtn_Click(object sender, RoutedEventArgs e)
        {
            if (PortalModeCombo.SelectedIndex == 0) // Operator / kiosk mode
            {
                AppSession.SignIn(UserRole.Operator, OperatorIdTxt.Text);
                Nav.Home();
                return;
            }

            string user = UsernameTxt.Text.Trim();
            try
            {
                if (_firstRun) { CreateFirstAccount(user); return; }

                switch (AuthService.SignIn(user, PasswordTxt.Password))
                {
                    case SignInResult.Ok:
                        AppSession.SignIn(UserRole.Researcher, user);
                        Nav.Home();
                        break;

                    case SignInResult.LockedOut:
                        PasswordTxt.Clear();
                        MessageBox.Show("Too many incorrect attempts for this username. Please wait " + AuthService.LockedSeconds(user) +
                                        " seconds and try again.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;

                    default:
                        PasswordTxt.Clear();
                        MessageBox.Show("Incorrect username or password.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Sign-in is not available: " + ex.Message, "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateFirstAccount(string user)
        {
            string pw = PasswordTxt.Password;
            string err = AuthService.CheckUsername(user) ?? AuthService.CheckNewPassword(user, pw);
            if (err == null && pw != ConfirmPasswordTxt.Password) err = "The two passwords do not match.";
            if (err != null)
            {
                MessageBox.Show(err, "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AuthService.CreateAccount(user, pw, null);
            PasswordTxt.Clear();
            ConfirmPasswordTxt.Clear();

            if (AuthService.SignIn(user, pw) == SignInResult.Ok)
            {
                AppSession.SignIn(UserRole.Researcher, user);
                Nav.Home();
            }
        }
    }
}
