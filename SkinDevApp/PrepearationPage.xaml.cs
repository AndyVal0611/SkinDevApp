using AForge.Video.DirectShow;
using SkinDevApp.Data;
using SkinDevApp.Views;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SkinDevApp
{
    /// <summary>
    /// Pre-analysis preparation: camera check and patient reminders before scanning.
    /// </summary>
    public partial class PreparationPage : Page
    {
        private bool _cameraReady;
        private bool _lightingConfirmed;
        private bool _distanceConfirmed;
        private bool _faceConfirmed;
        private bool _modelReady;

        private static readonly SolidColorBrush PendingBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
        private static readonly SolidColorBrush ReadyBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#588157"));
        private static readonly SolidColorBrush WarningBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D4A373"));
        private static readonly SolidColorBrush FailedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));

        public PreparationPage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Preparation");

            string id = AppSession.CurrentParticipantId;
            Participant p = string.IsNullOrEmpty(id) ? null : StudyRepository.GetParticipant(id);
            ParticipantLine.Text = p == null
                ? "No participant selected. Register or select a participant before scanning."
                : "Participant " + p.ParticipantID + "  ·  " + p.FullName + ".  Complete each step, then press START SCAN.";
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RunCameraCheck();
            RefreshOverallStatus();
            await RunServiceChecks();
        }

        /// <summary>AI classifier (required) and Grad-CAM++ service (optional: scan continues without heatmaps).</summary>
        private async System.Threading.Tasks.Task RunServiceChecks()
        {
            SystemHealth h;
            try { h = await SystemHealth.CheckAsync(); }
            catch (Exception ex)
            {
                SetStatus(ModelStatusDot, ModelStatusText, WarningBrush, "Check failed");
                ModelDetailText.Text = ex.Message;
                return;
            }

            _modelReady = h.ModelOk;
            SetStatus(ModelStatusDot, ModelStatusText, h.ModelOk ? ReadyBrush : FailedBrush, h.ModelOk ? "Loaded" : "Unavailable");
            ModelDetailText.Text = h.ModelText;

            SetStatus(GradCamStatusDot, GradCamStatusText, h.GradCamOk ? ReadyBrush : WarningBrush, h.GradCamOk ? "Available" : "Unavailable");
            GradCamDetailText.Text = h.GradCamOk
                ? h.GradCamText
                : "Grad-CAM++ unavailable. The scan still runs and saves the class scores; heatmaps will be missing. Start gradcam_service.py to enable them.";

            RefreshOverallStatus();
        }

        private void RunCameraCheck()
        {
            SetStatus(CameraStatusDot, CameraStatusText, PendingBrush, "Checking...");
            CameraDetailText.Text = "Scanning for video input devices...";

            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (devices.Count == 0)
                {
                    _cameraReady = false;
                    SetStatus(CameraStatusDot, CameraStatusText, FailedBrush, "Not detected");
                    CameraDetailText.Text = "No camera found. Connect a USB webcam and tap Re-check camera.";
                    return;
                }

                _cameraReady = true;
                SetStatus(CameraStatusDot, CameraStatusText, ReadyBrush, "Connected");
                var label = devices[0].Name;
                CameraDetailText.Text = devices.Count == 1
                    ? $"Using: {label}"
                    : $"Primary device: {label} ({devices.Count} cameras available)";
            }
            catch (Exception ex)
            {
                _cameraReady = false;
                SetStatus(CameraStatusDot, CameraStatusText, WarningBrush, "Check failed");
                CameraDetailText.Text = $"Could not verify camera: {ex.Message}";
            }
            finally
            {
                RefreshOverallStatus();
            }
        }

        private void RecheckCameraBtn_Click(object sender, RoutedEventArgs e)
        {
            RunCameraCheck();
        }

        private void LightingConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            _lightingConfirmed = true;
            SetStatus(LightingStatusDot, LightingStatusText, ReadyBrush, "Confirmed");
            LightingConfirmBtn.IsEnabled = false;
            LightingConfirmBtn.Content = "Lighting confirmed";
            RefreshOverallStatus();
        }

        private void DistanceConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            _distanceConfirmed = true;
            SetStatus(DistanceStatusDot, DistanceStatusText, ReadyBrush, "Confirmed");
            DistanceConfirmBtn.IsEnabled = false;
            DistanceConfirmBtn.Content = "Distance confirmed";
            RefreshOverallStatus();
        }

        private void FaceConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            _faceConfirmed = true;
            SetStatus(FaceStatusDot, FaceStatusText, ReadyBrush, "Confirmed");
            FaceConfirmBtn.IsEnabled = false;
            FaceConfirmBtn.Content = "Position confirmed";
            RefreshOverallStatus();
        }

        private void RefreshOverallStatus()
        {
            int complete = 0;
            if (_cameraReady) complete++;
            if (_lightingConfirmed) complete++;
            if (_distanceConfirmed) complete++;
            if (_faceConfirmed) complete++;

            OverallStatusDetail.Text = $"{complete} of 4 checks complete";

            if (complete == 4 && !_modelReady)
            {
                SetStatus(OverallStatusDot, null, FailedBrush, null);
                OverallStatusTitle.Text = "AI classifier not loaded — scanning is not possible";
                NextBtn.IsEnabled = false;
            }
            else if (complete == 4)
            {
                SetStatus(OverallStatusDot, null, ReadyBrush, null);
                OverallStatusTitle.Text = "Ready for analysis";
                NextBtn.IsEnabled = true;
            }
            else if (!_cameraReady)
            {
                SetStatus(OverallStatusDot, null, FailedBrush, null);
                OverallStatusTitle.Text = "Camera required before continuing";
                NextBtn.IsEnabled = false;
            }
            else
            {
                SetStatus(OverallStatusDot, null, PendingBrush, null);
                OverallStatusTitle.Text = "Preparation in progress";
                NextBtn.IsEnabled = false;
            }
        }

        private static void SetStatus(Ellipse dot, TextBlock label, SolidColorBrush brush, string text)
        {
            if (dot != null)
                dot.Fill = brush;
            if (label != null)
            {
                label.Text = text;
                label.Foreground = brush;
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            Nav.Back();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_cameraReady || !_lightingConfirmed || !_distanceConfirmed || !_faceConfirmed)
            {
                MessageBox.Show(
                    "Please complete all preparation steps before continuing.",
                    "LUMYVUE Preparation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string id = AppSession.CurrentParticipantId;
            if (string.IsNullOrEmpty(id))
            {
                MessageBox.Show("Register or select a participant before scanning.", "LUMYVUE Preparation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!Workflow.HasScanConsent(id))
            {
                MessageBox.Show("Required consent has not been recorded for " + id + ". The consent screen will open.",
                    "LUMYVUE Preparation", MessageBoxButton.OK, MessageBoxImage.Information);
                Nav.Go(new ConsentPage(id));
                return;
            }

            Nav.Go(new ClientDashboardForm());
        }
    }
}
