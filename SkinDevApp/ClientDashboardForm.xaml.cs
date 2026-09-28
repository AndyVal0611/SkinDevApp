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

        // --- frame handling ---
        private Bitmap _snappedRaw;
        private volatile bool _cameraLive;

        private readonly bool _cropLiveFrameToSquare = true;
        private const int AnalysisWidth = 640;

        /// <summary>Grad-CAM++ refresh period during live preview. Reduced to 1000ms for responsiveness.</summary>
        private const int LiveGradCamRefreshMs = 1000;

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
            _snappedRaw?.Dispose();
            _snappedRaw = null;

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

                _cameraLive = true;
                videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                UploadedImageViewer.Visibility = Visibility.Visible;

                SnapBtn.IsEnabled = true;
                VerdictTxt.Text = "Status: Live Camera Active. Click Snap Frame.";

                // Start continuous, throttled Grad-CAM++ with relaxed gating thresholds
                _live = new LiveGradCamController(GetLatestFrameMatForLiveLoop)
                {
                    RefreshIntervalMs = LiveGradCamRefreshMs,
                    WorkingWidth = 640,
                    MotionThreshold = 25.0,    // Relaxed threshold to prevent flickering during head movements
                    MaxHeatmapAgeSeconds = 8.0  // Extended timeout to allow smooth response transitions
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
            if (!_cameraLive) return;

            try
            {
                using (Bitmap rawFrame = (Bitmap)eventArgs.Frame.Clone())
                using (Bitmap bitmap = PrepareFrame(rawFrame))
                {
                    StoreLatestFrame(bitmap);

                    LiveGradCamController live = _live;
                    BitmapSource displaySource = null;
                    Mat heat = live?.GetLatestHeatmapClone();

                    if (_showLiveHeatmap && live != null && heat != null && !heat.Empty())
                    {
                        using (heat)
                        using (Mat frameMat = EnsureBgr(BitmapConverter.ToMat(bitmap)))
                        {
                            double alpha = live.GetOverlayAlpha(frameMat);

                            if (alpha > 0.01)
                            {
                                using (Mat blended = HeatmapRenderer.Blend(frameMat, heat, alpha))
                                {
                                    displaySource = ImageInterop.MatToBitmapSource(blended);
                                }
                            }
                        }
                    }
                    else
                    {
                        heat?.Dispose();
                    }

                    if (displaySource == null)
                        displaySource = ConvertBitmapToBitmapSource(bitmap);

                    Dispatcher.Invoke(() =>
                    {
                        if (!_cameraLive) return;
                        UploadedImageViewer.Source = displaySource;
                    });
                }
            }
            catch
            {
                // Frame stream capture catch
            }
        }

        private Bitmap PrepareFrame(Bitmap src)
        {
            if (!_cropLiveFrameToSquare || src.Width == src.Height)
                return (Bitmap)src.Clone();

            int s = Math.Min(src.Width, src.Height);
            var r = new System.Drawing.Rectangle((src.Width - s) / 2, (src.Height - s) / 2, s, s);
            return src.Clone(r, src.PixelFormat);
        }

        private static Mat EnsureBgr(Mat m)
        {
            if (m.Channels() == 3) return m;

            Mat bgr = new Mat();
            Cv2.CvtColor(m, bgr, m.Channels() == 4
                ? ColorConversionCodes.BGRA2BGR : ColorConversionCodes.GRAY2BGR);
            m.Dispose();
            return bgr;
        }

        private void StoreLatestFrame(Bitmap frame)
        {
            lock (_frameStoreLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = (Bitmap)frame.Clone();
            }
        }

        private Mat GetLatestFrameMatForLiveLoop()
        {
            lock (_frameStoreLock)
            {
                if (_latestRawFrame == null) return null;
                return EnsureBgr(BitmapConverter.ToMat(_latestRawFrame));
            }
        }

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
            catch (System.Threading.Tasks.TaskCanceledException) { }
        }

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
            Bitmap snapped = null;

            lock (_frameStoreLock)
            {
                if (_latestRawFrame != null)
                    snapped = (Bitmap)_latestRawFrame.Clone();
            }

            if (snapped == null) return;

            StopCamera();

            _snappedRaw?.Dispose();
            _snappedRaw = snapped;

            BitmapSource frozen = ConvertBitmapToBitmapSource(snapped);
            UploadedImageViewer.Source = frozen;
            currentCapturedImage = frozen;

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
                using (Mat frame = GetAnalysisMat())
                {
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

                    byte[] pngBytes = frame.ImEncode(".png");

                    using (GradCamResult gradcam = await GradCamService.ExplainAsync(
                        pngBytes, classIndex: null, method: "gradcam++"))
                    {
                        if (gradcam.Ok && gradcam.Overlay != null)
                        {
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

        private Mat GetAnalysisMat()
        {
            Mat full = _snappedRaw != null
                ? EnsureBgr(BitmapConverter.ToMat(_snappedRaw))
                : ImageInterop.BitmapSourceToMat(currentCapturedImage);

            using (full)
            {
                return LiveGradCamController.ResizeToWidth(full, AnalysisWidth);
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

                RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                    (int)ImageCard.ActualWidth,
                    (int)ImageCard.ActualHeight,
                    96d, 96d, System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(ImageCard);

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
            _cameraLive = false;

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