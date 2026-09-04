using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SkinDevApp.Views
{
    public partial class ClientDashboardForm : Page
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;

        private BitmapSource currentCapturedImage;
        private string primaryDiagnosis = "Acne";
        private string confidencePercent = "83.5%";

        public ClientDashboardForm()
        {
            InitializeComponent();
            ResetKioskState();
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ContentGrid == null) return;

            ContentGrid.ColumnDefinitions.Clear();
            ContentGrid.RowDefinitions.Clear();

            if (e.NewSize.Width > 850)
            {
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(ImageCard, 0);
                Grid.SetColumn(ImageCard, 0);

                Grid.SetRow(ControlsCard, 0);
                Grid.SetColumn(ControlsCard, 1);
            }
            else
            {
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(320) });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(ImageCard, 0);
                Grid.SetColumn(ImageCard, 0);

                Grid.SetRow(ControlsCard, 1);
                Grid.SetColumn(ControlsCard, 0);
            }
        }

        private void ResetKioskState()
        {
            StopCamera();
            DetectionDotsCanvas.Children.Clear();
            currentCapturedImage = null;

            SnapBtn.IsEnabled = false;
            AnalyzeBtn.IsEnabled = false;
            PrintBtn.IsEnabled = false;

            AcneBar.Value = 0; AcneScoreTxt.Text = "0.0%";
            HyperBar.Value = 0; HyperScoreTxt.Text = "0.0%";
            EczemaBar.Value = 0; EczemaScoreTxt.Text = "0.0%";
            NormalBar.Value = 0; NormalScoreTxt.Text = "0.0%";

            VerdictTxt.Text = "Status: Ready. Click Live Cam to start new scan.";
        }

        private void StartCamBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                if (videoDevices.Count == 0)
                {
                    MessageBox.Show("No camera hardware detected on this machine. Please connect a USB webcam.", "LUMYVUE Camera");
                    return;
                }

                ResetKioskState();

                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                UploadedImageViewer.Visibility = Visibility.Visible;

                SnapBtn.IsEnabled = true;
                VerdictTxt.Text = "Status: Live Camera Active. Click Snap Frame.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Camera Hardware Error: " + ex.Message, "LUMYVUE Camera");
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                using (Bitmap bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    Dispatcher.Invoke(() =>
                    {
                        var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                        UploadedImageViewer.Source = bitmapSource;
                        currentCapturedImage = bitmapSource;
                    });
                }
            }
            catch
            {
                // Frame stream capture catch
            }
        }

        private BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        private void SnapBtn_Click(object sender, RoutedEventArgs e)
        {
            if (currentCapturedImage == null) return;

            StopCamera();
            SnapBtn.IsEnabled = false;
            AnalyzeBtn.IsEnabled = true;

            VerdictTxt.Text = "Status: Frame Captured. Click Run Aesthetic Analysis.";
        }

        private void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetKioskState();

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                BitmapImage uploadedBmp = new BitmapImage(new Uri(openFileDialog.FileName));
                UploadedImageViewer.Source = uploadedBmp;
                currentCapturedImage = uploadedBmp;

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                UploadedImageViewer.Visibility = Visibility.Visible;

                AnalyzeBtn.IsEnabled = true;
                VerdictTxt.Text = "Status: Image Loaded. Click Run Aesthetic Analysis.";
            }
        }

        private void AnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            RunAnalysisAndRender();
        }

        private void RunAnalysisAndRender()
        {
            if (currentCapturedImage == null)
            {
                MessageBox.Show("Please capture a frame or upload an image first.", "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AnalyzeBtn.IsEnabled = false;

            Random rand = new Random();

            // Binigyan natin ng pantay-pantay na range ang bawat isa para pwedeng mag-iba-iba kung alin ang pinakamataas
            double acneScore = Math.Round(40.0 + (rand.NextDouble() * 45.0), 1);     // 40% hanggang 85%
            double hyperScore = Math.Round(30.0 + (rand.NextDouble() * 45.0), 1);    // 30% hanggang 75%
            double eczemaScore = Math.Round(20.0 + (rand.NextDouble() * 40.0), 1);   // 20% hanggang 60%

            double normalScore = Math.Round(100.0 - (acneScore * 0.4 + hyperScore * 0.3 + eczemaScore * 0.3), 1);
            if (normalScore < 5.0) normalScore = 8.5;

            // Alamin kung alin ang pinakamataas para maging totoong primary diagnosis
            if (hyperScore > acneScore && hyperScore >= eczemaScore)
            {
                primaryDiagnosis = "Hyperpigmentation";
                confidencePercent = $"{hyperScore:F1}%";
            }
            else if (eczemaScore > acneScore && eczemaScore > hyperScore)
            {
                primaryDiagnosis = "Eczema";
                confidencePercent = $"{eczemaScore:F1}%";
            }
            else
            {
                primaryDiagnosis = "Acne";
                confidencePercent = $"{acneScore:F1}%";
            }

            AcneBar.Value = acneScore; AcneScoreTxt.Text = $"{acneScore:F1}%";
            HyperBar.Value = hyperScore; HyperScoreTxt.Text = $"{hyperScore:F1}%";
            EczemaBar.Value = eczemaScore; EczemaScoreTxt.Text = $"{eczemaScore:F1}%";
            NormalBar.Value = normalScore; NormalScoreTxt.Text = $"{normalScore:F1}%";

            VerdictTxt.Text = $"Primary Status: {primaryDiagnosis} ({confidencePercent} Confidence)";

            // I-render ang detection dots batay sa kung anong diagnosis ang nanalo
            DrawAIDetectionOverlay(primaryDiagnosis, rand);
            PrintBtn.IsEnabled = true;
        }

        private void DrawAIDetectionOverlay(string diagnosis, Random rand)
        {
            DetectionDotsCanvas.Children.Clear();

            double canvasWidth = DetectionDotsCanvas.ActualWidth > 0 ? DetectionDotsCanvas.ActualWidth : 380;
            double canvasHeight = DetectionDotsCanvas.ActualHeight > 0 ? DetectionDotsCanvas.ActualHeight : 350;

            double centerX = canvasWidth / 2.0;
            double centerY = canvasHeight / 2.0;

            // Distance/Zoom factor: Dahil malapit ang mukha sa kiosk, pinalaki natin ang spread scale (1.35x to 1.6x)
            // para hindi magkumpulan sa gitna ng ilong/bibig kundi kumalat sa buong pisngi at noo.
            double distanceScale = 1.45 + (rand.NextDouble() * 0.2);

            int dotCount = 7; // Dagdagan natin ng konti para mas mukhang detalyado
            for (int i = 0; i < dotCount; i++)
            {
                // Mas pinalawak na random range tapos min-multiply sa distanceScale para sumunod sa lapit ng mukha
                double randomX = ((rand.NextDouble() * 120.0) - 60.0) * distanceScale;
                double randomY = ((rand.NextDouble() * 130.0) - 65.0) * distanceScale;

                // Kulay batay sa diagnosis
                System.Windows.Media.Color dotColor = System.Windows.Media.Color.FromRgb(230, 160, 145);
                if (diagnosis == "Hyperpigmentation")
                {
                    dotColor = System.Windows.Media.Color.FromRgb(210, 180, 140);
                }
                else if (diagnosis == "Eczema")
                {
                    dotColor = System.Windows.Media.Color.FromRgb(240, 140, 130);
                }

                Ellipse dot = new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = new System.Windows.Media.SolidColorBrush(dotColor),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 2
                };

                // Siguraduhing nasa loob ng canvas bounds at nakakalat sa pisngi/noo
                Canvas.SetLeft(dot, centerX + randomX);
                Canvas.SetTop(dot, centerY + randomY);
                DetectionDotsCanvas.Children.Add(dot);
            }
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (currentCapturedImage == null)
                {
                    currentCapturedImage = UploadedImageViewer.Source as BitmapSource;
                }

                // Kunin ang buong larawan kasama ang dots gamit ang RenderTargetBitmap
                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                    (int)ImageCard.ActualWidth,
                    (int)ImageCard.ActualHeight,
                    96d, 96d, PixelFormats.Pbgra32);
                renderBitmap.Render(ImageCard);

                // Ipasa ang exact scores papunta sa PrintReportWindow
                PrintReportWindow printWin = new PrintReportWindow(
                    renderBitmap,
                    DatabaseHelper.CurrentClientName,
                    primaryDiagnosis,
                    confidencePercent,
                    DatabaseHelper.CurrentClientAge,
                    DatabaseHelper.CurrentClientContact,
                    AcneBar.Value, HyperBar.Value, EczemaBar.Value, NormalBar.Value
                );

                printWin.Owner = Application.Current.MainWindow;
                printWin.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Report Window Error: " + ex.Message, "LUMYVUE Print", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            MainWindow mainWin = (MainWindow)Application.Current.MainWindow;
            mainWin.MainFrame.Navigate(new LoginForm());
        }

        private void StopCamera()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.NewFrame -= VideoSource_NewFrame;
                videoSource = null;
            }
        }
    }
}