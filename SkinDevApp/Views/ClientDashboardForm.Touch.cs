// ============================================================================
// ClientDashboardForm.Touch.cs  -  namespace SkinDevApp.Views
//
// Touchscreen and portrait-monitor support for the scan screen:
//   * pinch / drag / wheel zoom of the live preview (TouchZoom - display only, never the captured frames)
//   * zoom buttons, reset button
//   * the Front / Left / Right step indicator
//   * the portrait layout (large camera on top, controls below)
// Nothing here touches the camera, the classifier, Grad-CAM++ or the lesion detector.
// ============================================================================

using System;
using System.ComponentModel;
using System.Linq;
using AForge.Video.DirectShow;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkinDevApp.Data;
using SkinDevApp.Scanning;

namespace SkinDevApp.Views
{
    public partial class ClientDashboardForm
    {
        private TouchZoom _zoom;

        private void InitTouch()
        {
            ScanSettings cfg = ScanSettings.Current;
            _zoom = new TouchZoom(CameraViewportGrid, ZoomLayer, cfg.PreviewZoomMin, cfg.PreviewZoomMax);
            _zoom.Changed += UpdateZoomUi;
            if (!cfg.PreviewZoomButtons) ZoomControls.Visibility = Visibility.Collapsed;      // gestures still work
            UpdateZoomUi();

            // the step indicator follows the instruction line, so every place that clears or sets it keeps the indicator in step
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(ViewStepTxt, (s, e) => RefreshStepper());
        }

        private void UpdateZoomUi()
        {
            if (_zoom == null) return;
            ZoomLevelTxt.Text = _zoom.Scale.ToString("0.0") + "×";
            ZoomInBtn.IsEnabled = _zoom.Scale < _zoom.MaxZoom - 0.001;
            ZoomOutBtn.IsEnabled = _zoom.Scale > _zoom.MinZoom + 0.001;
            ZoomResetBtn.IsEnabled = _zoom.IsZoomed;
        }

        private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => _zoom.ZoomIn();
        private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => _zoom.ZoomOut();
        private void ZoomResetBtn_Click(object sender, RoutedEventArgs e) => _zoom.Reset(true);

        /// <summary>Back to the original view without animation (a new scan always starts un-zoomed).</summary>
        private void ResetZoomNow() { if (_zoom != null) _zoom.Reset(false); }

        // ----------------------------------------------------------- camera and size --

        private FilterInfoCollection _boxDevices;
        private readonly System.Collections.Generic.List<VideoCapabilities> _boxModes = new System.Collections.Generic.List<VideoCapabilities>();
        private System.Drawing.Size? _chosenSize;           // null = the sharpest picture
        private bool _fillingBox;

        private void CentreChk_Changed(object sender, RoutedEventArgs e) { UpdateGuideLayout(); }

        private void InitCameraBox()
        {
            _fillingBox = true;
            try
            {
                try { _boxDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice); } catch { _boxDevices = null; }
                CameraCombo.Items.Clear();
                if (_boxDevices == null || _boxDevices.Count == 0)
                {
                    CameraCombo.Items.Add("No camera found");
                    CameraCombo.SelectedIndex = 0;
                    CameraCombo.IsEnabled = SizeCombo.IsEnabled = false;
                    return;
                }
                for (int i = 0; i < _boxDevices.Count; i++) CameraCombo.Items.Add(_boxDevices[i].Name);
                int pick = 0;
                try { pick = Math.Max(0, CameraDiscovery.PickIndex(_boxDevices)); } catch { }
                CameraCombo.SelectedIndex = Math.Min(pick, CameraCombo.Items.Count - 1);
                FillSizes();
            }
            finally { _fillingBox = false; }
        }

        private void FillSizes()
        {
            _boxModes.Clear();
            SizeCombo.Items.Clear();
            _chosenSize = null;
            VideoCapabilities[] caps = null;
            try
            {
                if (_boxDevices != null && CameraCombo.SelectedIndex >= 0 && CameraCombo.SelectedIndex < _boxDevices.Count)
                    caps = new VideoCaptureDevice(_boxDevices[CameraCombo.SelectedIndex].MonikerString).VideoCapabilities;
            }
            catch { }

            VideoCapabilities best = CameraDiscovery.BestMode(caps);
            SizeCombo.Items.Add(best == null ? "Sharpest available" : "Sharpest available (" + best.FrameSize.Width + " × " + best.FrameSize.Height + ")");
            if (caps != null)
                foreach (VideoCapabilities c in caps.GroupBy(x => x.FrameSize.Width + "x" + x.FrameSize.Height).Select(g => g.OrderByDescending(x => x.AverageFrameRate).First())
                                                    .OrderByDescending(x => x.FrameSize.Width * x.FrameSize.Height))
                {
                    _boxModes.Add(c);
                    SizeCombo.Items.Add(c.FrameSize.Width + " × " + c.FrameSize.Height);
                }
            SizeCombo.SelectedIndex = 0;
        }

