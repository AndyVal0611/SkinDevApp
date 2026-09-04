using System.Windows;
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
    }
}