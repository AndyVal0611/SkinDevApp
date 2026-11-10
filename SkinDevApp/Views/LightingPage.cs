// ============================================================================
// LightingPage.cs  -  namespace SkinDevApp.Views
//
// "Lighting": the controls of the ring light and the COB lights, the same ones as the controller's own web page (ringlight.ino):
// power, brightness, warm / cool colour, and the COB lights. Changes are sent to the controller over WiFi a moment after the slider stops.
// ============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class LightingPage : Page
    {
        private readonly TextBlock _status = Ui.Text("Looking for the light controller…", 13, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _statusNote = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 4, 0, 0));
        private readonly Button _power = new Button { Height = 56, Margin = new Thickness(0, 12, 0, 0) };
        private readonly Slider _bright = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly Slider _temp = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly Button _cobPower = new Button { Height = 56, Margin = new Thickness(0, 12, 0, 0) };
        private readonly Slider _cob = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly TextBlock _brightVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _tempVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _cobVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly StackPanel _controls = new StackPanel();
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private bool _loading, _on, _cobOn;
        private TextBox _addrBox;

        public LightingPage()
        {
            Title = "Lighting";
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Ui.Header("Lighting"));
            var body = new StackPanel { Margin = new Thickness(28, 18, 28, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;

            // connection
            var conn = new DockPanel();
            Button retry = Ui.Btn("Try again", (s, e) => Refresh(), "SmallButton");
            DockPanel.SetDock(retry, Dock.Right);
            conn.Children.Add(retry);
            var connText = new StackPanel();
            connText.Children.Add(_status);
            connText.Children.Add(_statusNote);
            conn.Children.Add(connText);
            body.Children.Add(Ui.Card(conn, new Thickness(0, 0, 0, 12)));

            // ring light
            var ring = new StackPanel();
            ring.Children.Add(Ui.Text("Ring light", 17, true, Ui.Ink, new Thickness(0)));
            ring.Children.Add(Ui.Text("The main light around the camera. It comes on by itself when image capture starts.", 12.5, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            ring.Children.Add(_power);
            ring.Children.Add(Labelled("Brightness", _brightVal));
            ring.Children.Add(_bright);
            ring.Children.Add(Labelled("Color", _tempVal));
            ring.Children.Add(_temp);
            var ends = new DockPanel();
            ends.Children.Add(Ui.Text("Warm", 11.5, false, Ui.Muted, new Thickness(0)));
            var cool = Ui.Text("Cool", 11.5, false, Ui.Muted, new Thickness(0));
            DockPanel.SetDock(cool, Dock.Right);
            ends.Children.Insert(0, cool);
            ring.Children.Add(ends);
            var presets = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var p in new[] { Tuple.Create("Warm", 0), Tuple.Create("Neutral", 50), Tuple.Create("Cool", 100) })
            {
                int v = p.Item2;
                Button b = Ui.Btn(p.Item1, (s, e) => { _temp.Value = v; }, "SecondaryButton");
                b.Margin = new Thickness(0, 0, 8, 0);
                presets.Children.Add(b);
            }
            ring.Children.Add(presets);
            body.Children.Add(Ui.Card(ring, new Thickness(0, 0, 0, 12)));

            // COB lights
            var cobCard = new StackPanel();
            cobCard.Children.Add(Ui.Text("Soft lights", 17, true, Ui.Ink, new Thickness(0)));
            cobCard.Children.Add(Ui.Text("The soft lights inside the housing. They fade in when the Mini PC starts.", 12.5, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            cobCard.Children.Add(_cobPower);
            cobCard.Children.Add(Labelled("Brightness", _cobVal));
            cobCard.Children.Add(_cob);
            body.Children.Add(Ui.Card(cobCard, new Thickness(0, 0, 0, 12)));

            // automatic use and address
            var auto = new StackPanel();
            var autoChk = new CheckBox { Content = "Turn the ring light on when image capture starts, and off when it ends", FontSize = 13, IsChecked = LightController.AutoDuringScan };
            autoChk.Click += (s, e) => LightController.AutoDuringScan = autoChk.IsChecked == true;
            auto.Children.Add(autoChk);
            auto.Children.Add(Ui.Text("Light controller address", 12, false, Ui.Muted, new Thickness(0, 14, 0, 4)));
            var addr = new DockPanel();
            _addrBox = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Height = 40, Width = 260, Text = LightController.Host, HorizontalAlignment = HorizontalAlignment.Left };
            Button save = Ui.Btn("Save", (s, e) => { LightController.Host = _addrBox.Text; _addrBox.Text = LightController.Host; Refresh(); }, "SmallButton");
            save.Margin = new Thickness(8, 0, 0, 0);
            var addrRow = new StackPanel { Orientation = Orientation.Horizontal };
            addrRow.Children.Add(_addrBox);
            addrRow.Children.Add(save);
            auto.Children.Add(addrRow);
            auto.Children.Add(Ui.Text("Usually ringlight.local. If the controller cannot join the WiFi it starts its own hotspot \"RingLight\" (address 192.168.4.1).", 11.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            body.Children.Add(Ui.Card(auto, new Thickness(0)));

            _power.Click += (s, e) => { _on = !_on; ShowState(); Send(); };
            _cobPower.Click += (s, e) => { _cobOn = !_cobOn; ShowState(); Send(); };
            _bright.ValueChanged += (s, e) => { if (_loading) return; if (_bright.Value > 0) _on = true; ShowState(); Send(); };
            _temp.ValueChanged += (s, e) => { if (_loading) return; ShowState(); Send(); };
            _cob.ValueChanged += (s, e) => { if (_loading) return; if (_cob.Value > 0) _cobOn = true; ShowState(); Send(); };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Push(); };

            SetEnabled(false);
            Loaded += (s, e) => Refresh();
            Unloaded += (s, e) => _debounce.Stop();
        }

        private static UIElement Labelled(string label, TextBlock value)
        {
            var d = new DockPanel { Margin = new Thickness(0, 16, 0, 2) };
            DockPanel.SetDock(value, Dock.Right);
            d.Children.Add(value);
            d.Children.Add(Ui.Text(label, 13, false, Ui.Muted, new Thickness(0)));
            return d;
        }

        private void SetEnabled(bool on)
        {
            foreach (UIElement c in new UIElement[] { _power, _bright, _temp, _cobPower, _cob }) c.IsEnabled = on;
        }

        private async void Refresh()
        {
            _status.Text = "Looking for the light controller…";
            _status.Foreground = Ui.Ink;
            _statusNote.Text = "";
            LightState s = await LightController.GetAsync();
            if (s == null)
            {
                _status.Text = "Searching the WiFi network for the light controller…";
                _statusNote.Text = "This takes a few seconds.";
                s = await LightController.FindOnNetworkAsync();
                if (s != null && _addrBox != null) _addrBox.Text = LightController.Host;
            }
            if (s == null)
            {
                SetEnabled(false);
                _status.Text = "Light controller not connected";
                _status.Foreground = Ui.Bad;
                _statusNote.Text = "Check that the controller is powered and on the same WiFi as this computer, then press Try again.  (" + LightController.LastReport + ")";
                return;
            }
            _status.Text = "Connected to the light controller";
            _status.Foreground = Ui.Good;
            _statusNote.Text = s.Host;
            _loading = true;
            _on = s.On; _cobOn = s.CobOn;
            _bright.Value = s.Brightness; _temp.Value = s.Temperature; _cob.Value = s.CobBrightness;
            _loading = false;
            SetEnabled(true);
            ShowState();
        }

        private void ShowState()
        {
            _power.Style = (Style)FindResource(_on ? "PrimaryButton" : "SecondaryButton");
            _power.Content = _on ? "Ring light is on  ·  tap to turn off" : "Ring light is off  ·  tap to turn on";
            _cobPower.Style = (Style)FindResource(_cobOn ? "PrimaryButton" : "SecondaryButton");
            _cobPower.Content = _cobOn ? "Soft lights are on  ·  tap to turn off" : "Soft lights are off  ·  tap to turn on";
            _brightVal.Text = (int)_bright.Value + "%";
            int t = (int)_temp.Value;
            _tempVal.Text = t < 34 ? "Warm" : (t > 66 ? "Cool" : "Neutral");
            _cobVal.Text = (int)_cob.Value + "%";
        }

        private void Send() { _debounce.Stop(); _debounce.Start(); }

        private async void Push()
        {
            LightState s = await LightController.SetAsync(_on, (int)_bright.Value, (int)_temp.Value, _cobOn, (int)_cob.Value);
            if (s == null)
            {
                _status.Text = "Lost connection to the light controller";
                _status.Foreground = Ui.Bad;
                _statusNote.Text = "Press Try again.";
            }
        }
    }
}
