using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SkinDevApp.Views
{
    public partial class UserDashboardForm : Page
    {
        public UserDashboardForm()
        {
            InitializeComponent();
            LoadConsoleData();
        }

        private void LoadConsoleData()
        {
            // Bind actual rows from SQLite Database
            List<EvaluationRecord> records = DatabaseHelper.GetAllRecords();
            RecordsDataGrid.ItemsSource = records;

            // Display exact database counts without fake baseline numbers
            int actualTotal = DatabaseHelper.GetTotalCount();
            int actualUnique = DatabaseHelper.GetUniquePatientCount();

            if (TotalEvaluationsTxt != null)
            {
                TotalEvaluationsTxt.Text = actualTotal.ToString("N0");
            }

            if (UniquePatientsTxt != null)
            {
                UniquePatientsTxt.Text = actualUnique.ToString("N0");
            }
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to clear all patient logs from the database?",
                "Clear Database",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DatabaseHelper.ClearAllRecords();
                LoadConsoleData(); // Refresh DataGrid and reset count cards to 0
                MessageBox.Show("All database records have been cleared.", "LUMYVUE Console");
            }
        }

        public void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWin = (MainWindow)Application.Current.MainWindow;
            mainWin.MainFrame.Navigate(new LoginForm());
        }
    }
}