// CreateAccountDialog.cs - an administrator creates an account for a dermatologist or another administrator.
// The new person completes their personal and professional details themselves at their first sign-in (the details form is required there).
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class CreateAccountDialog : Window
    {
        private readonly RadioButton _derm, _admin;
        private readonly TextBox _name, _user;
        private readonly PasswordBox _pw, _pw2;
        private bool _created;

        private CreateAccountDialog()
        {
            Title = "LUMYVUE · Create account";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            BorderBrush = Ui.Line; BorderThickness = new Thickness(1);
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("PageWash");
            var root = new StackPanel { Margin = new Thickness(26, 20, 26, 22) };
            Content = root;
            root.Children.Add(Ui.Text("Create an account", 20, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            root.Children.Add(Ui.Text("For a dermatologist or another administrator who will use this prototype. They enter their personal and professional details themselves the first time they sign in.",
                12.5, false, Ui.Muted, new Thickness(0, 0, 0, 10)));

            root.Children.Add(Label("Account type *"));
            var types = new WrapPanel();
            _derm = new RadioButton { Content = "Dermatologist (reviews scans)", GroupName = "acct-type", IsChecked = true, Margin = new Thickness(0, 4, 18, 4), FontSize = 14 };
            _admin = new RadioButton { Content = "Administrator (also manages accounts)", GroupName = "acct-type", Margin = new Thickness(0, 4, 0, 4), FontSize = 14 };
            types.Children.Add(_derm); types.Children.Add(_admin);
            root.Children.Add(types);

            _name = Field(); _user = Field();
            _pw = new PasswordBox { Style = (Style)Application.Current.FindResource("FieldPassword"), Height = 40 };
            _pw2 = new PasswordBox { Style = (Style)Application.Current.FindResource("FieldPassword"), Height = 40 };
            root.Children.Add(Label("Full name (with title, for example Dr. Maria Santos) *")); root.Children.Add(_name);
            root.Children.Add(Label("Username (letters, digits, . _ -) *")); root.Children.Add(_user);
            root.Children.Add(Label("Temporary password (at least " + AuthService.MinPasswordLength + " characters) *")); root.Children.Add(_pw);
            root.Children.Add(Label("Confirm password *")); root.Children.Add(_pw2);
            root.Children.Add(Ui.Text("Give the person this username and password. They can change the password in Settings. Passwords are stored only as salted hashes.",
                11.5, false, Ui.Muted, new Thickness(0, 8, 0, 0)));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            row.Children.Add(Ui.Btn("Create account", (o, e) => Save(), "PrimaryButton"));
            var cancel = Ui.Btn("Cancel", (o, e) => Close(), "GhostButton"); cancel.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(cancel);
            root.Children.Add(row);
        }

        private static TextBlock Label(string t) => new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = t };
        private static TextBox Field() => new TextBox { Style = (Style)Application.Current.FindResource("Field") };

        private void Save()
        {
            string name = _name.Text.Trim(), user = _user.Text.Trim();
            string err = name.Length < 3 ? "Please enter the person's full name." : AuthService.CheckUsername(user) ?? AuthService.CheckNewPassword(user, _pw.Password);
            if (err == null && _pw.Password != _pw2.Password) err = "The two passwords do not match.";
            if (err != null) { LumyDialog.Show(err, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                string type = _admin.IsChecked == true ? AuthService.TypeAdministrator : AuthService.TypeDermatologist;
                AuthService.CreateAccount(user, _pw.Password, AppSession.ActorId, type, name);
                _created = true;
                LumyDialog.Show("The account \"" + user + "\" (" + type + ") was created.\n\nGive them the username and the temporary password. They will be asked for their details when they first sign in.",
                    "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex) { LumyDialog.Show(ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        public static Window BuildForPreview() => new CreateAccountDialog();

        /// <summary>Shows the dialog. True when an account was created.</summary>
        public static bool Run(Window owner)
        {
            var d = new CreateAccountDialog { Owner = owner };
            d.ShowDialog();
            return d._created;
        }
    }
}
