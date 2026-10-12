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

        /// <summary>Shows the first-run setup when no account exists yet, otherwise the normal sign-in.</summary>
        private void RefreshMode()
        {
            if (EnterPortalBtn == null || FirstRunPanel == null) return;

            try { _firstRun = !AuthService.HasAccounts(); }
            catch (Exception ex)
            {
                _firstRun = false;
                LoginHintTxt.Text = "The account database could not be read: " + ex.Message;
            }

            FirstRunPanel.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
            if (_firstRun)
            {
                LoginHintTxt.Text = "No account exists yet. Create the first account now (password of at least " +
                                    AuthService.MinPasswordLength + " characters) and keep it safe: it protects patient records and settings. " +
                                    "More accounts can be added later in Settings.";
                EnterPortalBtn.Content = "Create account and sign in";
            }
            else
            {
                if (string.IsNullOrEmpty(LoginHintTxt.Text) || LoginHintTxt.Text.StartsWith("No account exists")) LoginHintTxt.Text = "";
                EnterPortalBtn.Content = "Sign in";
            }
        }

        private void EnterPortalBtn_Click(object sender, RoutedEventArgs e)
        {
            string user = UsernameTxt.Text.Trim();
            try
            {
                if (_firstRun) { CreateFirstAccount(user); return; }

                switch (AuthService.SignIn(user, PasswordTxt.Password))
                {
                    case SignInResult.Ok:
                        AppSession.SignIn(UserRole.Researcher, user);      // everyone who signs in can use every screen
                        Nav.Home();
                        break;

                    case SignInResult.LockedOut:
                        PasswordTxt.Clear();
                        MessageBox.Show("Too many incorrect attempts for this username. Please wait " + AuthService.LockedSeconds(user) +
                                        " seconds and try again.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;

                    default:
                        PasswordTxt.Clear();
                        MessageBox.Show("The username or password is incorrect.", "LUMYVUE Security", MessageBoxButton.OK, MessageBoxImage.Warning);
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
