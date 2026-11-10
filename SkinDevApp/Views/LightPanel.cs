// ============================================================================
// LightPanel.cs  -  namespace SkinDevApp.Views
//
// A compact "Lighting" panel for the scan screen: two independent brightness sliders, one for the soft lights and one for the main ring
// light, so the light can be adjusted while the images are being taken. The ring light itself is switched on when the camera starts and off
// when it stops (see LightController); the slider only sets how bright it is.
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
        private readonly TextBlock _note = Ui.Text("Looking for the light controller…", 11.5, false, Ui.Muted, new Thickness(0, 8, 0, 0));
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        private bool _loading, _softChanged, _ringChanged;

        public LightPanel()
        {
            Style = (Style)Application.Current.FindResource("Panel");
            Margin = new Thickness(0, 0, 0, 10);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Lighting", 14, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Row("Soft lights", _soft, _softVal));
            sp.Children.Add(Row("Ring light", _ring, _ringVal));
            sp.Children.Add(_note);
            Child = sp;

            _soft.ValueChanged += (s, e) => { if (_loading) return; _softChanged = true; Show(); _debounce.Stop(); _debounce.Start(); };
            _ring.ValueChanged += (s, e) => { if (_loading) return; _ringChanged = true; Show(); _debounce.Stop(); _debounce.Start(); };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Push(); };
            _soft.IsEnabled = _ring.IsEnabled = false;
            Loaded += (s, e) => Refresh();
            Unloaded += (s, e) => _debounce.Stop();
            IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(); };
        }

        private static UIElement Row(string label, Slider slider, TextBlock value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var head = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            head.Children.Add(value);
            head.Children.Add(Ui.Text(label, 12.5, false, Ui.Muted, new Thickness(0)));
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
                _note.Text = "Light controller not connected. Open Lighting in the menu for details.";
                return;
            }
            _loading = true;
            _soft.Value = s.CobOn ? s.CobBrightness : 0;
            _ring.Value = s.Brightness;
            _loading = false;
            _soft.IsEnabled = _ring.IsEnabled = true;
            _note.Text = "The ring light comes on when the camera starts.";
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
                cobBrightness: soft && _soft.Value > 0 ? (int?)_soft.Value : null);
            if (s == null) _note.Text = "Lost connection to the light controller.";
        }
    }
}
