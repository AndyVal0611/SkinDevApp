// ============================================================================
// ClientDashboardForm_xaml.cs  —  v2 (guided auto-capture, class-coloured heatmaps)
//
// What changed vs the previous version
//   * Live camera -> on-screen face guide ("Move closer", "Center your face", ...)
//   * Scan flow driven by LiveGradCamController's state machine:
//       Live -> Scanning -> Stabilizing -> Hold Still -> Stable-Capturing
//            -> Final Analysis -> Results
//   * Auto-capture of the EXACT stable frame; Snap Now = manual capture
//   * "Model Class Scores" (honest label), four class-coloured Grad-CAM++ maps,
//     approximate region line, view selector (predicted / all / one class)
//   * Patient view vs Researcher view (live gates + metrics, capture details,
//     Pending Review window)
//   * UI never blocks the loops: every controller event is marshalled with
//     Dispatcher.BeginInvoke and coalesced.
// ============================================================================

using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SkinDevApp.AI;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;
using SkinDevApp.Scanning;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Media = System.Windows.Media;

namespace SkinDevApp.Views
{
    public partial class ClientDashboardForm : Page
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;
        private int _camIndex;                              // device currently in use
        private int _camTried;                              // devices tried in this start attempt
        private int _framesSeen;                            // frames received from the current device
        private System.Windows.Threading.DispatcherTimer _camWatchdog;

        private BitmapSource currentCapturedImage;      // uploaded image only
        private string primaryDiagnosis = "";
        private string confidencePercent = "0.0%";

        // --- live scan ----------------------------------------------------------
        private LiveGradCamController _live;
        private readonly object _frameStoreLock = new object();
        private Bitmap _latestRawFrame;
        private volatile bool _cameraLive;
        private volatile bool _resultsFrozen;

        private int _framePending;          // 1 while the UI is still drawing the last video frame
        private int _snapPending;           // 1 while a snapshot update is queued on the UI thread
        private LiveSnapshot _latestSnap;
        private bool _serviceOnline;

        private ScanResult _lastResult;     // owned by the dashboard
        private OverlayView _view = OverlayView.Predicted;
        private bool _initialized;

        private readonly bool _cropLiveFrameToSquare = true;
        private const int AnalysisWidth = 640;
        private const int FastLoopMs = 200;

        // --- brushes (frozen, created once) -----------------------------------------
        private static readonly Media.Brush OvalIdle = MakeBrush("#99FFFFFF");
        private static readonly Media.Brush OvalAdjust = MakeBrush("#F59E0B");
        private static readonly Media.Brush OvalReady = MakeBrush("#22C55E");
        private static readonly Media.Brush OvalCapture = MakeBrush("#3B82F6");

        private static Media.Brush MakeBrush(string hex)
        {
            var b = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public ClientDashboardForm()
        {
            InitializeComponent();
            _initialized = true;

            CaptureArchive.FrameCropDescription = _cropLiveFrameToSquare
                ? "live: centre square crop of the camera frame; upload: none"
                : "none";
            CaptureArchive.CameraMirrored = false;
            RegionAttribution.MirroredCamera = false;

            Unloaded += (s, e) => ResetKioskState();

            ResetKioskState();
        }

        // ============================================================================
        // Layout
        // ============================================================================

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

        private void CameraViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGuideLayout();
        }

        /// <summary>Where the (Uniform-stretched) image actually sits inside the viewport.</summary>
        private System.Windows.Rect GetDisplayedImageRect()
        {
            double W = CameraViewportGrid.ActualWidth;
            double H = CameraViewportGrid.ActualHeight;

            double iw = H, ih = H;     // default: square
            BitmapSource src = UploadedImageViewer.Source as BitmapSource;
            if (src != null && src.PixelWidth > 0 && src.PixelHeight > 0)
            {
                iw = src.PixelWidth;
                ih = src.PixelHeight;
            }

            if (W <= 0 || H <= 0 || iw <= 0 || ih <= 0)
                return new System.Windows.Rect(0, 0, Math.Max(W, 0), Math.Max(H, 0));

            double scale = Math.Min(W / iw, H / ih);
            double dw = iw * scale, dh = ih * scale;
            return new System.Windows.Rect((W - dw) / 2.0, (H - dh) / 2.0, dw, dh);
        }

