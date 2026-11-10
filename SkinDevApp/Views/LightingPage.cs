// ============================================================================
// LightingPage.cs  -  namespace SkinDevApp.Views
//
// "Lighting": the connection to the ESP32 light controller (USB, COM port) and the controls of the soft lights and the ring light.
// Brightness changes are sent a moment after the slider stops. The ring light normally comes on only while images are captured; the
// "Test ring light" button lights it by hand for a short check and it switches itself off again after one minute or when this page closes.
// The percentages are the controller's settings: LUMYVUE cannot measure the real brightness of the lights.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkinDevApp.Data;
using SkinDevApp.Hardware;

namespace SkinDevApp.Views
{
    public sealed class LightingPage : Page
    {
        private const string AutoItem = "Automatic (find the controller)";

        private readonly TextBlock _status = Ui.Text("Looking for the light controller…", 13, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _statusNote = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 4, 0, 0));
        private readonly ComboBox _ports = new ComboBox { Style = (Style)Application.Current.FindResource("Combo"), Height = 40, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Button _ringTest = new Button { Height = 52, Margin = new Thickness(0, 12, 0, 0) };
        private readonly Slider _ring = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly Slider _temp = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly Button _softPower = new Button { Height = 52, Margin = new Thickness(0, 12, 0, 0) };
        private readonly Slider _soft = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        private readonly TextBlock _ringVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _tempVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _softVal = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
        private readonly TextBlock _ringState = Ui.Text("", 12.5, true, Ui.Muted, new Thickness(0));
        private readonly TextBlock _softState = Ui.Text("", 12.5, true, Ui.Muted, new Thickness(0));
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _testOff = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        private bool _loading, _ringLit, _softLit, _ringChanged, _tempChanged, _softChanged;

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

            // ---- connection
            var conn = new StackPanel();
            var top = new DockPanel();
            Button retry = Ui.Btn("Try again", (s, e) => Reconnect(), "SmallButton");
            DockPanel.SetDock(retry, Dock.Right);
            top.Children.Add(retry);
            var text = new StackPanel();
            text.Children.Add(_status);
            text.Children.Add(_statusNote);
            top.Children.Add(text);
            conn.Children.Add(top);
            conn.Children.Add(Ui.Text("Controller port (USB)", 12, false, Ui.Muted, new Thickness(0, 14, 0, 4)));
            var portRow = new StackPanel { Orientation = Orientation.Horizontal };
            portRow.Children.Add(_ports);
            Button refreshPorts = Ui.Btn("Refresh list", (s, e) => FillPorts(), "SmallButton");
            refreshPorts.Margin = new Thickness(8, 0, 0, 0);
            portRow.Children.Add(refreshPorts);
            conn.Children.Add(portRow);
            conn.Children.Add(Ui.Text("Automatic is best: LUMYVUE asks every USB serial port until the light controller answers. Choose a port only if that fails.", 11.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            body.Children.Add(Ui.Card(conn, new Thickness(0, 0, 0, 12)));

            // ---- soft lights
            var softCard = new StackPanel();
            softCard.Children.Add(Ui.Text("Soft lights", 17, true, Ui.Ink, new Thickness(0)));
            softCard.Children.Add(Ui.Text("The soft lights inside the housing. They fade in by themselves when the Mini PC starts and stay on during the scan.", 12.5, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            softCard.Children.Add(_softPower);
            softCard.Children.Add(Labelled("Brightness", _softVal, _softState));
            softCard.Children.Add(_soft);
            body.Children.Add(Ui.Card(softCard, new Thickness(0, 0, 0, 12)));

            // ---- ring light
            var ring = new StackPanel();
            ring.Children.Add(Ui.Text("Ring light", 17, true, Ui.Ink, new Thickness(0)));
            ring.Children.Add(Ui.Text("The main light around the camera. It comes on by itself when image capture starts and goes off when each capture ends.", 12.5, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            ring.Children.Add(_ringTest);
            ring.Children.Add(Labelled("Brightness", _ringVal, _ringState));
            ring.Children.Add(_ring);
            ring.Children.Add(Labelled("Color", _tempVal, null));
            ring.Children.Add(_temp);
            var ends = new DockPanel();
            var cool = Ui.Text("Cool", 11.5, false, Ui.Muted, new Thickness(0));
            DockPanel.SetDock(cool, Dock.Right);
            ends.Children.Add(cool);
            ends.Children.Add(Ui.Text("Warm", 11.5, false, Ui.Muted, new Thickness(0)));
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

            // ---- automatic use and notes
            var auto = new StackPanel();
            var autoChk = new CheckBox { Content = "Turn the ring light on when image capture starts, and off when each capture ends", FontSize = 13, IsChecked = LightController.AutoDuringScan };
            autoChk.Click += (s, e) => LightController.AutoDuringScan = autoChk.IsChecked == true;
            auto.Children.Add(autoChk);
            auto.Children.Add(Ui.Text("The percentages are the settings the controller accepted. LUMYVUE cannot measure the real brightness of the lights.", 11.5, false, Ui.Muted, new Thickness(0, 10, 0, 0)));
            body.Children.Add(Ui.Card(auto, new Thickness(0)));

            // ---- behaviour
            _ports.SelectionChanged += (s, e) =>
            {
                if (_loading || _ports.SelectedItem == null) return;
                string pick = (string)_ports.SelectedItem;
                LightController.PreferredPort = pick == AutoItem ? "" : pick;
                Reconnect();
            };
            _softPower.Click += (s, e) => { _softLit = !_softLit; if (_softLit && _soft.Value < 1) _soft.Value = 100; _softChanged = true; ShowState(); Send(); };
            _ringTest.Click += (s, e) => ToggleRingTest();
            _ring.ValueChanged += (s, e) => { if (_loading) return; _ringChanged = true; ShowState(); Send(); };
            _temp.ValueChanged += (s, e) => { if (_loading) return; _tempChanged = true; ShowState(); Send(); };
            _soft.ValueChanged += (s, e) => { if (_loading) return; if (_soft.Value > 0) _softLit = true; _softChanged = true; ShowState(); Send(); };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Push(); };
            _testOff.Tick += (s, e) => { _testOff.Stop(); if (_ringLit) ToggleRingTest(); };

            SetEnabled(false);
            ShowState();
            FillPorts();
            Action changed = () => Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) Refresh(); }));
            Loaded += (s, e) => { LightController.ConnectionChanged += changed; Refresh(); };
            Unloaded += (s, e) =>
            {
                LightController.ConnectionChanged -= changed;
                _debounce.Stop(); _testOff.Stop();
                if (_ringLit) { _ringLit = false; var ignore = LightController.SetAsync(on: false); }     // a hand test never outlives the page
            };
        }

        private static UIElement Labelled(string label, TextBlock value, TextBlock state)
        {
            var d = new DockPanel { Margin = new Thickness(0, 16, 0, 2) };
            DockPanel.SetDock(value, Dock.Right);
            d.Children.Add(value);
            if (state != null) { state.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(state, Dock.Left); }
            d.Children.Add(Ui.Text(label, 13, false, Ui.Muted, new Thickness(0)));
            if (state != null) d.Children.Add(state);
            return d;
        }

        private void FillPorts()
        {
            _loading = true;
            var items = new List<string> { AutoItem };
            foreach (string p in ESP32Controller.AvailablePorts()) items.Add(p);
            string keep = LightController.PreferredPort;
            if (!string.IsNullOrEmpty(keep) && !items.Contains(keep, StringComparer.OrdinalIgnoreCase)) items.Add(keep);
            _ports.ItemsSource = items;
            _ports.SelectedItem = string.IsNullOrEmpty(keep) ? AutoItem : items.FirstOrDefault(i => string.Equals(i, keep, StringComparison.OrdinalIgnoreCase));
            _loading = false;
        }

        private void SetEnabled(bool on)
        {
            foreach (UIElement c in new UIElement[] { _ringTest, _ring, _temp, _softPower, _soft }) c.IsEnabled = on;
        }

        private async void Reconnect()
        {
            LightController.PreferredPort = LightController.PreferredPort;      // drops the link; the monitor opens it again
            _status.Text = "Looking for the light controller…";
            _status.Foreground = Ui.Ink;
            _statusNote.Text = "";
            await System.Threading.Tasks.Task.Delay(300);
            Refresh();
        }

        private async void Refresh()
        {
            FillPorts();
            LightState s = await LightController.GetAsync();
            if (s == null)
            {
                SetEnabled(false);
                _status.Text = "Light controller not connected";
                _status.Foreground = Ui.Bad;
                var ports = ESP32Controller.AvailablePorts();
                _statusNote.Text = !string.IsNullOrEmpty(LightController.LastError) ? LightController.LastError
                                   : (ports.Count == 0 ? "No USB serial port was found. Check that the controller's USB cable is plugged in." : "Ports found: " + string.Join(", ", ports) + ".");
                return;
            }
            _status.Text = "Connected to the light controller";
            _status.Foreground = Ui.Good;
            _statusNote.Text = s.Host + (string.IsNullOrEmpty(s.Firmware) ? "" : "  ·  firmware " + s.Firmware);
            _loading = true;
            _ringLit = s.On; _softLit = s.CobOn;
            _ring.Value = s.Brightness; _temp.Value = s.Temperature; _soft.Value = s.CobBrightness;
            _loading = false;
            SetEnabled(true);
            ShowState();
        }

        private void ShowState()
        {
            _softPower.Style = (Style)FindResource(_softLit ? "PrimaryButton" : "SecondaryButton");
            _softPower.Content = _softLit ? "Soft lights are on  ·  tap to turn off" : "Soft lights are off  ·  tap to turn on";
            _ringTest.Style = (Style)FindResource(_ringLit ? "PrimaryButton" : "SecondaryButton");
            _ringTest.Content = _ringLit ? "Ring light is on (test)  ·  tap to turn off" : "Test ring light  ·  tap to turn on for a check";
            _softVal.Text = (int)_soft.Value + "%";
            _ringVal.Text = (int)_ring.Value + "%";
            int t = (int)_temp.Value;
            _tempVal.Text = t < 34 ? "Warm" : (t > 66 ? "Cool" : "Neutral");
            _softState.Text = _softLit ? "ON" : "OFF";
            _ringState.Text = _ringLit ? "ON" : "OFF";
        }

        private async void ToggleRingTest()
        {
            _ringLit = !_ringLit;
            ShowState();
            _testOff.Stop();
            if (_ringLit) _testOff.Start();
            LightState s = await LightController.SetAsync(on: _ringLit);
            if (s == null) { _status.Text = "Lost connection to the light controller"; _status.Foreground = Ui.Bad; }
        }

        private void Send() { _debounce.Stop(); _debounce.Start(); }

        private async void Push()
        {
            bool ring = _ringChanged, temp = _tempChanged, soft = _softChanged;
            _ringChanged = _tempChanged = _softChanged = false;
            LightState s = await LightController.SetAsync(
                brightness: ring ? (int?)_ring.Value : null,
                temperature: temp ? (int?)_temp.Value : null,
                cob: soft ? (bool?)_softLit : null,
                cobBrightness: soft ? (int?)_soft.Value : null);
            if (s == null)
            {
                _status.Text = "Lost connection to the light controller";
                _status.Foreground = Ui.Bad;
                _statusNote.Text = "LUMYVUE reconnects by itself when the controller is back. Press Try again to hurry it up.";
            }
        }
    }
}
