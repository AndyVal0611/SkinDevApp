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

            // Kusa nitong ikakarga ang LoginForm sa Startup
            MainFrame.Navigate(new LoginForm());
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