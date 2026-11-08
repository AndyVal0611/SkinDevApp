// PasswordDialogs.cs - recovery code display, "Forgot your password?", an administrator's reset, and a new recovery code for yourself.
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

        /// <summary>Shows a recovery code once, with a plain explanation of what it is for.</summary>
        public static void ShowRecoveryCode(Window owner, string username, string code)
        {
            StackPanel body;
            Window w = Frame("Your recovery code", "Write this code down and keep it somewhere safe and private, away from this computer. If you forget your password, it is the only way to set a new one without an administrator.", owner, out body);
            body.Children.Add(new Border
            {
                Background = Ui.Brush("#F7F5EC"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 4, 0, 8),
                Child = new TextBlock { Text = code, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Foreground = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Center }
            });
            body.Children.Add(Ui.Text("Account: " + username + ". The code is shown only now and is stored only in scrambled form. Using it to reset a password replaces it with a new code. Anyone who has this code can reset the password.",
                                      11.5, false, Ui.Muted, new Thickness(0, 0, 0, 12)));
            var ok = Ui.Btn("I have written it down", (o, e) => w.Close(), "PrimaryButton");
            ok.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(ok);
            w.ShowDialog();
        }

        /// <summary>"Forgot your password?" on the sign-in screen: username + recovery code + new password.</summary>
        public static bool ForgotPassword(Window owner, string usernameHint)
        {
            StackPanel body;
            Window w = Frame("Forgot your password?", "Enter your username, your recovery code and a new password. If you do not have your recovery code, ask an administrator to reset your password in Settings > Accounts.", owner, out body);
            TextBox user = Field(body, "Username"); user.Text = usernameHint ?? "";
            TextBox code = Field(body, "Recovery code (for example ABCD-EFGH-JKLM-NPQR)");
            PasswordBox p1 = Pw(body, "New password (at least " + AuthService.MinPasswordLength + " characters)");
            PasswordBox p2 = Pw(body, "Confirm new password");
            bool done = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Ui.Btn("Set new password", (o, e) =>
            {
                if (p1.Password != p2.Password) { LumyDialog.Show("The two passwords do not match.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                try
                {
                    string next = AuthService.ResetWithRecoveryCode(user.Text, code.Text, p1.Password);
                    done = true; w.Close();
                    LumyDialog.Show("Your password was changed. A new recovery code will be shown next. The old code no longer works.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowRecoveryCode(owner, user.Text.Trim(), next);
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

        /// <summary>The signed-in person makes a new recovery code for their own account (asks for the current password first).</summary>
        public static void NewCodeForMe(Window owner)
        {
            StackPanel body;
            Window w = Frame("New recovery code", "Confirm your password to make a new recovery code. Any earlier code stops working.", owner, out body);
            PasswordBox pw = Pw(body, "Your password");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(Ui.Btn("Make a new code", (o, e) =>
            {
                try
                {
                    if (AuthService.Confirm(AppSession.UserId, pw.Password) != SignInResult.Ok) { LumyDialog.Show("That password is not correct.", "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    string code = AuthService.NewRecoveryCode(AppSession.UserId);
                    w.Close();
                    ShowRecoveryCode(owner, AppSession.UserId, code);
                }
                catch (Exception ex) { LumyDialog.Show(ex.Message, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }, "PrimaryButton"));
            var cancel = Ui.Btn("Cancel", (o, e) => w.Close(), "GhostButton"); cancel.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(cancel);
            body.Children.Add(row);
            w.ShowDialog();
        }
    }
}