        /// <summary>The camera chosen in the box; falls back to the automatic ranking.</summary>
        private int SelectedCameraIndex()
        {
            int i = CameraCombo.SelectedIndex;
            if (videoDevices != null && i >= 0 && i < videoDevices.Count && CameraCombo.Items.Count == videoDevices.Count) return i;
            return PickCameraIndex();
        }

        /// <summary>The size chosen in the box; otherwise the sharpest picture (Settings can turn that off).</summary>
        private VideoCapabilities ChooseMode(VideoCapabilities[] caps)
        {
            if (_chosenSize.HasValue)
            {
                VideoCapabilities pick = caps.Where(c => c.FrameSize == _chosenSize.Value).OrderByDescending(c => c.AverageFrameRate).FirstOrDefault();
                if (pick != null) return pick;
            }
            ScanSettings cfg = ScanSettings.Current;
            if (cfg.MaxResolution) return CameraDiscovery.BestMode(caps);
            return caps.OrderBy(c => Math.Abs(c.FrameSize.Width - cfg.CameraWidth) + Math.Abs(c.FrameSize.Height - cfg.CameraHeight)).First();
        }

        private void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingBox || _boxDevices == null || _boxDevices.Count == 0) return;
            _fillingBox = true;
            try { FillSizes(); } finally { _fillingBox = false; }
            if (_cameraLive) { _camTried = 0; OpenCamera(SelectedCameraIndex()); }       // switching camera while live reopens it
        }

        private void SizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingBox || SizeCombo.SelectedIndex < 0) return;
            _chosenSize = SizeCombo.SelectedIndex > 0 && SizeCombo.SelectedIndex - 1 < _boxModes.Count ? _boxModes[SizeCombo.SelectedIndex - 1].FrameSize : (System.Drawing.Size?)null;
            if (_cameraLive) { _camTried = 0; OpenCamera(SelectedCameraIndex()); }
        }

        // ------------------------------------------------------------- step indicator --

        private void RefreshStepper()
        {
            if (StepperGrid == null) return;
            StepperGrid.Children.Clear();
            if (_session == null) { StepperGrid.Visibility = Visibility.Collapsed; return; }

            int n = 0;
            foreach (ScanView v in ScanViews.Sequence)
            {
                n++;
                bool done = _session.Outcomes.Any(o => o.View == ScanViews.Name(v) && o.Ok);
                bool now = !done && !_session.IsComplete && _session.Current == v;

                string bg = done ? "#E3E4C4" : now ? "#6E6F52" : "#FFFFFF";
                string line = done ? "#A9AA7E" : now ? "#6E6F52" : "#DCD4C1";
                string ink = done ? "#5B5C42" : now ? "#FFFFFF" : "#8D8D76";
                string mark = done ? "✓" : n.ToString();

                var disc = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 0),
                    Background = Ui.Brush(done ? "#6E6F52" : now ? "#FFFFFF" : "#EDE8DA"),
                    Child = new TextBlock
                    {
                        Text = mark, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Ui.Brush(done ? "#FFFFFF" : now ? "#6E6F52" : "#8D8D76")
                    }
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(disc);
                row.Children.Add(new TextBlock { Text = ScanViews.Name(v), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(ink), VerticalAlignment = VerticalAlignment.Center });

                StepperGrid.Children.Add(new Border
                {
                    Background = Ui.Brush(bg), BorderBrush = Ui.Brush(line), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14),
                    MinHeight = 54, Margin = new Thickness(0, 0, v == ScanViews.Sequence[ScanViews.Sequence.Length - 1] ? 0 : 8, 0), Child = row
                });
            }
            StepperGrid.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ layout --

        /// <summary>Portrait monitors: a large camera picture on top, the controls underneath. Landscape: side by side.</summary>
        private void ApplyAdaptiveLayout(Size size)
        {
            if (ContentGrid == null) return;

            ContentGrid.ColumnDefinitions.Clear();
            ContentGrid.RowDefinitions.Clear();

            bool portrait = size.Height > size.Width * 1.05;
            bool wide = size.Width > 850 && !portrait;

            if (wide)
            {
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ImageCard.ClearValue(FrameworkElement.HeightProperty);
                Grid.SetRow(ImageCard, 0); Grid.SetColumn(ImageCard, 0);
                Grid.SetRow(ControlsCard, 0); Grid.SetColumn(ControlsCard, 1);
                return;
            }

            if (portrait)
            {
                // the picture is 4:3 landscape: give it the full width and the height that fits, capped so the controls keep room
                double h = Math.Max(340, Math.Min(size.Width * 0.78, size.Height * 0.52));
                ImageCard.Height = h;
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            else
            {
                ImageCard.Height = 320;
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(ImageCard, 0); Grid.SetColumn(ImageCard, 0);
            Grid.SetRow(ControlsCard, 1); Grid.SetColumn(ControlsCard, 0);
        }
    }
}
