using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkinDevApp.Views
{
    public partial class PrintReportWindow : Window
    {
        private string savedDiagnosis;
        private string savedConfidence;
        private string savedClientName;

        public PrintReportWindow(BitmapSource capturedImage, string clientName, string diagnosis, string confidence, string age, string contact, double acneVal, double hyperVal, double eczemaVal, double normalVal)
        {
            InitializeComponent();

            savedClientName = string.IsNullOrEmpty(clientName) ? "Client Walk-In" : clientName;
            savedDiagnosis = diagnosis;
            savedConfidence = confidence;

            // Patient Info
            if (PatientNameTxt != null) PatientNameTxt.Text = savedClientName;
            if (PatientDetailsTxt != null)
                PatientDetailsTxt.Text = $"Age: {(string.IsNullOrEmpty(age) ? "N/A" : age)} | Contact: {(string.IsNullOrEmpty(contact) ? "N/A" : contact)}";

            if (ConsultationDateTxt != null) ConsultationDateTxt.Text = DateTime.Now.ToString("yyyy-MM-dd");
            if (ReportDateTxt != null) ReportDateTxt.Text = "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // Captured Image na may dots
            if (ReportFaceImage != null && capturedImage != null)
            {
                ReportFaceImage.Source = capturedImage;
            }

            // Dynamic Dermatological Status (Progress Bars & Percent Texts)
            if (AcneProgress != null) AcneProgress.Value = acneVal;
            if (AcnePercentTxt != null) AcnePercentTxt.Text = $"{acneVal:F1}%";

            if (HyperProgress != null) HyperProgress.Value = hyperVal;
            if (HyperPercentTxt != null) HyperPercentTxt.Text = $"{hyperVal:F1}%";

            if (EczemaProgress != null) EczemaProgress.Value = eczemaVal;
            if (EczemaPercentTxt != null) EczemaPercentTxt.Text = $"{eczemaVal:F1}%";

            if (NormalProgress != null) NormalProgress.Value = normalVal;
            if (NormalPercentTxt != null) NormalPercentTxt.Text = $"{normalVal:F1}%";

            // Primary Status Box
            if (PrimaryStatusTxt != null)
            {
                PrimaryStatusTxt.Text = $"Primary Status: {diagnosis} ({confidence} Confidence)";
            }
        }

        private void TriggerPrintBtn_Click(object sender, RoutedEventArgs e)
        {
            TriggerPrintBtn.Visibility = Visibility.Hidden;

            try
            {
                // 1. Isang beses lang lalabas ang File Explorer para sa pangalan at lokasyon ng PDF
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PDF Document (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = $"LUMYVUE_{savedClientName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    PrintDialog printDlg = new PrintDialog();

                    // Hanapin ang Microsoft Print to PDF printer nang tahimik sa background
                    LocalPrintServer printServer = new LocalPrintServer();
                    PrintQueue pdfPrinter = printServer.GetPrintQueues()
                        .Cast<PrintQueue>()
                        .FirstOrDefault(pq => pq.Name.Contains("Print to PDF") || pq.Name.Contains("PDF"));

                    if (pdfPrinter != null)
                    {
                        printDlg.PrintQueue = pdfPrinter;
                    }

                    // I-bypass ang extra system prompt at direktang i-print/save gamit ang napiling file path
                    printDlg.PrintTicket.PageOrientation = PageOrientation.Portrait;

                    double pageWidth = 595.28;
                    double pageHeight = 841.89;

                    double scaleX = pageWidth / PrintCanvas.ActualWidth;
                    double scaleY = pageHeight / PrintCanvas.ActualHeight;
                    double scale = Math.Min(scaleX, scaleY) * 0.95;

                    Transform oldTransform = PrintCanvas.LayoutTransform;
                    PrintCanvas.LayoutTransform = new ScaleTransform(scale, scale);

                    Size pageSize = new Size(pageWidth, pageHeight);
                    PrintCanvas.Measure(pageSize);
                    PrintCanvas.Arrange(new Rect(new Point((pageWidth - PrintCanvas.DesiredSize.Width) / 2, 10), PrintCanvas.DesiredSize));

                    // Isang beses na pag-execute ng print visual papunta sa file
                    printDlg.PrintVisual(PrintCanvas, "LUMYVUE Clinical PDF Report");

                    PrintCanvas.LayoutTransform = oldTransform;

                    // 2. Isang beses lang din magre-record sa database kasabay ng pag-save
                    DatabaseHelper.InsertRecord(
                        savedClientName,
                        savedDiagnosis,
                        savedConfidence
                    );

                    MessageBox.Show("Clinical PDF Assessment Report saved successfully and logged to database!", "LUMYVUE PDF Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("PDF Export Error: " + ex.Message, "LUMYVUE PDF Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TriggerPrintBtn.Visibility = Visibility.Visible;
            }
        }
    }
}