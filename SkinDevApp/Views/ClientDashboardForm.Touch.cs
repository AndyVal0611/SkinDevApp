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

                string bg = done ? "#E6EBDD" : now ? "#5F6652" : "#FFFFFF";
                string line = done ? "#A9B58F" : now ? "#5F6652" : "#D8D3C0";
                string ink = done ? "#3E5B2E" : now ? "#FFFFFF" : "#8A8576";
                string mark = done ? "✓" : n.ToString();

                var disc = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 8, 0),
                    Background = Ui.Brush(done ? "#5F6652" : now ? "#FFFFFF" : "#EFEAE0"),
                    Child = new TextBlock
                    {
                        Text = mark, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Ui.Brush(done ? "#FFFFFF" : now ? "#5F6652" : "#8A8576")
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
