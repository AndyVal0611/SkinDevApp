using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SkinDevApp.AI;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SkinDevApp.Views
{
    public partial class ClientDashboardForm : Page
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;

        private BitmapSource currentCapturedImage;
        private string primaryDiagnosis = "Acne";
        private string confidencePercent = "0.0%";

        // --- continuous live Grad-CAM++ (runs while the camera is streaming) ---
        private LiveGradCamController _live;
        private readonly object _frameStoreLock = new object();
        private Bitmap _latestRawFrame;
        private readonly bool _showLiveHeatmap = true;

        /// <summary>Grad-CAM++ refresh period during live preview. 2000ms is
        /// the tested default for a CPU-only deployment machine.</summary>
        private const int LiveGradCamRefreshMs = 2000;

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

                // Start continuous, throttled Grad-CAM++ in the background.
                // The preview stays at full frame rate; the heatmap behind it
                // refreshes roughly every 2 seconds - see LiveGradCamController.
                _live = new LiveGradCamController(GetLatestFrameMatForLiveLoop)
                {
                    RefreshIntervalMs = LiveGradCamRefreshMs,
                    WorkingWidth = 640
                };
                _live.AnalysisUpdated += OnLiveAnalysisUpdated;
                _live.ServiceStatusChanged += OnLiveServiceStatusChanged;
                _live.Start();

                LiveStatusTxt.Text = "Connecting to Grad-CAM++ service...";
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
                    // Give the background analysis loop a copy to work from.
                    StoreLatestFrame(bitmap);

                    BitmapSource displaySource;
                    Mat heat = _live?.GetLatestHeatmapClone();

                    if (_showLiveHeatmap && heat != null && !heat.Empty())
                    {
                        // Composite the most recent heatmap onto THIS frame, so
                        // the preview stays at full frame rate even though the
                        // gradient computation behind it only refreshes every
                        // ~2 seconds. Fades if the service stalls, rather than
                        // silently showing a stale result forever.
                        using (heat)
                        using (Mat frameMat = BitmapConverter.ToMat(bitmap))
                        {
                            double alpha = HeatmapRenderer.AgeAdjustedAlpha(_live.HeatmapAge);

                            using (Mat blended = HeatmapRenderer.Blend(frameMat, heat, alpha))
                            {
                                displaySource = ImageInterop.MatToBitmapSource(blended);
                            }
                        }
                    }
                    else
                    {
                        heat?.Dispose();
                        displaySource = ConvertBitmapToBitmapSource(bitmap);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        UploadedImageViewer.Source = displaySource;
                        currentCapturedImage = displaySource;
                    });
                }
            }
            catch
            {
                // Frame stream capture catch
            }
        }

        /// <summary>Keeps a copy of the latest raw camera frame for the
        /// background analysis loop, independent of what's on screen.</summary>
        private void StoreLatestFrame(Bitmap frame)
        {
            lock (_frameStoreLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = (Bitmap)frame.Clone();
            }
        }

        /// <summary>Frame provider passed to LiveGradCamController. Returns a
        /// fresh Mat from the latest stored frame, or null if none yet.</summary>
        private Mat GetLatestFrameMatForLiveLoop()
        {
            lock (_frameStoreLock)
            {
                if (_latestRawFrame == null) return null;
                return BitmapConverter.ToMat(_latestRawFrame);
            }
        }

        /// <summary>Raised on the background thread roughly every 2 seconds
        /// with a fresh classification. Updates the live prediction display.</summary>
        private void OnLiveAnalysisUpdated(LiveInfo info)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (info.Onnx == null) return;

                    double acneScore = info.Onnx.Probabilities[0] * 100.0;
                    double hyperScore = info.Onnx.Probabilities[1] * 100.0;
                    double eczemaScore = info.Onnx.Probabilities[2] * 100.0;
                    double normalScore = info.Onnx.Probabilities[3] * 100.0;

                    AcneBar.Value = acneScore; AcneScoreTxt.Text = $"{acneScore:F1}%";
                    HyperBar.Value = hyperScore; HyperScoreTxt.Text = $"{hyperScore:F1}%";
                    EczemaBar.Value = eczemaScore; EczemaScoreTxt.Text = $"{eczemaScore:F1}%";
                    NormalBar.Value = normalScore; NormalScoreTxt.Text = $"{normalScore:F1}%";

                    primaryDiagnosis = info.Onnx.PredictedClass;
                    confidencePercent = info.Onnx.ConfidenceText;

                    if (info.Disagreement)
                    {
                        LiveStatusTxt.Text =
                            $"⚠ ONNX/Keras mismatch: {info.Onnx.PredictedClass} vs {info.ServiceClass}";
                    }
                    else
                    {
                        LiveStatusTxt.Text = $"● Live Grad-CAM++ - {info.GradCamMs:0} ms";
                    }

                    VerdictTxt.Text = $"Primary Status: {primaryDiagnosis} ({confidencePercent} Confidence) [Live]";
                });
            }
            catch (System.Threading.Tasks.TaskCanceledException) { /* page closing */ }
        }

        /// <summary>Raised when the Grad-CAM service goes offline or recovers.</summary>
        private void OnLiveServiceStatusChanged(bool online, string error)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LiveStatusTxt.Text = online
                        ? "● Live Grad-CAM++ connected"
                        : $"○ Grad-CAM++ offline - {error}";
                });
            }
            catch (System.Threading.Tasks.TaskCanceledException) { }
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

        // ---------------------------------------------------------------------
        // REAL ANALYSIS - replaces the previous Random()-based fake predictor.
        //
        //   1. Convert whatever is currently displayed (webcam snap or an
        //      uploaded file) into an OpenCV BGR Mat.
        //   2. Run the real ONNX classifier on it (AiEngine.Predict).
        //   3. Send the SAME bytes to the local Grad-CAM++ Python service and
        //      replace the displayed image with the real heatmap overlay.
        //
        // If the Grad-CAM service isn't running, the classification result
        // still shows - only the heatmap is skipped, with a message saying so.
        // ---------------------------------------------------------------------

        private async void AnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunAnalysisAndRender();
        }

        private async Task RunAnalysisAndRender()
        {
            if (currentCapturedImage == null)
            {
                MessageBox.Show("Please capture a frame or upload an image first.", "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!AiEngine.IsAvailable)
            {
                AiEngine.EnsureLoaded();
            }

            if (!AiEngine.IsAvailable)
            {
                MessageBox.Show(
                    "The AI model is not loaded.\n\n" +
                    (AiEngine.LoadErrorMessage ?? "Unknown error.") +
                    "\n\nSee the instructions at the top of AiEngine.cs.",
                    "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AnalyzeBtn.IsEnabled = false;
            VerdictTxt.Text = "Status: Running skin analysis...";

            try
            {
                using (Mat frame = ImageInterop.BitmapSourceToMat(currentCapturedImage))
                {
                    // ---- 1. real ONNX classification -------------------------
                    PredictionResult prediction = await Task.Run(() => AiEngine.Predict(frame));

                    double acneScore = prediction.Probabilities[0] * 100.0;
                    double hyperScore = prediction.Probabilities[1] * 100.0;
                    double eczemaScore = prediction.Probabilities[2] * 100.0;
                    double normalScore = prediction.Probabilities[3] * 100.0;

                    primaryDiagnosis = prediction.PredictedClass;
                    confidencePercent = prediction.ConfidenceText;

                    AcneBar.Value = acneScore; AcneScoreTxt.Text = $"{acneScore:F1}%";
                    HyperBar.Value = hyperScore; HyperScoreTxt.Text = $"{hyperScore:F1}%";
                    EczemaBar.Value = eczemaScore; EczemaScoreTxt.Text = $"{eczemaScore:F1}%";
                    NormalBar.Value = normalScore; NormalScoreTxt.Text = $"{normalScore:F1}%";

                    VerdictTxt.Text = $"Primary Status: {primaryDiagnosis} ({confidencePercent} Confidence)  |  Rendering Grad-CAM++...";

                    // ---- 2. real Grad-CAM++ on the SAME bytes -----------------
                    byte[] pngBytes = frame.ImEncode(".png");

                    using (GradCamResult gradcam = await GradCamService.ExplainAsync(
                        pngBytes, classIndex: null, method: "gradcam++"))
                    {
                        if (gradcam.Ok && gradcam.Overlay != null)
                        {
                            // Replace the displayed image with the real heatmap
                            // overlay. PrintBtn_Click renders whatever is
                            // currently shown here, so the printed report
                            // automatically includes it - no changes needed there.
                            UploadedImageViewer.Source = ImageInterop.ToBitmapSource(gradcam.Overlay);

                            if (gradcam.PredictedIndex != prediction.PredictedIndex)
                            {
                                VerdictTxt.Text =
                                    $"WARNING: ONNX says {prediction.PredictedClass}, but the Grad-CAM " +
                                    $"service says {gradcam.PredictedClass}. This indicates a preprocessing " +
                                    "mismatch - do not trust this result until it's fixed.";
                            }
                            else
                            {
                                VerdictTxt.Text = $"Primary Status: {primaryDiagnosis} ({confidencePercent} Confidence)";
                            }
                        }
                        else
                        {
                            // Classification is still valid and shown - only the
                            // heatmap is unavailable (service probably not running).
                            VerdictTxt.Text =
                                $"Primary Status: {primaryDiagnosis} ({confidencePercent} Confidence)  " +
                                $"[Grad-CAM unavailable: {gradcam.Error}]";
                        }
                    }

                    PrintBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Analysis Error: " + ex.Message, "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
                VerdictTxt.Text = "Status: Analysis failed. Please try again.";
            }
            finally
            {
                AnalyzeBtn.IsEnabled = true;
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

                // Kunin ang buong larawan kasama ang overlay gamit ang RenderTargetBitmap
                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                    (int)ImageCard.ActualWidth,
                    (int)ImageCard.ActualHeight,
                    96d, 96d, System.Windows.Media.PixelFormats.Pbgra32);
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

            if (_live != null)
            {
                _live.AnalysisUpdated -= OnLiveAnalysisUpdated;
                _live.ServiceStatusChanged -= OnLiveServiceStatusChanged;
                _live.Dispose();
                _live = null;
            }

            lock (_frameStoreLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = null;
            }

            LiveStatusTxt.Text = "";
        }
    }
}