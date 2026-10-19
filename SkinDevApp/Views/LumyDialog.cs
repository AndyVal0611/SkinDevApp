// ============================================================================
// LumyDialog.cs  -  namespace SkinDevApp
//
// LUMYVUE's own message dialog: a drop-in replacement for MessageBox.Show with the same arguments and the same results
// (OK / Cancel / Yes / No), drawn in the LUMYVUE style with large touch-friendly buttons. The screen behind is dimmed.
// If no LUMYVUE window is on screen yet (start-up problems), the standard Windows message box is used instead.
//
// Toast.Show(...) shows a short notification at the bottom right that disappears by itself.
// ============================================================================

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public static class LumyDialog
    {
        public static MessageBoxResult Show(string text) => Show(text, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.None);
        public static MessageBoxResult Show(string text, string caption) => Show(text, caption, MessageBoxButton.OK, MessageBoxImage.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button) => Show(text, caption, button, MessageBoxImage.None);

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            Application app = Application.Current;
            if (app == null) return MessageBox.Show(text, caption, button, icon);
            if (!app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Show(text, caption, button, icon));

            Window owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? app.MainWindow;
            if (owner == null || !owner.IsVisible || owner is SplashWindow) return MessageBox.Show(text, caption, button, icon);

            Window dlg;
            try { dlg = Build(text, caption, button, icon); }
            catch { return MessageBox.Show(text, caption, button, icon); }
            dlg.Owner = owner;

            var main = owner as MainWindow;
            if (main != null) main.SetDimmed(true);
            try { dlg.ShowDialog(); }
            finally { if (main != null) main.SetDimmed(false); }
            return dlg.Tag is MessageBoxResult ? (MessageBoxResult)dlg.Tag : DefaultResult(button);
        }

        /// <summary>The answer given when the dialog is closed without pressing a button (as MessageBox does).</summary>
        private static MessageBoxResult DefaultResult(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OKCancel:
                case MessageBoxButton.YesNoCancel: return MessageBoxResult.Cancel;
                case MessageBoxButton.YesNo: return MessageBoxResult.No;
                default: return MessageBoxResult.OK;
            }
        }

        private static SolidColorBrush B(string hex) => Ui.Brush(hex);

        /// <summary>Builds the dialog window (internal so the render checks can show it without waiting for a click).</summary>
        internal static Window Build(string text, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = (FontFamily)Application.Current.FindResource("BodyFont"),
                Title = string.IsNullOrWhiteSpace(caption) ? "LUMYVUE" : caption
            };

            // icon and colours by kind
            string iconKey, bg, fg;
            switch (icon)
            {
                case MessageBoxImage.Error: iconKey = "Icon.XCircle"; bg = "#F8E6E2"; fg = "#A93D33"; break;
                case MessageBoxImage.Warning: iconKey = "Icon.Alert"; bg = "#FBF1DE"; fg = "#A86A12"; break;
                case MessageBoxImage.Question: iconKey = "Icon.Question"; bg = "#E7E8CF"; fg = "#5F6048"; break;
                default: iconKey = "Icon.Info"; bg = "#E7E8CF"; fg = "#5F6048"; break;
            }

            // "LUMYVUE Consent" -> "Consent"; plain "LUMYVUE" stays
            string title = (caption ?? "").Trim();
            if (title.StartsWith("LUMYVUE ", StringComparison.OrdinalIgnoreCase)) title = title.Substring(8).Trim();
            if (title.Length == 0) title = "LUMYVUE";
            title = char.ToUpper(title[0]) + title.Substring(1);

            var body = new StackPanel { Width = 460 };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            Border badge = Ui.IconBadge(iconKey, 48, bg, fg);
            badge.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "LUMYVUE", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("#8D8D76") });
            titles.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = B("#2F3024"), TextTrimming = TextTrimming.CharacterEllipsis });
            head.Children.Add(titles);
            body.Children.Add(head);

            body.Children.Add(new TextBlock
            {
                Text = text ?? "", FontSize = 14, Foreground = B("#3A3B2C"), TextWrapping = TextWrapping.Wrap, LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 22)
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Action<string, MessageBoxResult, bool, bool, bool> add = (label, result, primary, isDefault, isCancel) =>
            {
                var b = new Button
                {
                    Content = label, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"),
                    Height = 48, MinWidth = 120, Margin = new Thickness(10, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel
                };
                b.Click += (s, e) => { win.Tag = result; win.Close(); };
                row.Children.Add(b);
            };
            switch (button)
            {
                case MessageBoxButton.OKCancel:
                    add("Cancel", MessageBoxResult.Cancel, false, false, true);
                    add("OK", MessageBoxResult.OK, true, true, false);
                    break;
                case MessageBoxButton.YesNo:
                    add("No", MessageBoxResult.No, false, false, true);
                    add("Yes", MessageBoxResult.Yes, true, true, false);
                    break;
                case MessageBoxButton.YesNoCancel:
                    add("Cancel", MessageBoxResult.Cancel, false, false, true);
                    add("No", MessageBoxResult.No, false, false, false);
                    add("Yes", MessageBoxResult.Yes, true, true, false);
                    break;
                default:
                    add("OK", MessageBoxResult.OK, true, true, true);
                    break;
            }
            body.Children.Add(row);

            // the shadow sits on its own layer behind the card, so the text on the card stays perfectly sharp
            var shadow = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(24),
                Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.22, Color = (Color)ColorConverter.ConvertFromString("#2F3024") }
            };
            var card = new Border
            {
                Background = B("#FFFEFB"), CornerRadius = new CornerRadius(24), Padding = new Thickness(28, 26, 28, 24),
                BorderBrush = B("#E8E2D3"), BorderThickness = new Thickness(1), Child = body
            };
            var root = new Grid { Margin = new Thickness(30), RenderTransformOrigin = new Point(0.5, 0.5) };
            root.Children.Add(shadow);
            root.Children.Add(card);
            win.Content = root;

            // a gentle entrance: fade in while settling from 96 % to full size
            var scale = new ScaleTransform(0.96, 0.96);
            root.RenderTransform = scale;
            win.Loaded += (s, e) =>
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            };
            win.KeyDown += (s, e) => { if (e.Key == Key.Escape && button == MessageBoxButton.YesNo) { win.Tag = MessageBoxResult.No; win.Close(); } };
            return win;
        }
    }

    public enum ToastKind { Success, Info, Warning }

    /// <summary>A short notification at the bottom right of the main window that fades away by itself.</summary>
    public static class Toast
    {
        public static void Show(string text, ToastKind kind = ToastKind.Success)
        {
            Application app = Application.Current;
            if (app == null) return;
            if (!app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(new Action(() => Show(text, kind))); return; }
            var main = app.MainWindow as MainWindow;
            if (main == null || !main.IsVisible) return;

            string iconKey = kind == ToastKind.Warning ? "Icon.Alert" : kind == ToastKind.Info ? "Icon.Info" : "Icon.CheckCircle";
            string accent = kind == ToastKind.Warning ? "#F2C46D" : kind == ToastKind.Info ? "#DDDCC2" : "#C2C395";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Ui.Icon(iconKey, 20, Ui.Brush(accent));
            ic.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(ic);
            row.Children.Add(new TextBlock { Text = text, Foreground = Ui.Brush("#F7F5E9"), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, TextWrapping = TextWrapping.Wrap });
            var toast = new Border
            {
                Background = Ui.Brush("#F02F3024"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 22, 14),
                Margin = new Thickness(0, 10, 0, 0), Child = row, HorizontalAlignment = HorizontalAlignment.Right
            };
            main.ShowToast(toast);
        }
    }
}
