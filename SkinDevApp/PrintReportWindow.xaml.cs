using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkinDevApp.Imaging;
using SkinDevApp.Scanning;

namespace SkinDevApp.Views
{
    public partial class PrintReportWindow : Window
    {
        private string savedDiagnosis;
        private string savedConfidence;
        private string savedClientName;
        private Border _shotsPage;          // page 2: every captured view with its Grad-CAM++ maps

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

        // ------------------------------------------------------------------
        // Page 2: every captured view (Front / Left / Right) with the original and the
        // four class-specific Grad-CAM++ maps, each with that view's score.
        // ------------------------------------------------------------------

        /// <summary>Add the "all captured views" page to the report. Safe to skip.</summary>
        public void SetShots(GalleryModel model)
        {
            if (model == null || PagesPanel == null) return;
            _shotsPage = BuildShotsPage(model);
            PagesPanel.Children.Add(_shotsPage);
        }

        private static SolidColorBrush Hex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static TextBlock Txt(string text, double size, bool bold, string color, Thickness margin)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Hex(color),
                TextWrapping = TextWrapping.Wrap,
                Margin = margin
            };
        }

        private Border BuildShotsPage(GalleryModel model)
        {
            var page = new Border
            {
                Width = 595,
                Height = 842,
                Background = Brushes.White,
                Padding = new Thickness(35, 40, 35, 35),
                Margin = new Thickness(0, 16, 0, 0),
                CornerRadius = new CornerRadius(4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, Opacity = 0.1, Direction = 270 }
            };

            var root = new StackPanel();
            root.Children.Add(Txt("LUMYVUE", 22, true, "#1A2328", new Thickness(0, 0, 0, 2)));
            root.Children.Add(Txt("Captured views and Grad-CAM++ attribution maps", 11, true, "#8A827A", new Thickness(0, 0, 0, 6)));
            root.Children.Add(new Border { Height = 2, Background = Hex("#1A2328"), Margin = new Thickness(0, 0, 0, 8) });

            string who = model.Session != null ? "Patient: " + model.Session.PatientId + "   |   Scan " +
                         (model.Session.SessionId ?? "").Substring(0, Math.Min(8, (model.Session.SessionId ?? "").Length))
                         : "Single capture";
            root.Children.Add(Txt(who + "   |   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 9, false, "#555555", new Thickness(0, 0, 0, 10)));

            foreach (GalleryView v in model.Views)
            {
                string head = v.TabTitle.ToUpper() + (v.Available ? "   -   " + v.SummaryLine : "   -   not captured");
                root.Children.Add(Txt(head, 10, true, "#1A2328", new Thickness(0, 4, 0, 3)));

                if (!v.Available) continue;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                for (int i = 0; i < v.Cards.Count; i++)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int i = 0; i < v.Cards.Count; i++)
                {
                    GalleryCard c = v.Cards[i];

                    var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 3, 0, i == v.Cards.Count - 1 ? 0 : 3, 0) };
                    cell.Children.Add(new Border
                    {
                        BorderBrush = Hex(c.ColorHex),
                        BorderThickness = new Thickness(2),
                        Background = Hex("#111827"),
                        Height = 96,
                        Child = new Image { Source = GalleryUi.LoadBitmap(c.ImagePath), Stretch = Stretch.Uniform }
                    });

                    string cap = c.IsOriginal ? "Original"
                        : ClassPalette.Names[c.ClassIndex] + (c.ScorePercent.HasValue ? " " + c.ScorePercent.Value.ToString("0.0") + "%" : "");
                    cell.Children.Add(Txt(cap, 7.5, true, "#1A2328", new Thickness(0, 2, 0, 0)));
                    if (!c.IsOriginal && c.Diffuse)
                        cell.Children.Add(Txt("diffuse attribution", 6.5, false, "#8A5A00", new Thickness(0)));

                    Grid.SetColumn(cell, i);
                    row.Children.Add(cell);
                }
                root.Children.Add(row);
            }

            FusedResult f = model.Fusion;
            if (f != null && f.ViewsUsed > 0)
            {
                root.Children.Add(new Border { Height = 1, Background = Hex("#E2EADF"), Margin = new Thickness(0, 2, 0, 6) });
                root.Children.Add(Txt("Overall (" + f.ViewsUsed + " of " + f.ViewsExpected + " views): " + f.TopClass + " " +
                                      f.TopClassPercent.ToString("0.0") + "% pooled model score" +
                                      (f.Disagreement ? "  -  views differ, see each angle above" : "  -  views agree"),
                                      9, true, "#1A2328", new Thickness(0, 0, 0, 2)));
                foreach (string n in f.Notes)
                    root.Children.Add(Txt("- " + n, 8, false, "#8A5A00", new Thickness(0, 0, 0, 1)));
            }

            root.Children.Add(Txt(GalleryModel.AttributionStatement, 7.5, false, "#555555", new Thickness(0, 8, 0, 0)));
            root.Children.Add(Txt("Scores are the model's class scores for that view (not independent disease probabilities). " +
                                  "This report is a screening aid, not a diagnosis.", 7.5, false, "#777777", new Thickness(0, 3, 0, 0)));

            page.Child = root;
            return page;
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

                    // A4 at 96 dpi. Every page is printed from its on-screen visual, so page 1 (the
                    // summary) and page 2 (all captured views) end up in ONE multi-page PDF.
                    const double pw = 793.7, ph = 1122.5;

                    var pages = new System.Collections.Generic.List<FrameworkElement> { PrintCanvas };
                    if (_shotsPage != null) pages.Add(_shotsPage);

                    var oldEffects = pages.Select(pg => pg.Effect).ToList();
                    foreach (FrameworkElement pg in pages) pg.Effect = null;

                    try
                    {
                        var doc = new FixedDocument();
                        doc.DocumentPaginator.PageSize = new Size(pw, ph);

                        foreach (FrameworkElement page in pages)
                        {
                            var fixedPage = new FixedPage { Width = pw, Height = ph, Background = Brushes.White };
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = pw,
                                Height = ph,
                                Fill = new VisualBrush(page)
                                {
                                    Stretch = Stretch.Uniform,
                                    AlignmentX = AlignmentX.Center,
                                    AlignmentY = AlignmentY.Top
                                }
                            };
                            fixedPage.Children.Add(rect);

                            var content = new PageContent();
                            ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
                            doc.Pages.Add(content);
                        }

                        printDlg.PrintDocument(doc.DocumentPaginator, "LUMYVUE Clinical PDF Report");
                    }
                    finally
                    {
                        for (int i = 0; i < pages.Count; i++) pages[i].Effect = oldEffects[i];
                    }

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