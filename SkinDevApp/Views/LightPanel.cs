// ============================================================================
// LightPanel.cs  -  namespace SkinDevApp.Views
//
// A compact "Lighting" panel for the scan screen: two independent brightness sliders, one for the soft lights and one for the main ring
// light, so both can be adjusted while the images are being taken (also between the front, left and right images). It also shows whether the
// controller is connected and whether each light is ON or OFF. The ring light itself is switched by the capture process (on when a capture
// starts, off when it ends); the ring slider only sets how bright it is. The percentages are the controller's settings, not measured light.
// ============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class LightPanel : Border
    {
        private readonly Slider _soft = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly Slider _ring = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly TextBlock _softVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _ringVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _softState = Ui.Text("", 11.5, true, Ui.Muted, new Thickness(10, 0, 0, 0));
        private readonly TextBlock _ringState = Ui.Text("", 11.5, true, Ui.Muted, new Thickness(10, 0, 0, 0));
        private readonly TextBlock _link = Ui.Text("Looking for the light controller…", 11.5, false, Ui.Muted, new Thickness(0, 8, 0, 0));
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        private readonly DispatcherTimer _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private bool _loading, _softChanged, _ringChanged;

        public LightPanel()
        {
            Style = (Style)Application.Current.FindResource("Panel");
            Margin = new Thickness(0, 0, 0, 10);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Lighting", 14, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Row("Soft lights", _soft, _softVal, _softState));
            sp.Children.Add(Row("Ring light", _ring, _ringVal, _ringState));
            sp.Children.Add(_link);
            Child = sp;

            _soft.ValueChanged += (s, e) => { if (_loading) return; _softChanged = true; Show(); _debounce.Stop(); _debounce.Start(); };
            _ring.ValueChanged += (s, e) => { if (_loading) return; _ringChanged = true; Show(); _debounce.Stop(); _debounce.Start(); };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Push(); };
            _poll.Tick += (s, e) => { if (!_softChanged && !_ringChanged && !_debounce.IsEnabled) Refresh(); };
            _soft.IsEnabled = _ring.IsEnabled = false;
            Loaded += (s, e) => { Refresh(); _poll.Start(); };
            Unloaded += (s, e) => { _debounce.Stop(); _poll.Stop(); };
        }

        private static UIElement Row(string label, Slider slider, TextBlock value, TextBlock state)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var head = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            head.Children.Add(value);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Ui.Text(label, 12.5, false, Ui.Muted, new Thickness(0)));
            left.Children.Add(state);
            head.Children.Add(left);
            sp.Children.Add(head);
            sp.Children.Add(slider);
            return sp;
        }

        private async void Refresh()
        {
            LightState s = await LightController.GetAsync();
            if (s == null)
            {
                _soft.IsEnabled = _ring.IsEnabled = false;
                _softState.Text = _ringState.Text = "";
                _link.Text = "Light controller not connected. Open Lighting in the menu for details.";
                return;
            }
            _loading = true;
            _soft.Value = s.CobBrightness;
            _ring.Value = s.Brightness;
            _loading = false;
            _soft.IsEnabled = _ring.IsEnabled = true;
            _softState.Text = s.CobOn ? "ON" : "OFF";
            _ringState.Text = s.On ? "ON" : "OFF";
            _link.Text = "Connected on " + s.Host + ". The ring light comes on when a capture starts.";
            Show();
        }

        private void Show()
        {
            _softVal.Text = (int)_soft.Value + "%";
            _ringVal.Text = (int)_ring.Value + "%";
        }

        private async void Push()
        {
            bool soft = _softChanged, ring = _ringChanged;
            _softChanged = _ringChanged = false;
            LightState s = await LightController.SetAsync(
                brightness: ring ? (int?)_ring.Value : null,
                cob: soft ? (bool?)(_soft.Value > 0) : null,
                cobBrightness: soft ? (int?)_soft.Value : null);
            if (s == null) { _link.Text = "Lost connection to the light controller. It reconnects by itself."; return; }
            _softState.Text = s.CobOn ? "ON" : "OFF";
            _ringState.Text = s.On ? "ON" : "OFF";
        }
    }
}
