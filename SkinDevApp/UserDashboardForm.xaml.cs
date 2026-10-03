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

        public void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            Nav.Home();       // legacy console is opened from the Dashboard; return there
        }
    }
}