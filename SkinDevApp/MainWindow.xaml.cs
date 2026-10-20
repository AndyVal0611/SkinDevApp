using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkinDevApp.Data;
using SkinDevApp.Views; // Kailangan ito para mahanap ang LoginForm

namespace SkinDevApp
{
    public partial class MainWindow : Window
    {
        /// <summary>The design canvas the screens are laid out for. Bigger monitors scale the interface up (never down).</summary>
        private const double DesignWidth = 1600, DesignHeight = 900, MaxScale = 1.3;

        private bool _closeAnimated;

        public MainWindow()
        {
            InitializeComponent();

            // LUMYVUE runs full screen on its monitor (landscape or portrait); the layouts adapt to the real resolution and Windows scaling
            WindowState = WindowState.Maximized;

            // the interface grows with the screen, so text and touch targets keep a comfortable size on a large 1080p monitor
            SizeChanged += (s, e) => ApplyUiScale();

            // every page glides in: a short fade and a small upward slide
            MainFrame.Navigated += (s, e) => AnimateIn(MainFrame.Content as FrameworkElement);

            // Kusa nitong ikakarga ang LoginForm sa Startup
            MainFrame.Navigate(new LoginForm());
        }

        private void ApplyUiScale()
        {
            double w = Root.ActualWidth, h = Root.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double scale = Math.Min(w / DesignWidth, h / DesignHeight);
            scale = Math.Max(1.0, Math.Min(MaxScale, scale));
            UiScale.ScaleX = UiScale.ScaleY = Math.Round(scale, 3);
        }

        private static void AnimateIn(FrameworkElement page)
        {
            if (page == null) return;
            var slide = new TranslateTransform(0, 18);
            page.RenderTransform = slide;
            page.Opacity = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
            var rise = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease };
            rise.Completed += (s, e) =>
            {
                page.BeginAnimation(UIElement.OpacityProperty, null);
                page.Opacity = 1;
                page.RenderTransform = Transform.Identity;     // no leftover transform: text stays crisp
            };
            page.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        /// <summary>Leaves room at the bottom for the on-screen keyboard, so the field being typed into stays visible above it.</summary>
        internal void SetKeyboardInset(double height)
        {
            ScaleHost.Margin = new Thickness(0, 0, 0, Math.Max(0, height));
        }

        // ----------------------------------------------------------------- dialogs and notifications --

        /// <summary>Dims the screen behind a LUMYVUE dialog.</summary>
        internal void SetDimmed(bool on)
        {
            if (on)
            {
                DimLayer.Visibility = Visibility.Visible;
                DimLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            }
            else
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (s, e) => { if (DimLayer.Opacity < 0.01) DimLayer.Visibility = Visibility.Collapsed; };
                DimLayer.BeginAnimation(OpacityProperty, fade);
            }
        }

        /// <summary>A short notification at the bottom right that fades away by itself.</summary>
        internal void ShowToast(UIElement toast)
        {
            ToastHost.Children.Add(toast);
            var slide = new TranslateTransform(0, 16);
            toast.RenderTransform = slide;
            toast.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.6) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
                fade.Completed += (a, b) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }

        // ----------------------------------------------------------------- closing --

        private void ShowExitLayer(string text)
        {
            ExitTxt.Text = text;
            ExitLayer.Visibility = Visibility.Visible;
            ExitLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        }

        /// <summary>Do not lose a scan that is still being written to the database when the window is closed.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);                       // raises Closing: the scan page queues its last save here
            if (_closeAnimated || e.Cancel) return;

            if (SaveQueue.Pending > 0)
            {
                // show the closing screen before waiting, so the person sees that the last scan is being saved
                ShowExitLayer("Saving the last scan…");
                ExitLayer.BeginAnimation(OpacityProperty, null);
                ExitLayer.Opacity = 1;
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                Mouse.OverrideCursor = Cursors.Wait;
                bool done;
                try { done = SaveQueue.WaitAll(TimeSpan.FromSeconds(15)); }
                finally { Mouse.OverrideCursor = null; }

                if (!done && LumyDialog.Show(
                        "A scan is still being saved to the database. Closing now may leave it out until the next start " +
                        "(its images are safe on disk and are re-indexed automatically).\n\nClose anyway?",
                        "LUMYVUE", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    ExitLayer.Visibility = Visibility.Collapsed;
                    ExitLayer.Opacity = 0;
                    return;
                }
            }

            // a short, calm goodbye: the LUMYVUE screen fades in, then the window closes
            e.Cancel = true;
            _closeAnimated = true;
            ShowExitLayer("Closing LUMYVUE");
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            hold.Tick += (s, a) => { hold.Stop(); Close(); };
            hold.Start();
        }
    }
}