        /// <summary>Place the guide oval using the same normalised layout the analyser uses.</summary>
        private void UpdateGuideLayout()
        {
            if (GuideOval == null) return;

            System.Windows.Rect r = GetDisplayedImageRect();
            if (r.Width < 10 || r.Height < 10) return;

            double rx = r.Width * FaceGuideLayout.RadiusX;
            double ry = r.Height * FaceGuideLayout.RadiusY;
            double cx = r.X + r.Width * FaceGuideLayout.CenterX;
            double cy = r.Y + r.Height * FaceGuideLayout.CenterY;

            GuideOval.Width = rx * 2.0;
            GuideOval.Height = ry * 2.0;
            Canvas.SetLeft(GuideOval, cx - rx);
            Canvas.SetTop(GuideOval, cy - ry);
        }

        // ============================================================================
        // Reset / camera lifecycle
        // ============================================================================

        private void ResetKioskState()
        {
            StopCamera();

            DetectionDotsCanvas.Children.Clear();
            currentCapturedImage = null;

            _lastResult?.Dispose();
            _lastResult = null;
            _resultsFrozen = false;

            SnapBtn.IsEnabled = false;
            AnalyzeBtn.IsEnabled = false;
            PrintBtn.IsEnabled = false;
            NewScanBtn.IsEnabled = false;
            OpenDetailsBtn.IsEnabled = false;

            SetBars(null);

            GuideCanvas.Visibility = Visibility.Collapsed;
            GuideBanner.Visibility = Visibility.Collapsed;
            StateBanner.Visibility = Visibility.Collapsed;
            HoldBar.Visibility = Visibility.Collapsed;
            LegendPanel.Visibility = Visibility.Collapsed;

            UploadedImageViewer.Source = null;
            PlaceholderPanel.Visibility = Visibility.Visible;

            VerdictTxt.Text = "Status: Ready. Click Live Cam to start a new scan.";
            RegionTxt.Text = "";
            ResearcherMetricsTxt.Text = "";
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

                if (!AiEngine.IsAvailable) AiEngine.EnsureLoaded();

                if (!AiEngine.IsAvailable)
                {
                    MessageBox.Show(
                        "The AI model is not loaded.\n\n" + (AiEngine.LoadErrorMessage ?? "Unknown error."),
                        "LUMYVUE Camera", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ResetKioskState();

                _cameraLive = true;
                _resultsFrozen = false;

                _camTried = 0;
                OpenCamera(PickCameraIndex());

                PlaceholderPanel.Visibility = Visibility.Collapsed;
                UploadedImageViewer.Visibility = Visibility.Visible;

                GuideCanvas.Visibility = Visibility.Visible;
                UpdateGuideLayout();

                SnapBtn.IsEnabled = true;
                VerdictTxt.Text = "Status: Live camera active. Position your face in the oval.";

                _live = new LiveGradCamController(GetLatestFrameMatForLiveLoop)
                {
                    FastLoopMs = FastLoopMs,
                    WorkingWidth = 640,
                    View = _view,
                    AutoCaptureEnabled = AutoCaptureChk.IsChecked == true,
                    RequireFaceForAutoCapture = true,
                    MotionThreshold = 25.0,
                    MaxHeatmapAgeSeconds = 6.0
                };

                _live.FastUpdated += OnFastUpdated;
                _live.CaptureTriggered += OnCaptureTriggered;
                _live.ScanCompleted += OnScanCompleted;
                _live.ServiceStatusChanged += OnLiveServiceStatusChanged;
                _live.Start();

                LiveStatusTxt.Text = "Connecting to Grad-CAM++ service...";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Camera Hardware Error: " + ex.Message, "LUMYVUE Camera");
            }
        }


        // ── camera selection ────────────────────────────────────────────────────

        private static readonly string[] NotARealWebcam =
            { "virtual", "obs", "ir camera", "infrared", " ir ", "snap camera", "droidcam", "ndi", "manycam", "xsplit", "depth", "camo", "nvidia broadcast", "phone link" };

        /// <summary>Prefers a normal RGB webcam; skips virtual and infrared cameras when possible.</summary>
        private int PickCameraIndex()
        {
            // Prefer Logitech BRIO
            for (int i = 0; i < videoDevices.Count; i++)
            {
                string name = (videoDevices[i].Name ?? "").ToLowerInvariant();

                if (name.Contains("brio"))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CAM] BRIO found: {videoDevices[i].Name}");

                    return i;
                }
            }

