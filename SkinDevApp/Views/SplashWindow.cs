// ============================================================================
// SplashWindow.cs  -  namespace SkinDevApp.Views
//
// The LUMYVUE start-up screen. App.OnStartup shows it, runs the real start-up work step by step and reports each step here
// ("Initializing system", "Connecting camera", "Preparing AI models", "Loading workspace"), then fades it out into the main
// window. Nothing here is simulated: the progress moves only when a step has actually finished.
// ============================================================================

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SkinDevApp.Views
{
    public sealed class SplashWindow : Window
    {
        private const double TrackWidth = 380;
        private readonly TextBlock _status;
        private readonly TextBlock _detail;
        private readonly Border _fill;
        private readonly Grid _root;

        public SplashWindow()
        {
            Title = "LUMYVUE";
            Width = 780; Height = 480;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont");

            var card = new Grid { ClipToBounds = true };
            var cardBg = new Border { CornerRadius = new CornerRadius(30), Background = (Brush)Application.Current.FindResource("SplashWash") };
            card.Children.Add(cardBg);

            // two soft, slowly drifting glows (decoration only: no text sits on a blurred layer)
            card.Children.Add(Glow("#DDBAAE", 340, new Thickness(420, -150, -60, 0), 0.32, 18));
            card.Children.Add(Glow("#C2C395", 300, new Thickness(-110, 230, 0, -100), 0.28, -14));
            card.Clip = new RectangleGeometry(new Rect(0, 0, 720, 420), 30, 30);

            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -30, 0, 0) };
            var word = new TextBlock
            {
                Text = "LUMYVUE", FontSize = 76, FontStyle = FontStyles.Italic, Foreground = Brushes.White,
                FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), HorizontalAlignment = HorizontalAlignment.Center
            };
            center.Children.Add(word);

            var ribbon = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            string[] tones = { "#F3DFD0", "#E9C6A8", "#D9A97F", "#BC8559", "#8C5A3C", "#5B3A29" };
            for (int i = 0; i < tones.Length; i++)
            {
                var sw = new Border { Width = 36, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, 0, 2, 0), Background = Ui.Brush(tones[i]), Opacity = 0 };
                sw.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(450 + i * 80) });
                ribbon.Children.Add(sw);
            }
            center.Children.Add(ribbon);

            var tagline = new TextBlock { Text = "AI skin health monitoring", FontSize = 16, Foreground = Ui.Brush("#E6E4CC"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0), Opacity = 0 };
            tagline.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(700) });
            center.Children.Add(tagline);
            card.Children.Add(center);

            // entrance: the wordmark rises softly into place
            var rise = new TranslateTransform(0, 14);
            word.RenderTransform = rise;
            word.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            word.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease });
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });

            // bottom: what is happening now, and how far along start-up is
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 34), Width = TrackWidth };
            var statusRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var spinner = Ui.Spinner(16, "#F7F5E9");
            spinner.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(spinner, Dock.Left);
            statusRow.Children.Add(spinner);
            _status = new TextBlock { Text = "Starting LUMYVUE", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#F7F5E9"), VerticalAlignment = VerticalAlignment.Center };
            statusRow.Children.Add(_status);
            bottom.Children.Add(statusRow);

            var track = new Grid { Height = 4, Width = TrackWidth };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Ui.Brush("#33FFFFFF") });
            var fillBrush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#C2C395"), (Color)ColorConverter.ConvertFromString("#EFD7CF"), 0);
            _fill = new Border { CornerRadius = new CornerRadius(2), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(_fill);
            bottom.Children.Add(track);

            _detail = new TextBlock { Text = " ", FontSize = 12, Foreground = Ui.Brush("#CFCDB4"), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            bottom.Children.Add(_detail);
            card.Children.Add(bottom);

            card.Children.Add(new TextBlock
            {
                Text = "Research prototype  ·  a screening aid, not a diagnosis", FontSize = 11, Foreground = Ui.Brush("#B9B79E"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12)
            });

            // a soft shadow on its own layer behind the card
            var shadow = new Border
            {
                CornerRadius = new CornerRadius(30), Background = Ui.Brush("#5F6048"),
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Opacity = 0.3, Color = (Color)ColorConverter.ConvertFromString("#2F3024") }
            };
            _root = new Grid { Margin = new Thickness(30), RenderTransformOrigin = new Point(0.5, 0.5) };
            _root.Children.Add(shadow);
            _root.Children.Add(card);
            Content = _root;
        }

        private static FrameworkElement Glow(string hex, double size, Thickness margin, double opacity, double drift)
        {
            var e = new Ellipse
            {
                Width = size, Height = size, Fill = Ui.Brush(hex), Opacity = opacity, Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Effect = new BlurEffect { Radius = 70 }
            };
            var t = new TranslateTransform();
            e.RenderTransform = t;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, drift, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            return e;
        }

        /// <summary>Shows the current start-up step and moves the progress bar to the given fraction (0..1).</summary>
        public void Step(string status, double progress, string detail = null)
        {
            if (_status.Text != status)
            {
                var fadeOut = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(110));
                fadeOut.Completed += (s, e) =>
                {
                    _status.Text = status;
                    _status.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
                };
                _status.BeginAnimation(OpacityProperty, fadeOut);
            }
            _detail.Text = string.IsNullOrEmpty(detail) ? " " : detail;
            double target = Math.Max(0, Math.Min(1, progress)) * TrackWidth;
            _fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Fills the bar, then fades the splash away.</summary>
        public Task FinishAsync()
        {
            var done = new TaskCompletionSource<bool>();
            Step("Ready", 1.0, "Welcome to LUMYVUE");
            var scale = new ScaleTransform(1, 1);
            _root.RenderTransform = scale;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(450) };
            fade.Completed += (s, e) => done.TrySetResult(true);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(450) });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(450) });
            _root.BeginAnimation(OpacityProperty, fade);
            return done.Task;
        }
    }
}
