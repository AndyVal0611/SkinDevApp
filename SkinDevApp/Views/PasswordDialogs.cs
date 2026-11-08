// PasswordDialogs.cs - "Forgot your password?" (confirm birthday and registration number, then choose a new password) and an administrator's reset.
using System;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class PasswordDialogs
    {
        private static Window Frame(string title, string intro, Window owner, out StackPanel body)
        {
            var w = new Window
            {
                Title = "LUMYVUE · " + title, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None,
                BorderBrush = Ui.Line, BorderThickness = new Thickness(1), WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("PageWash")
            };
            body = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            w.Content = body;
            body.Children.Add(Ui.Text(title, 20, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            body.Children.Add(Ui.Text(intro, 12.5, false, Ui.Muted, new Thickness(0, 0, 0, 10)));
            return w;
        }

        private static TextBox Field(StackPanel body, string label)
        {
            body.Children.Add(new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = label });
            var t = new TextBox { Style = (Style)Application.Current.FindResource("Field") };
            body.Children.Add(t);
            return t;
        }

        private static PasswordBox Pw(StackPanel body, string label)
        {
            body.Children.Add(new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = label });
            var p = new PasswordBox { Style = (Style)Application.Current.FindResource("FieldPassword"), Height = 40 };
            body.Children.Add(p);
            return p;
        }

        /// <summary>"Forgot your password?" on the sign-in screen: confirm two details from your own profile, then choose a new password.</summary>
        public static bool ForgotPassword(Window owner, string usernameHint)
        {
            StackPanel body;
            Window w = Frame("Forgot your password?", "No problem. Confirm two details from your profile, then choose a new password. If the details do not match, ask an administrator to reset your password in Settings > Accounts.", owner, out body);
            TextBox user = Field(body, "Username"); user.Text = usernameHint ?? "";
            body.Children.Add(new TextBlock { Style = (Style)Application.Current.FindResource("FieldLabel"), Text = "Your birthday (as saved in your profile)" });
            var dob = new DatePicker();
            body.Children.Add(dob);
            TextBox reg = Field(body, "Your professional registration / license number");
            PasswordBox p1 = Pw(body, "New password (at least " + AuthService.MinPasswordLength + " characters)");
            PasswordBox p2 = Pw(body, "Confirm new password");
            bool done = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Ui.Btn("Set new password", (o, e) =>
            {
                if (p1.Password != p2.Password) { LumyDialog.Show("The two passwords do not match.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                try
                {
                    AuthService.ResetWithProfile(user.Text, dob.SelectedDate, reg.Text, p1.Password);
                    done = true; w.Close();
                    LumyDialog.Show("Your password was changed. You can sign in with the new password now.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { LumyDialog.Show(ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }, "PrimaryButton"));
            var cancel = Ui.Btn("Cancel", (o, e) => w.Close(), "GhostButton"); cancel.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(cancel);
            body.Children.Add(row);
            w.ShowDialog();
            return done;
        }

        /// <summary>An administrator sets a temporary password for another account.</summary>
        public static bool AdminReset(Window owner, string username)
        {
            StackPanel body;
            Window w = Frame("Reset password", "Set a temporary password for \"" + username + "\". Give it to the person; they can change it in Settings.", owner, out body);
            PasswordBox p1 = Pw(body, "Temporary password (at least " + AuthService.MinPasswordLength + " characters)");
            PasswordBox p2 = Pw(body, "Confirm password");
            bool done = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Ui.Btn("Reset password", (o, e) =>
            {
                if (p1.Password != p2.Password) { LumyDialog.Show("The two passwords do not match.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                try { AuthService.ResetPasswordByAdmin(username, p1.Password, AppSession.ActorId); done = true; w.Close(); LumyDialog.Show("The password for \"" + username + "\" was reset.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); }
                catch (Exception ex) { LumyDialog.Show(ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }, "PrimaryButton"));
            var cancel = Ui.Btn("Cancel", (o, e) => w.Close(), "GhostButton"); cancel.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(cancel);
            body.Children.Add(row);
            w.ShowDialog();
            return done;
        }
    }
}
