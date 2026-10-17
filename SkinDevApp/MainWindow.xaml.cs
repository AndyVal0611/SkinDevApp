using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SkinDevApp.Data;
using SkinDevApp.Views; // Kailangan ito para mahanap ang LoginForm

namespace SkinDevApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // A portrait touch monitor: use the whole screen (the layouts adapt to the real resolution and Windows scaling)
            if (SystemParameters.PrimaryScreenHeight > SystemParameters.PrimaryScreenWidth)
                WindowState = WindowState.Maximized;

            // every page glides in: a short fade and a small upward slide
            MainFrame.Navigated += (s, e) => AnimateIn(MainFrame.Content as FrameworkElement);

            // Kusa nitong ikakarga ang LoginForm sa Startup
            MainFrame.Navigate(new LoginForm());
        }

        private static void AnimateIn(FrameworkElement page)
        {
            if (page == null) return;
            var slide = new System.Windows.Media.TranslateTransform(0, 18);
            page.RenderTransform = slide;
            page.Opacity = 0;

            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
            var rise = new System.Windows.Media.Animation.DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease };
            rise.Completed += (s, e) =>
            {
                page.BeginAnimation(UIElement.OpacityProperty, null);
                page.Opacity = 1;
                page.RenderTransform = System.Windows.Media.Transform.Identity;     // no leftover transform: text stays crisp
            };
            page.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, rise);
        }

        /// <summary>Do not lose a scan that is still being written to the database when the window is closed.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);                       // raises Closing: the scan page queues its last save here
            if (e.Cancel || SaveQueue.Pending == 0) return;

            Mouse.OverrideCursor = Cursors.Wait;
            bool done;
            try { done = SaveQueue.WaitAll(TimeSpan.FromSeconds(15)); }
            finally { Mouse.OverrideCursor = null; }

            if (!done && MessageBox.Show(
                    "A scan is still being saved to the database. Closing now may leave it out until the next start " +
                    "(its images are safe on disk and are re-indexed automatically).\n\nClose anyway?",
                    "LUMYVUE", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        }
    }
}