            // Otherwise prefer Logitech
            for (int i = 0; i < videoDevices.Count; i++)
            {
                string name = (videoDevices[i].Name ?? "").ToLowerInvariant();

                if (name.Contains("logitech"))
                    return i;
            }

            // Fallback
            return 0;
        }

        private void OpenCamera(int index)
        {
            CloseCameraDevice();

            _camIndex = index;
            _framesSeen = 0;
            string name = videoDevices[index].Name;
            LiveStatusTxt.Text = "Opening camera: " + name + " ...";

            videoSource = new VideoCaptureDevice(videoDevices[index].MonikerString);

            // Use a common, widely supported mode (about 640x480 or closest) to avoid
            // drivers that open silently but never deliver frames at their default mode.
            try
            {
                var caps = videoSource.VideoCapabilities;
                if (caps != null && caps.Length > 0)
                {
                    var best = caps.OrderBy(c => Math.Abs(c.FrameSize.Width - 640) + Math.Abs(c.FrameSize.Height - 480)).First();
                    videoSource.VideoResolution = best;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[CAM] caps: " + ex.Message); }

            videoSource.NewFrame += VideoSource_NewFrame;
            videoSource.VideoSourceError += VideoSource_Error;
            videoSource.Start();

            // If this device gives no frames within 4 s, try the next one.
            _camWatchdog?.Stop();
            _camWatchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _camWatchdog.Tick += CamWatchdog_Tick;
            _camWatchdog.Start();
        }

        private void CloseCameraDevice()
        {
            var src = videoSource;
            videoSource = null;
            if (src == null) return;
            try
            {
                src.NewFrame -= VideoSource_NewFrame;
                src.VideoSourceError -= VideoSource_Error;
                if (src.IsRunning) { src.SignalToStop(); }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[CAM] close: " + ex.Message); }
        }

        private void CamWatchdog_Tick(object sender, EventArgs e)
        {
            if (!_cameraLive) { _camWatchdog?.Stop(); return; }

            if (Volatile.Read(ref _framesSeen) > 0)
            {
                _camWatchdog.Stop();
                LiveStatusTxt.Text = "Camera: " + videoDevices[_camIndex].Name;
                return;
            }

            _camTried++;
            if (_camTried >= videoDevices.Count)
            {
                _camWatchdog.Stop();
                var names = string.Join("\n", Enumerable.Range(0, videoDevices.Count)
                    .Select(i => "  " + (i + 1) + ". " + videoDevices[i].Name));
                MessageBox.Show(
                    "No camera delivered any video.\n\nDevices found:\n" + names +
                    "\n\nClose other apps that use the camera (Teams, Zoom, Windows Camera, browser tabs) and check " +
                    "Settings > Privacy & security > Camera > 'Let desktop apps access your camera'.",
                    "LUMYVUE Camera", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenCamera((_camIndex + 1) % videoDevices.Count);
        }

        private void VideoSource_Error(object sender, VideoSourceErrorEventArgs eventArgs)
        {
            System.Diagnostics.Debug.WriteLine("[CAM] error: " + eventArgs.Description);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_cameraLive) LiveStatusTxt.Text = "Camera error: " + eventArgs.Description;
            }));
        }

        private void StopCamera()
        {
            _cameraLive = false;

            _camWatchdog?.Stop();
            CloseCameraDevice();

            if (_live != null)
            {
                _live.FastUpdated -= OnFastUpdated;
                _live.CaptureTriggered -= OnCaptureTriggered;
                _live.ScanCompleted -= OnScanCompleted;
                _live.ServiceStatusChanged -= OnLiveServiceStatusChanged;
                _live.Dispose();
                _live = null;
            }

            lock (_frameStoreLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = null;
            }

            Interlocked.Exchange(ref _latestSnap, null);
            LiveStatusTxt.Text = "";
        }

        // ============================================================================
        // Camera thread: store the frame, draw the (cheap) overlay, hand off to the UI
        // ============================================================================

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!_cameraLive) return;
            // _framesSeen is incremented below, only for frames that are not black

            try
            {
                using (Bitmap rawFrame = (Bitmap)eventArgs.Frame.Clone())
                using (Bitmap bitmap = PrepareFrame(rawFrame))
                {
                    if (Volatile.Read(ref _framesSeen) > 0 || !IsBlackFrame(bitmap)) Interlocked.Increment(ref _framesSeen);
                    StoreLatestFrame(bitmap);                       // the loops always get the newest frame

                    if (_resultsFrozen) return;                      // a captured frame is on screen
                    if (Volatile.Read(ref _framePending) == 1) return;   // UI still busy: drop this frame

                    BitmapSource displaySource = null;
                    LiveGradCamController live = _live;

                    if (live != null)
                    {
                        using (Mat frameMat = EnsureBgr(BitmapConverter.ToMat(bitmap)))
                        using (Mat blended = live.RenderLiveOverlay(frameMat))
                        {
                            if (blended != null)
                                displaySource = ImageInterop.MatToBitmapSource(blended);
                        }
                    }

                    if (displaySource == null)
                        displaySource = ConvertBitmapToBitmapSource(bitmap);

                    Interlocked.Exchange(ref _framePending, 1);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (_cameraLive && !_resultsFrozen)
                            {
                                UploadedImageViewer.Source = displaySource;
                            }
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _framePending, 0);
                        }
                    }));
                }
            }
            catch
            {
                // Frame stream capture catch (a dropped frame is harmless)
                Interlocked.Exchange(ref _framePending, 0);
            }
        }

        /// <summary>True when the frame is essentially black (IR / blocked / not-yet-streaming camera).</summary>
        private static bool IsBlackFrame(Bitmap bmp)
        {
            try
            {
                using (Mat m = BitmapConverter.ToMat(bmp))
                {
                    Scalar s = Cv2.Mean(m);
                    return (s.Val0 + s.Val1 + s.Val2) / 3.0 < 8.0;
                }
            }
            catch { return false; }
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

        // ============================================================================
        // Controller events (background threads -> UI via BeginInvoke, coalesced)
        // ============================================================================

        private void OnFastUpdated(LiveSnapshot s)
        {
            Interlocked.Exchange(ref _latestSnap, s);

            if (Interlocked.Exchange(ref _snapPending, 1) == 1) return;   // an update is already queued

            try
            {
                Dispatcher.BeginInvoke(new Action(ApplyLatestSnapshot));
            }
            catch
            {
                Interlocked.Exchange(ref _snapPending, 0);
            }
        }

        private void ApplyLatestSnapshot()
        {
            Interlocked.Exchange(ref _snapPending, 0);

            LiveSnapshot s = Volatile.Read(ref _latestSnap);
            if (s == null || !_cameraLive || _resultsFrozen || s.Onnx == null) return;

            // ---- Model Class Scores: smoothed, so the bars glide ---------------------
            float[] smoothed = (s.Stable != null && s.Stable.SmoothedProbabilities != null
                                && s.Stable.SmoothedProbabilities.Length == 4)
                ? s.Stable.SmoothedProbabilities : s.Onnx.Probabilities;
            SetBars(smoothed);

            ScanDecision d = s.Decision;

            // ---- state banner -------------------------------------------------------
            SetBanner(d.Message, d.State);

            // ---- guide oval + instruction + hold progress -----------------------------
            UpdateGuide(s.Quality, d);

            // ---- verdict (live read) ----------------------------------------------------
            StabilizedResult st = s.Stable;
            if (st == null || st.IsUncertain)
            {
                VerdictTxt.Text = "Analyzing… keep your face in the oval.";
                RegionTxt.Text = "";
            }
            else if (!st.IsStable)
            {
                VerdictTxt.Text = "Confirming… " + st.StatusText;
                RegionTxt.Text = "";
            }
            else
            {
                primaryDiagnosis = st.DisplayClass;
                confidencePercent = (st.DisplayConfidence * 100f).ToString("F1") + "%";
                VerdictTxt.Text = "Live read: " + primaryDiagnosis + " (" + confidencePercent + " model score)";
                RegionTxt.Text = s.HeatmapAvailable && s.HeatmapAgeSeconds < 6.0 ? s.RegionText : "";
            }

            // ---- service line -------------------------------------------------------------
            if (_serviceOnline && s.HeatmapAvailable)
                LiveStatusTxt.Text = "● Grad-CAM++ " + s.GradCamMs.ToString("0") + " ms";
            else if (_serviceOnline)
                LiveStatusTxt.Text = "● Grad-CAM++ connected — waiting for first map";
            else
                LiveStatusTxt.Text = "○ Grad-CAM++ offline — scores still live, no heatmap";

            LegendPanel.Visibility = s.HeatmapAvailable ? Visibility.Visible : Visibility.Collapsed;

            if (ResearcherChk.IsChecked == true)
                ResearcherMetricsTxt.Text = BuildLiveMetrics(s);
        }

        private void UpdateGuide(FrameQuality q, ScanDecision d)
        {
            bool showGuide = _live != null && !_resultsFrozen;
            GuideCanvas.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
            if (!showGuide) return;

            UpdateGuideLayout();

            string text = q != null ? q.GuidanceText : "";
            bool ready = q != null && q.Ready;

            if (d.State == ScanState.HoldStill) text = "Hold still…";
            else if (d.State == ScanState.StableCapturing) text = "Capturing…";
            else if (d.State == ScanState.Cooldown) text = d.Message;

            GuideTxt.Text = text;
            GuideBanner.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

            Media.Brush stroke;
            if (d.State == ScanState.StableCapturing || d.State == ScanState.FinalAnalysis) stroke = OvalCapture;
            else if (ready) stroke = OvalReady;
            else if (q == null || q.Guidance == GuidanceState.NoFace || q.Guidance == GuidanceState.Unavailable) stroke = OvalIdle;
            else stroke = OvalAdjust;

            GuideOval.Stroke = stroke;
            GuideOval.StrokeThickness = ready ? 4 : 3;

            bool showHold = d.State == ScanState.HoldStill
                         || (d.State == ScanState.Stabilizing && d.Progress > 0.0);
            HoldBar.Visibility = showHold ? Visibility.Visible : Visibility.Collapsed;
            HoldBar.Value = d.Progress;
        }

        private void SetBanner(string text, ScanState state)
        {
            if (string.IsNullOrEmpty(text) || state == ScanState.Idle)
            {
                StateBanner.Visibility = Visibility.Collapsed;
                return;
            }

            StateTxt.Text = text;
            StateBanner.Visibility = Visibility.Visible;
        }

        private void OnCaptureTriggered(string trigger, Mat frozen)
        {
            // Convert on this (background) thread; the Mat clone is ours to dispose.
            BitmapSource bmp = null;
            try { bmp = ImageInterop.MatToBitmapSource(frozen); }
            finally { frozen.Dispose(); }

            _resultsFrozen = true;      // stop the camera thread from drawing over the captured frame

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UploadedImageViewer.Source = bmp;

                GuideCanvas.Visibility = Visibility.Collapsed;
                GuideBanner.Visibility = Visibility.Collapsed;
                HoldBar.Visibility = Visibility.Collapsed;
                LegendPanel.Visibility = Visibility.Collapsed;

                SetBanner("Stable — Capturing", ScanState.StableCapturing);
                SnapBtn.IsEnabled = false;

                VerdictTxt.Text = trigger == "auto"
                    ? "Reading is stable — frame captured. Running final analysis…"
                    : "Frame captured. Running final analysis…";
                RegionTxt.Text = "";
            }));
        }

        private void OnScanCompleted(ScanResult result)
        {
            Dispatcher.BeginInvoke(new Action(() => ShowResult(result)));
        }

        private void OnLiveServiceStatusChanged(bool online, string error)
        {
            _serviceOnline = online;

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_cameraLive) return;
                    LiveStatusTxt.Text = online
                        ? "● Grad-CAM++ connected"
                        : "○ Grad-CAM++ offline — " + error;
                }));
            }
            catch (TaskCanceledException) { }
        }

        // ============================================================================
        // Results
        // ============================================================================

        private void ShowResult(ScanResult r)
        {
            if (r == null) return;

            // The page was reset while the analysis was running: discard.
            if (_live == null && currentCapturedImage == null)
            {
                r.Dispose();
                return;
            }

            _lastResult?.Dispose();
            _lastResult = r;
            _resultsFrozen = true;

            GuideCanvas.Visibility = Visibility.Collapsed;
            GuideBanner.Visibility = Visibility.Collapsed;
            HoldBar.Visibility = Visibility.Collapsed;

            NewScanBtn.IsEnabled = true;
            SnapBtn.IsEnabled = false;

            if (!r.Ok || r.Request == null || r.Request.Onnx == null)
            {
                SetBanner("Results — error", ScanState.Results);
                VerdictTxt.Text = "Analysis failed: " + (r.Error ?? "unknown error") + ". Press New Scan to try again.";
                RegionTxt.Text = "";
                return;
            }

            PredictionResult onnx = r.Request.Onnx;

            // Scores of the EXACT captured frame.
            SetBars(onnx.Probabilities);

            primaryDiagnosis = onnx.PredictedClass;
            confidencePercent = onnx.ConfidenceText;

            var verdict = new StringBuilder();
            verdict.Append("Primary prediction: ").Append(primaryDiagnosis)
                   .Append(" (").Append(confidencePercent).Append(" model score)");

            StabilizedResult st = r.Request.Stable;
            if (st != null && !string.IsNullOrEmpty(st.DisplayClass) && st.DisplayClass != onnx.PredictedClass)
                verdict.Append("\nStabilised across frames: ").Append(st.DisplayClass);

            string warning = r.Record != null && r.Record.Consistency != null ? r.Record.Consistency.Warning : null;
            if (!string.IsNullOrEmpty(warning))
                verdict.Append("\n⚠ ").Append(warning);

            VerdictTxt.Text = verdict.ToString();

            if (r.Maps != null)
            {
                RegionTxt.Text = r.Maps.RegionSummary();
                LegendPanel.Visibility = Visibility.Visible;
            }
            else
            {
                RegionTxt.Text = "Heatmap unavailable (" + (r.Error ?? "Grad-CAM++ service not reachable") +
                                 "). Scores come from the ONNX classifier only.";
                LegendPanel.Visibility = Visibility.Collapsed;
            }

            RenderResultOverlay();

            SetBanner(r.Request.Trigger == "auto" ? "Results — auto-captured" : "Results", ScanState.Results);

            PrintBtn.IsEnabled = true;
            OpenDetailsBtn.IsEnabled = !string.IsNullOrEmpty(r.Folder);

            if (ResearcherChk.IsChecked == true)
                ResearcherMetricsTxt.Text = BuildResultMetrics(r);
        }

        /// <summary>Draw the captured frame with the selected class map(s).</summary>
        private void RenderResultOverlay()
        {
            ScanResult r = _lastResult;
            if (r == null || r.Request == null || r.Request.Working == null || r.Request.Onnx == null) return;

            Mat shown;
            if (r.Maps != null)
                shown = ClassHeatmapRenderer.RenderOverlay(
                    r.Request.Working, r.Maps, _view, r.Request.Onnx.PredictedIndex);
            else
                shown = r.Request.Working.Clone();

            using (shown)
            {
                UploadedImageViewer.Source = ImageInterop.MatToBitmapSource(shown);
            }
        }

        private void SetBars(float[] probs)
        {
            double a = probs != null && probs.Length > 0 ? probs[0] * 100.0 : 0;
            double h = probs != null && probs.Length > 1 ? probs[1] * 100.0 : 0;
            double e = probs != null && probs.Length > 2 ? probs[2] * 100.0 : 0;
            double n = probs != null && probs.Length > 3 ? probs[3] * 100.0 : 0;

            AcneBar.Value = a; AcneScoreTxt.Text = a.ToString("F1") + "%";
            HyperBar.Value = h; HyperScoreTxt.Text = h.ToString("F1") + "%";
            EczemaBar.Value = e; EczemaScoreTxt.Text = e.ToString("F1") + "%";
            NormalBar.Value = n; NormalScoreTxt.Text = n.ToString("F1") + "%";
        }

        // ============================================================================
        // Buttons
        // ============================================================================

        private void SnapBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_live == null) return;

            SnapBtn.IsEnabled = false;
            VerdictTxt.Text = "Capturing…";
            _live.RequestManualCapture();

            // If nothing was captured (e.g. no frame yet), give the button back.
            Task.Delay(4000).ContinueWith(_ =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_live != null && !_resultsFrozen) SnapBtn.IsEnabled = true;
                    }));
                }
                catch { }
            });
        }

        private void NewScanBtn_Click(object sender, RoutedEventArgs e)
        {
            _lastResult?.Dispose();
            _lastResult = null;

            PrintBtn.IsEnabled = false;
            OpenDetailsBtn.IsEnabled = false;
            NewScanBtn.IsEnabled = false;
            RegionTxt.Text = "";
            SetBars(null);
            LegendPanel.Visibility = Visibility.Collapsed;

            if (_live != null)
            {
                _resultsFrozen = false;
                _live.NewScan();
                GuideCanvas.Visibility = Visibility.Visible;
                SnapBtn.IsEnabled = true;
                VerdictTxt.Text = "Status: Live camera active. Position your face in the oval.";
            }
            else
            {
                ResetKioskState();      // upload mode: back to the start screen
            }
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
                VerdictTxt.Text = "Status: Image loaded. Click Run Aesthetic Analysis.";
            }
        }

        private async void AnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUploadAnalysis();
        }

        /// <summary>Uploaded image: ONNX + all-class Grad-CAM++ + archive, same pipeline as a live capture.</summary>
        private async Task RunUploadAnalysis()
        {
            if (currentCapturedImage == null)
            {
                MessageBox.Show("Please upload an image first.", "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!AiEngine.IsAvailable) AiEngine.EnsureLoaded();

            if (!AiEngine.IsAvailable)
            {
                MessageBox.Show(
                    "The AI model is not loaded.\n\n" + (AiEngine.LoadErrorMessage ?? "Unknown error.") +
                    "\n\nSee the instructions at the top of AiEngine.cs.",
                    "LUMYVUE Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AnalyzeBtn.IsEnabled = false;
            VerdictTxt.Text = "Status: Running skin analysis…";

            try
            {
                CaptureRequest req;

                using (Mat full = ImageInterop.BitmapSourceToMat(currentCapturedImage))
                {
                    req = await Task.Run(() => ScanPipeline.BuildUploadRequest(full, AnalysisWidth));
                }

                VerdictTxt.Text = "Status: Rendering Grad-CAM++…";

                ScanResult result = await ScanPipeline.FinalAnalysisAsync(req);
                ShowResult(result);
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

        private void AutoCaptureChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            if (_live != null) _live.AutoCaptureEnabled = AutoCaptureChk.IsChecked == true;
        }

        private void OverlayViewRb_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;

            if (ReferenceEquals(sender, ViewAllRb)) _view = OverlayView.All;
            else if (ReferenceEquals(sender, ViewAcneRb)) _view = OverlayView.Acne;
            else if (ReferenceEquals(sender, ViewHyperRb)) _view = OverlayView.Hyperpigmentation;
            else if (ReferenceEquals(sender, ViewEczemaRb)) _view = OverlayView.Eczema;
            else if (ReferenceEquals(sender, ViewNormalRb)) _view = OverlayView.Normal;
            else _view = OverlayView.Predicted;

            if (_live != null)
            {
                _live.View = _view;
                _live.InvalidateOverlay();
            }

            if (_lastResult != null && _lastResult.Ok) RenderResultOverlay();
        }

        private void ResearcherChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;

            ResearcherPanel.Visibility = ResearcherChk.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;

            if (ResearcherChk.IsChecked == true && _lastResult != null && _lastResult.Ok)
                ResearcherMetricsTxt.Text = BuildResultMetrics(_lastResult);
        }

        private void OpenDetailsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null || string.IsNullOrEmpty(_lastResult.Folder)) return;
            ShowReviewWindow(_lastResult.Folder);
        }

        private void PendingBtn_Click(object sender, RoutedEventArgs e)
        {
            var pending = ReviewStore.ListPending();

            if (pending.Count == 0)
            {
                MessageBox.Show("There are no captures waiting for review.", "LUMYVUE Review");
                return;
            }

            ShowReviewWindow(pending[0]);
        }

        private void ShowReviewWindow(string folder)
        {
            try
            {
                var win = new ResearcherResultWindow(folder);
                win.Owner = Application.Current.MainWindow;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the capture: " + ex.Message, "LUMYVUE Review",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
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
            ResetKioskState();
            MainWindow mainWin = (MainWindow)Application.Current.MainWindow;
            mainWin.MainFrame.Navigate(new LoginForm());
        }

        // ============================================================================
        // Researcher view text
        // ============================================================================

        private static string Ok(bool v) { return v ? "ok" : "--"; }

        private string BuildLiveMetrics(LiveSnapshot s)
        {
            var sb = new StringBuilder();
            ScanDecision d = s.Decision;
            ScanGates g = d.Gates;
            StabilityMetrics m = d.Metrics;
            FrameQuality q = s.Quality;

            sb.AppendLine("state      : " + d.State);
            sb.AppendLine("gates      : face " + Ok(g.Face) + " | quality " + Ok(g.Quality) + " | stable " + Ok(g.Stable) +
                          " | conf " + Ok(g.Confident) + " | margin " + Ok(g.Margin) +
                          " | consistent " + Ok(g.Consistent) + " | still " + Ok(g.Still));
            if (!g.AllOk) sb.AppendLine("waiting on : " + g.FirstFailure);
            sb.AppendLine("stability  : held " + m.FramesHeldStable + "/" + m.StableFramesRequired +
                          "  consistency " + m.ConsistencyRatio.ToString("P0") +
                          "  margin " + (m.Margin * 100f).ToString("F1") + " pts");
            sb.AppendLine("motion     : " + s.Motion.ToString("F1"));

            if (q != null)
            {
                string face = q.FaceFound ? q.FaceW.ToString("P0") + " of frame width" : "no face";
                sb.AppendLine("image      : sharpness " + q.Sharpness.ToString("F0") +
                              "  brightness " + q.Brightness.ToString("F0"));
                sb.AppendLine("face guide : " + face + "  (" + q.Guidance + ")");
            }

            sb.AppendLine("fast loop  : " + s.FastLoopMs.ToString("F0") + " ms   ONNX " + s.Onnx.InferenceMs.ToString("F0") + " ms");

            if (s.HeatmapAvailable)
            {
                sb.AppendLine("grad-cam++ : " + s.GradCamMs.ToString("F0") + " ms service / " +
                              s.GradCamRoundTripMs.ToString("F0") + " ms round trip  [" + s.ExecMode + "]");
                sb.AppendLine("heatmap age: " + s.HeatmapAgeSeconds.ToString("F1") + " s   frame #" + s.FrameId);
                sb.AppendLine("ONNX vs service top class: " + (s.Disagreement
                    ? "DIFFER (" + s.Onnx.PredictedClass + " vs " + s.ServiceClass + ")"
                    : "same"));
            }
            else
            {
                sb.AppendLine("grad-cam++ : " + (s.ServiceOnline ? "waiting for first map" : "service offline"));
            }

            return sb.ToString();
        }

        private string BuildResultMetrics(ScanResult r)
        {
            var sb = new StringBuilder();

            if (r.Record == null)
            {
                sb.AppendLine("No record available.");
                return sb.ToString();
            }

            CaptureRecord rec = r.Record;
            sb.AppendLine("capture    : " + rec.CaptureId.Substring(0, 8) + "  (" + rec.Trigger + ")");
            sb.AppendLine("status     : " + rec.ReviewStatus + "   training use: " + rec.TrainingUse);
            sb.AppendLine("model      : " + rec.Model.RunName + "  onnx " + Short(rec.Model.OnnxSha256));

            if (rec.Stability != null)
                sb.AppendLine("stability  : held " + rec.Stability.FramesHeldStable + "/" + rec.Stability.StableFramesRequired +
                              "  consistency " + rec.Stability.Consistency.ToString("P0") +
                              "  motion " + rec.Stability.MotionMean.ToString("F1") + "/" + rec.Stability.MotionMax.ToString("F1"));

            if (rec.PickCameraIndex != null)
                sb.AppendLine("quality    : sharp " + rec.Quality.Sharpness.ToString("F0") +
                              "  bright " + rec.Quality.Brightness.ToString("F0") +
                              "  face " + (rec.Quality.FaceFound ? "yes" : "no"));

            if (rec.Consistency != null && rec.Consistency.KerasAvailable && rec.Consistency.MaxAbsDifference.HasValue)
                sb.AppendLine("ONNX/Keras : same top class " + (rec.Consistency.SameTopClass == true ? "yes" : "NO") +
                              "  max diff " + rec.Consistency.MaxAbsDifference.Value.ToString("F4"));

            foreach (MapSection m in rec.ClassMaps)
                sb.AppendLine(m.Class.PadRight(18) + " strength " + m.RelativeStrength.ToString("F2") + "  " +
                              (m.Diffuse ? "diffuse" : m.TopZone));

            sb.AppendLine("saved to   : " + r.Folder);
            return sb.ToString();
        }

        private static string Short(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "unknown";
            return hash.Length > 12 ? hash.Substring(0, 12) + "…" : hash;
        }
    }
}