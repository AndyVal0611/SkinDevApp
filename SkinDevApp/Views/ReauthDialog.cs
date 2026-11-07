// ============================================================================
// ReauthDialog.cs  -  namespace SkinDevApp.Views
//
// "Confirm it's you": before the Records & database screen opens, the signed-in user types their own password again.
// It uses the same account check, lock-out (5 wrong attempts lock the account for 60 seconds) and audit log as the sign-in screen,
// but never changes who is signed in. A user who is not signed in with an account cannot pass.
// ============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class ReauthDialog
    {
        /// <summary>Shows the dialog; true only when the signed-in user's password was entered correctly.</summary>
        public static bool Confirm(string purpose)
        {
            if (string.IsNullOrWhiteSpace(AppSession.UserId)) { LumyDialog.Show("Please sign in first.", "LUMYVUE"); return false; }
            Application app = Application.Current;
            Window owner = app.MainWindow;
            string user = AppSession.UserId;

            var win = new Window { WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Title = "LUMYVUE" };
            var body = new StackPanel();
            body.Children.Add(Ui.Text("Confirm it's you", 18, true, null, new Thickness(0, 0, 0, 4)));
            body.Children.Add(Ui.Text("Signed in as " + user + ". Enter your password again to open " + purpose + ".", 13, false, Ui.Muted, new Thickness(0, 0, 0, 14)));
            var pw = new PasswordBox { Style = (Style)app.FindResource("FieldPassword") };
            body.Children.Add(Ui.Text("Password", 12, true, Ui.Muted, new Thickness(0, 0, 0, 4)));
            body.Children.Add(pw);
            var error = Ui.Text("", 12, false, Ui.Bad, new Thickness(0, 8, 0, 0));
            body.Children.Add(error);

            bool ok = false;
            void Try()
            {
                SignInResult r = AuthService.Confirm(user, pw.Password);
                if (r == SignInResult.Ok) { ok = true; win.Close(); return; }
                pw.Clear();
                error.Text = r == SignInResult.LockedOut
                    ? "Too many incorrect attempts. Please wait " + AuthService.LockedSeconds(user) + " seconds."
                    : "That password is not correct.";
                pw.Focus();
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Button cancel = Ui.Btn("Cancel", (s, e) => win.Close(), "SecondaryButton", 120);
            cancel.Margin = new Thickness(0, 0, 10, 0);
            Button confirm = Ui.Btn("Confirm", (s, e) => Try(), "PrimaryButton", 120);
            confirm.IsDefault = true;
            buttons.Children.Add(cancel); buttons.Children.Add(confirm);
            body.Children.Add(buttons);

            LumyDialog.Frame(win, body, 440);
            win.Loaded += (s, e) => pw.Focus();
            win.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
            win.ShowDialog();
            return ok;
        }
    }
}
