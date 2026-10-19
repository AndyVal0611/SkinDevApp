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

            // the stages are shown (as "waiting") whenever the three-photograph scan is switched on
            MultiViewChk.Checked += (s, e) => RefreshStepper();
            MultiViewChk.Unchecked += (s, e) => RefreshStepper();
            RefreshStepper();
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
            // before a scan starts the three stages are still shown (all waiting), so the workflow is clear at a glance
            if (_session == null && MultiViewChk.IsChecked != true) { StepperGrid.Visibility = Visibility.Collapsed; return; }

            string[] captions = { "Straight on", "Left cheek", "Right cheek" };
            int n = 0;
            foreach (ScanView v in ScanViews.Sequence)
            {
                n++;
                bool done = _session != null && _session.Outcomes.Any(o => o.View == ScanViews.Name(v) && o.Ok);
                bool now = _session != null && !done && !_session.IsComplete && _session.Current == v;

                Brush bg = now ? (Brush)FindResource("HeaderGradient") : Ui.Brush(done ? "#E7E8CF" : "#FFFEFB");
                string line = done ? "#C2C395" : now ? "#5F6048" : "#E3DDCB";
                string ink = done ? "#4F503B" : now ? "#FFFFFF" : "#8D8D76";

                var disc = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 0),
                    Background = Ui.Brush(done ? "#818263" : now ? "#FFFFFF" : "#EFEADC")
                };
                if (done) disc.Child = new Grid { Children = { Ui.Icon("Icon.Check", 14, Brushes.White) } };
                else disc.Child = new TextBlock
                {
                    Text = n.ToString(), FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Ui.Brush(now ? "#5F6048" : "#8D8D76")
                };

                var top = new StackPanel { Orientation = Orientation.Horizontal };
                top.Children.Add(disc);
                top.Children.Add(new TextBlock { Text = ScanViews.Name(v), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(ink), VerticalAlignment = VerticalAlignment.Center });
                var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                body.Children.Add(top);
                var caption = new TextBlock
                {
                    Text = done ? "Captured" : now ? "Now: " + captions[n - 1].ToLowerInvariant() : captions[n - 1],
                    FontSize = 11, Foreground = Ui.Brush(now ? "#E7E6CF" : done ? "#6F6F58" : "#9B9B84"), Margin = new Thickness(34, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                body.Children.Add(caption);

                var pill = new Border
                {
                    Background = bg, BorderBrush = Ui.Brush(line), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(16),
                    MinHeight = 64, Padding = new Thickness(10, 8, 8, 8),
                    Margin = new Thickness(0, 0, v == ScanViews.Sequence[ScanViews.Sequence.Length - 1] ? 0 : 8, 0), Child = body
                };
                // narrow pills keep just the number and the view name (the instruction is shown below them anyway)
                pill.SizeChanged += (s, e) => caption.Visibility = pill.ActualWidth < 132 ? Visibility.Collapsed : Visibility.Visible;
                StepperGrid.Children.Add(pill);
            }
            StepperGrid.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ layout --

        /// <summary>
        /// Landscape (16:9 workstation): camera | capture | AI analysis, all visible at once.
        /// Narrower landscape windows: camera | capture above analysis.  Portrait: camera on top, the two panels below.
        /// </summary>
        private void ApplyAdaptiveLayout(Size size)
        {
            if (ContentGrid == null) return;

            ContentGrid.ColumnDefinitions.Clear();
            ContentGrid.RowDefinitions.Clear();
            foreach (UIElement el in new UIElement[] { ImageCard, ControlsCard, AnalysisCard })
            {
                Grid.SetRowSpan(el, 1); Grid.SetColumnSpan(el, 1);
            }
            ImageCard.ClearValue(FrameworkElement.HeightProperty);

            bool portrait = size.Height > size.Width * 1.05;

            if (!portrait && size.Width >= 1100)
            {
                // wide screens give the camera more room; on a laptop-sized window the two panels keep a readable width
                double cam = size.Width >= 1450 ? 1.65 : 1.25;
                double min = size.Width >= 1450 ? 330 : 290;
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cam, GridUnitType.Star) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = min });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = min });
                Grid.SetRow(ImageCard, 0); Grid.SetColumn(ImageCard, 0);
                Grid.SetRow(ControlsCard, 0); Grid.SetColumn(ControlsCard, 1);
                Grid.SetRow(AnalysisCard, 0); Grid.SetColumn(AnalysisCard, 2);
                return;
            }

            if (!portrait && size.Width > 850)
            {
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(ImageCard, 0); Grid.SetColumn(ImageCard, 0); Grid.SetRowSpan(ImageCard, 2);
                Grid.SetRow(ControlsCard, 0); Grid.SetColumn(ControlsCard, 1);
                Grid.SetRow(AnalysisCard, 1); Grid.SetColumn(AnalysisCard, 1);
                return;
            }

            // portrait / narrow: the picture is landscape, so give it the full width and the height that fits
            ImageCard.Height = portrait ? Math.Max(340, Math.Min(size.Width * 0.78, size.Height * 0.48)) : 320;
            ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(ImageCard, 0); Grid.SetColumn(ImageCard, 0);
            Grid.SetRow(ControlsCard, 1); Grid.SetColumn(ControlsCard, 0);
            Grid.SetRow(AnalysisCard, 2); Grid.SetColumn(AnalysisCard, 0);
        }

    }
}
