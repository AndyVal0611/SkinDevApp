// SettingsPage.xaml.cs - camera and capture preferences, the thermal receipt printer, accounts, and the change history.
// Layout follows a "settings" pattern: tabs on top, then rows with the name and a short explanation on the left and the control on the right.
// The image-analysis, heatmap and lesion-marking values are fixed for the study (see ScanSettings) and are not shown here.
// Saved to SystemSettings; StudyRepository.SaveSettings audits every changed value.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AForge.Video.DirectShow;
using SkinDevApp.Data;
using SkinDevApp.Printing;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class SettingsPage : Page
    {
        private const string AutoCamera = "Automatic (prefers the Logitech BRIO)";

        // friendly names for the settings audit log
        private static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>
        {
            { "camera.name", "Camera" }, { "camera.width", "Camera width" }, { "camera.height", "Camera height" },
            { "capture.auto", "Automatic capture" }, { "capture.review_before_finish", "Review images before results" },
            { "capture.stable_frames", "Steady frames needed" }, { "capture.hold_ms", "Hold-still time" },
            { "capture.cooldown_s", "Pause between images" }, { "capture.min_sharpness", "Minimum sharpness" },
            { "capture.min_brightness", "Minimum brightness" }, { "capture.max_brightness", "Maximum brightness" },
            { "pose.front_max_yaw", "Front view: largest head turn" }, { "pose.side_min_yaw", "Side views: smallest head turn" },
            { "printer.enabled", "Receipt printer" }, { "printer.simulation", "Printer simulation mode" }, { "printer.port", "Printer COM port" },
            { "printer.baud", "Printer baud rate" }, { "printer.data_bits", "Printer data bits" }, { "printer.parity", "Printer parity" },
            { "printer.stop_bits", "Printer stop bits" }, { "printer.handshake", "Printer flow control" }, { "printer.chars_per_line", "Receipt characters per line" },
            { "printer.styles", "Receipt bold and large text" }, { "printer.reset", "Printer reset before each receipt" }, { "printer.feed_lines", "Paper feed after a receipt" },
            { "printer.line_delay_ms", "Pause after each printed line" }, { "printer.final_default", "Final receipt by default" },
            { "printer.show_scores", "AI scores on receipts" }, { "printer.show_assessment", "Dermatologist assessment on receipts" }
        };

        private readonly Dictionary<string, TextBox> _num = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, CheckBox> _bool = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, string> _label = new Dictionary<string, string>();
        private readonly List<Tuple<string, UIElement, bool>> _tabs = new List<Tuple<string, UIElement, bool>>();   // title, content, shows Save bar
        private ComboBox _camera;
        private StackPanel _historyHost;

        public SettingsPage()
        {
            InitializeComponent();
            HeaderHost.Content = Ui.Header("Settings");
            if (!AppSession.IsResearcher)
            {
                ContentHost.Content = Ui.Card(Ui.Text("Please sign in to open the settings.", 14, false, Ui.Bad));
                FooterBar.Visibility = Visibility.Collapsed;
                return;
            }

            _tabs.Add(Tuple.Create("Camera & capture", BuildScanTab(), true));
            _tabs.Add(Tuple.Create("Receipt printer", BuildPrinterTab(), false));      // saved with its own button: never mixed with the camera settings
            _tabs.Add(Tuple.Create("Accounts", BuildAccountsTab(), false));
            _tabs.Add(Tuple.Create("History", BuildHistoryTab(), false));

            Fill(ScanSettings.Load());
            ShowTab(0);
        }

        // ------------------------------------------------------------------ tabs --

        private void ShowTab(int index)
        {
            TabStrip.Children.Clear();
            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                bool on = i == index;
                var label = new TextBlock
                {
                    Text = _tabs[i].Item1,
                    FontSize = 14,
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = on ? Ui.Ink : Ui.Muted,
                    Margin = new Thickness(2, 6, 2, 10)
                };
                var tab = new Border
                {
                    Child = label,
                    Margin = new Thickness(0, 0, 28, -1),
                    BorderBrush = on ? Ui.Brush("#75765A") : System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent
                };
                tab.MouseLeftButtonUp += (s, e) => ShowTab(idx);
                TabStrip.Children.Add(tab);
            }

            ContentHost.Content = _tabs[index].Item2;
            FooterBar.Visibility = _tabs[index].Item3 ? Visibility.Visible : Visibility.Collapsed;
            if (_tabs[index].Item1 == "History") RefreshHistory();
            if (_tabs[index].Item1 == "Receipt printer") RefreshPrinterStatus();
        }

        // ------------------------------------------------------------------ rows --

        /// <summary>A white card holding rows separated by thin lines.</summary>
        private Border RowCard(IEnumerable<UIElement> rows)
        {
            var sp = new StackPanel();
            foreach (UIElement r in rows) sp.Children.Add(r);
            var card = Ui.Card(sp, new Thickness(8, 0, 8, 12));
            card.Padding = new Thickness(26, 6, 26, 6);
            card.VerticalAlignment = VerticalAlignment.Top;
            return card;
        }

        /// <summary>Name and explanation on the left, the control on the right.</summary>
        private UIElement Row(string title, string description, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 230 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Ui.Text(title, 13.5, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(description)) left.Children.Add(Ui.Text(description, 11.5, false, Ui.Muted, new Thickness(0)));
            g.Children.Add(left);

            var host = new ContentControl { Content = control, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(host, 1);
            g.Children.Add(host);

            return new Border
            {
                Child = g,
                Padding = new Thickness(0, 13, 0, 13),
                BorderBrush = Ui.Brush("#EDE8DA"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        /// <summary>A heading with an optional one-line hint, then a card of rows.</summary>
        private UIElement Section(string title, string hint, params UIElement[] rows)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (!string.IsNullOrEmpty(title))
            {
                var head = new StackPanel { Margin = new Thickness(10, 6, 8, 8) };
                head.Children.Add(Ui.Text(title, 16, true, Ui.Ink, new Thickness(0)));
                if (!string.IsNullOrEmpty(hint)) head.Children.Add(Ui.Text(hint, 12, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
                sp.Children.Add(head);
            }
            sp.Children.Add(RowCard(rows));
            return sp;
        }

        /// <summary>Rarely needed settings, hidden until asked for so the page stays short.</summary>
        private UIElement Advanced(string title, string hint, UIElement body)
        {
            var arrow = Ui.Text("\u25B8", 14, true, Ui.Muted, new Thickness(0, 0, 10, 0));
            var name = Ui.Text(title, 14, true, Ui.Ink, new Thickness(0));
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(arrow);
            line.Children.Add(name);
            if (!string.IsNullOrEmpty(hint)) line.Children.Add(Ui.Text("   " + hint, 12, false, Ui.Muted, new Thickness(0)));
            var toggle = new Border
            {
                Child = line, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(8, 4, 8, 10),
                Background = System.Windows.Media.Brushes.White, BorderBrush = Ui.Brush("#E8E2D3"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Cursor = System.Windows.Input.Cursors.Hand
            };
            var host = new ContentControl { Content = body, Visibility = Visibility.Collapsed };
            toggle.MouseLeftButtonUp += (s, e) =>
            {
                bool open = host.Visibility != Visibility.Visible;
                host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = open ? "\u25BE" : "\u25B8";
            };
            var sp = new StackPanel();
            sp.Children.Add(toggle);
            sp.Children.Add(host);
            return sp;
        }

        private UIElement NumRow(string key, string title, string description, double width = 130)
        {
            var box = new TextBox { Style = (Style)FindResource("Field"), Height = 36, Width = width };
            _num[key] = box;
            _label[key] = title;
            return Row(title, description, box);
        }

        private UIElement BoolRow(string key, string title, string description)
        {
            var box = new CheckBox { Content = "On", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
            _bool[key] = box;
            _label[key] = title;
            return Row(title, description, box);
        }

        private UIElement Note(string text)
        {
            return new Border { Padding = new Thickness(0, 14, 0, 12), Child = Ui.Text(text, 11.5, false, Ui.Muted, new Thickness(0)) };
        }

        // ------------------------------------------------------- camera & capture --

        private UIElement BuildScanTab()
        {
            _camera = new ComboBox { Style = (Style)FindResource("Combo"), Height = 38, Width = 340 };
            var names = new List<string> { AutoCamera };
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                for (int i = 0; i < devices.Count; i++) names.Add(devices[i].Name);
            }
            catch { }
            _camera.ItemsSource = names;
            _label["camera.name"] = "Camera";

            var page = new StackPanel();
            page.Children.Add(Section("Camera", null,
                Row("Camera", "Chosen automatically when the Logitech BRIO is connected.", _camera),
                BoolRow("camera.max_resolution", "Sharpest picture", "Use the best quality the camera offers.")));
            page.Children.Add(Section("Taking images", null,
                BoolRow("capture.auto", "Automatic capture", "Take each image once the face is steady. The manual button always works."),
                BoolRow("capture.review_before_finish", "Review before the results", "Check each image, and retake it if needed, before the results appear.")));
            page.Children.Add(Section("Touch screen", null,
                BoolRow("preview.zoom_buttons", "Zoom buttons", "Show + and \u2212 on the live picture. Pinching always works."),
                BoolRow("touch.always_keyboard", "Always show the on-screen keyboard", "Turn on if the keyboard does not open when a field is tapped.")));

            page.Children.Add(Advanced("Advanced", "Fine-tuning. The defaults work well; change only if you need to.",
                RowCard(new[]
                {
                    NumRow("camera.width", "Picture width (pixels)", "Used when Sharpest picture is off. Default 640."),
                    NumRow("camera.height", "Picture height (pixels)", "Used when Sharpest picture is off. Default 480."),
                    NumRow("preview.zoom_min", "Smallest zoom", "1 shows the whole picture. Default 1."),
                    NumRow("preview.zoom_max", "Largest zoom", "How far the live picture can be enlarged. Default 4."),
                    NumRow("capture.stable_frames", "Steady readings needed", "In a row, before an image is taken. Default 8."),
                    NumRow("capture.hold_ms", "Hold-still time (ms)", "How long the face must stay still. Default 1200."),
                    NumRow("capture.cooldown_s", "Pause between images (s)", "Prevents duplicates. Default 4."),
                    NumRow("capture.min_sharpness", "Minimum sharpness", "Blurrier images are rejected. Default 25."),
                    NumRow("capture.min_brightness", "Minimum brightness", "From 0 (black) to 255 (white). Default 45."),
                    NumRow("capture.max_brightness", "Maximum brightness", "From 0 (black) to 255 (white). Default 215."),
                    NumRow("pose.front_max_yaw", "Front view: largest head turn", "Default 0.12."),
                    NumRow("pose.side_min_yaw", "Side views: smallest head turn", "Default 0.20."),
                    Note("Image analysis and lesion marking are fixed for this study and cannot be changed here.")
                })));
            return page;
        }

        // ----------------------------------------------------------- printer tab --
        // Kamenda QR701 (58 mm, TTL serial, ESC/POS). No datasheet confirmed its serial values, so they must match the printer's self-test page.

        private CheckBox _pEnabled, _pSim, _pStyles, _pReset, _pFinal, _pScores, _pAssess;
        private ComboBox _pPort, _pBaud, _pData, _pParity, _pStop, _pFlow;
        private TextBox _pCols, _pFeed, _pDelay;
        private TextBlock _pStatus, _pMsg;
        private Button _pTestBtn;

        private static readonly string[] FlowShown = { "None", "XON / XOFF (software)", "RTS / CTS (hardware)" };
        private static readonly string[] FlowStored = { "None", "XOnXOff", "RequestToSend" };

        private CheckBox On() => new CheckBox { Content = "On", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        private ComboBox Pick(IEnumerable<string> items, double width) =>
            new ComboBox { Style = (Style)FindResource("Combo"), Height = 38, Width = width, ItemsSource = items.ToList() };
        private TextBox Num(double width = 110) => new TextBox { Style = (Style)FindResource("Field"), Height = 36, Width = width };

        private UIElement BuildPrinterTab()
        {
            _pEnabled = On(); _pSim = On(); _pStyles = On(); _pReset = On(); _pFinal = On(); _pScores = On(); _pAssess = On();
            _pPort = Pick(new string[0], 170);
            _pBaud = Pick(PrinterSettings.BaudRates.Select(b => b.ToString(CultureInfo.InvariantCulture)), 130);
            _pData = Pick(new[] { "8", "7" }, 80);
            _pParity = Pick(new[] { "None", "Even", "Odd" }, 100);
            _pStop = Pick(new[] { "One", "Two" }, 90);
            _pFlow = Pick(FlowShown, 230);
            _pCols = Num(); _pFeed = Num(); _pDelay = Num();

            Button refresh = Ui.Btn("Refresh list", (s, e) => FillPorts((string)_pPort.SelectedItem), "SmallButton");
            refresh.Margin = new Thickness(10, 0, 0, 0);
            var portRow = new StackPanel { Orientation = Orientation.Horizontal };
            portRow.Children.Add(_pPort);
            portRow.Children.Add(refresh);

            var serial = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var part in new[] { Tuple.Create("Data bits", (UIElement)_pData), Tuple.Create("Parity", (UIElement)_pParity), Tuple.Create("Stop bits", (UIElement)_pStop) })
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                col.Children.Add(Ui.Text(part.Item1, 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 3)));
                col.Children.Add(part.Item2);
                serial.Children.Add(col);
            }

            _pStatus = Ui.Text("", 12.5, true, Ui.Ink, new Thickness(0));
            _pMsg = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 10, 0, 0));
            var tests = new StackPanel();
            var testButtons = new WrapPanel();
            Button check = Ui.Btn("Check connection", async (s, e) =>
            {
                PrinterSettings ps; string err;
                if (!ReadPrinter(out ps, out err)) { PrinterMessage(err, false); return; }
                PrinterMessage("Opening " + (ps.Simulation ? "nothing (simulation)" : ps.PortName) + "...", true);
                PrinterMessage(await ThermalPrinter.TestConnectionAsync(ps), true);
            }, "SmallButton");
            _pTestBtn = Ui.Btn("Test print", async (s, e) => await TestPrint(), "SmallButton");
            Button layout = Ui.Btn("Receipt layout test", (s, e) =>
            {
                PrinterSettings ps; string err;
                if (!ReadPrinter(out ps, out err)) { PrinterMessage(err, false); return; }
                ReceiptPreviewWindow.ShowSample(ps);
            }, "SmallButton");
            foreach (Button b in new[] { check, _pTestBtn, layout }) { b.Margin = new Thickness(0, 0, 8, 8); testButtons.Children.Add(b); }
            tests.Children.Add(_pStatus);
            tests.Children.Add(testButtons);
            tests.Children.Add(Ui.Text("Tests use the values on this page, even before saving.", 11, false, Ui.Muted, new Thickness(0)));

            Button save = Ui.Btn("Save printer settings", (s, e) => SavePrinter(), "PrimaryButton", 230);
            Button defaults = Ui.Btn("Restore printer defaults", (s, e) => { FillPrinter(new PrinterSettings()); PrinterMessage("The default printer values are shown. Press Save printer settings to apply them.", true); }, "SecondaryButton", 240);
            defaults.Margin = new Thickness(10, 0, 0, 0);
            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            saveRow.Children.Add(save);
            saveRow.Children.Add(defaults);

            FillPrinter(PrinterSettings.Load());
            var page = new StackPanel();
            page.Children.Add(Section("Receipt printer", null,
                Row("Print receipts", "Print patient receipts on the thermal printer. When off, receipts can still be previewed.", _pEnabled),
                Row("Test mode", "Nothing is printed; receipts are saved as files instead.", _pSim),
                Row("Port", "The serial port the printer is connected to.", portRow),
                Row("Connection", "Check the connection and print a test page.", tests)));
            page.Children.Add(Section("On the receipt", null,
                Row("Final receipt first", "After the dermatologist's review, offer the final receipt first.", _pFinal),
                Row("AI scores", "Print the four scores under the classification.", _pScores),
                Row("Dermatologist's assessment", "Print it on final receipts. Notes are never printed.", _pAssess)));
            page.Children.Add(new Border { Child = new StackPanel { Children = { saveRow, _pMsg } }, Margin = new Thickness(8, 0, 8, 14) });
            page.Children.Add(Advanced("Advanced", "Printer connection and layout. Change only if printing looks wrong.",
                RowCard(new UIElement[]
                {
                    Row("Baud rate", "Must match the printer. Hold its feed button while switching it on to print a page that shows it.", _pBaud),
                    Row("Serial format", "Usually 8 data bits, no parity, 1 stop bit.", serial),
                    Row("Flow control", "Keep None unless the printer's documentation says otherwise.", _pFlow),
                    Row("Characters per line", "From 24 to 48. 32 fits the paper width.", _pCols),
                    Row("Bold and large text", "Turn on only after the test page shows its style test correctly.", _pStyles),
                    Row("Reset before each receipt", "Prevents an earlier job from changing the layout.", _pReset),
                    Row("Paper feed after a receipt", "Blank lines, from 0 to 12. Default 4.", _pFeed),
                    Row("Pause after each line (ms)", "Increase if lines go missing. From 0 to 500. Default 20.", _pDelay),
                    Note("Garbled characters usually mean the baud rate or serial format does not match. Nothing printed: check the power, the paper roll (shiny side towards the print head) and the wiring. Missing lines: increase the pause after each line.")
                })));
            return page;
        }

        private void FillPorts(string keep)
        {
            var ports = ThermalPrinter.PortNames().ToList();
            if (!string.IsNullOrWhiteSpace(keep) && !ports.Contains(keep, StringComparer.OrdinalIgnoreCase)) ports.Insert(0, keep);
            _pPort.ItemsSource = ports;
            _pPort.SelectedItem = ports.FirstOrDefault(p => string.Equals(p, keep, StringComparison.OrdinalIgnoreCase));
            if (_pStatus != null) RefreshPrinterStatus();
        }

        private void FillPrinter(PrinterSettings s)
        {
            _pEnabled.IsChecked = s.Enabled;
            _pSim.IsChecked = s.Simulation;
            FillPorts(s.PortName);
            _pBaud.SelectedItem = s.BaudRate.ToString(CultureInfo.InvariantCulture);
            if (_pBaud.SelectedItem == null)
            {
                var list = ((List<string>)_pBaud.ItemsSource).ToList();
                list.Insert(0, s.BaudRate.ToString(CultureInfo.InvariantCulture));
                _pBaud.ItemsSource = list;
                _pBaud.SelectedIndex = 0;
            }
            _pData.SelectedItem = s.DataBits.ToString(CultureInfo.InvariantCulture);
            _pParity.SelectedItem = s.Parity;
            _pStop.SelectedItem = s.StopBits;
            int flow = Array.IndexOf(FlowStored, s.Handshake);
            _pFlow.SelectedIndex = flow < 0 ? 0 : flow;
            _pCols.Text = s.CharsPerLine.ToString(CultureInfo.InvariantCulture);
            _pFeed.Text = s.FeedLines.ToString(CultureInfo.InvariantCulture);
            _pDelay.Text = s.LineDelayMs.ToString(CultureInfo.InvariantCulture);
            _pStyles.IsChecked = s.Styles;
            _pReset.IsChecked = s.SendReset;
            _pFinal.IsChecked = s.FinalByDefault;
            _pScores.IsChecked = s.ShowScores;
            _pAssess.IsChecked = s.ShowAssessment;
        }

        private bool ReadPrinter(out PrinterSettings s, out string error)
        {
            s = new PrinterSettings();
            var bad = new List<string>();
            int I(TextBox box, string name, int lo, int hi)
            {
                int v;
                if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(name + " must be a whole number from " + lo + " to " + hi); return lo; }
                return v;
            }
            s.Enabled = _pEnabled.IsChecked == true;
            s.Simulation = _pSim.IsChecked == true;
            s.PortName = (_pPort.SelectedItem as string ?? "").Trim();
            int baud;
            if (!int.TryParse(_pBaud.SelectedItem as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud)) { bad.Add("Choose a baud rate"); baud = 9600; }
            s.BaudRate = baud;
            s.DataBits = int.Parse((string)_pData.SelectedItem ?? "8", CultureInfo.InvariantCulture);
            s.Parity = (string)_pParity.SelectedItem ?? "None";
            s.StopBits = (string)_pStop.SelectedItem ?? "One";
            s.Handshake = FlowStored[Math.Max(0, _pFlow.SelectedIndex)];
            s.CharsPerLine = I(_pCols, "Characters per line", 24, 48);
            s.FeedLines = I(_pFeed, "Paper feed after a receipt", 0, 12);
            s.LineDelayMs = I(_pDelay, "Pause after each line", 0, 500);
            s.Styles = _pStyles.IsChecked == true;
            s.SendReset = _pReset.IsChecked == true;
            s.FinalByDefault = _pFinal.IsChecked == true;
            s.ShowScores = _pScores.IsChecked == true;
            s.ShowAssessment = _pAssess.IsChecked == true;
            if (s.Enabled && !s.Simulation && s.PortName.Length == 0) bad.Add("Choose the printer's COM port, or turn on simulation mode");
            error = bad.Count > 0 ? string.Join(". ", bad) + "." : null;
            return bad.Count == 0;
        }

        private void PrinterMessage(string text, bool ok)
        {
            _pMsg.Foreground = ok ? Ui.Ink : Ui.Bad;
            _pMsg.Text = text;
        }

        private void RefreshPrinterStatus()
        {
            PrinterStatus st = ThermalPrinter.Status(PrinterSettings.Load());
            _pStatus.Text = "Saved settings: " + st.Headline + ". " + st.Detail;
            _pStatus.Foreground = st.Level == 0 ? Ui.Good : st.Level == 1 ? Ui.Warn : Ui.Bad;
        }

        private async System.Threading.Tasks.Task TestPrint()
        {
            PrinterSettings ps; string err;
            if (!ReadPrinter(out ps, out err)) { PrinterMessage(err, false); return; }
            ps.Enabled = true;                                        // the test may run before the printer is switched on in Settings
            _pTestBtn.IsEnabled = false;
            PrinterMessage(ps.Simulation ? "Simulating the test page..." : "Sending the test page to " + ps.PortName + "...", true);
            try
            {
                SendResult r = await ThermalPrinter.SendAsync(EscPos.TestPage(ps), ps, null, System.Threading.CancellationToken.None);
                if (r.Outcome == SendOutcome.SentToPrinter)
                    PrinterMessage("Test page sent (" + r.BytesSent + " bytes). Check the paper: the " + ps.CharsPerLine + "-character ruler must fit on one line, and the style test shows whether bold and large text work. " +
                                   "If nothing came out, check the power, the paper and the baud rate.", true);
                else if (r.Outcome == SendOutcome.Simulated)
                    PrinterMessage("SIMULATION: nothing was printed. The test page was saved as " + System.IO.Path.ChangeExtension(r.SimulationFile, ".txt"), true);
                else PrinterMessage("Test print failed. " + r.Error, false);
            }
            finally { _pTestBtn.IsEnabled = true; }
        }

        private void SavePrinter()
        {
            PrinterSettings ps; string err;
            if (!ReadPrinter(out ps, out err)) { PrinterMessage(err, false); return; }
            try
            {
                int n = ps.Save();
                PrinterMessage(n == 0 ? "Nothing changed." : (n == 1 ? "1 printer setting was" : n + " printer settings were") + " saved.", true);
                Toast.Show(n == 0 ? "Nothing changed" : "Printer settings saved", n == 0 ? ToastKind.Info : ToastKind.Success);
                RefreshPrinterStatus();
            }
            catch (Exception ex) { PrinterMessage("The printer settings could not be saved: " + ex.Message, false); }
        }

        // ---------------------------------------------------------- accounts tab --

        private TextBlock _acctMsg, _acctList;
        private PasswordBox _curPw, _newPw, _newPw2;

        private static PasswordBox Pw() => new PasswordBox { Style = (Style)Application.Current.FindResource("FieldPassword"), Height = 36, Width = 300, Margin = new Thickness(0, 0, 0, 8) };

        private static StackPanel Labeled(params Tuple<string, UIElement>[] items)
        {
            var sp = new StackPanel();
            foreach (var it in items)
            {
                sp.Children.Add(Ui.Text(it.Item1, 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 3)));
                sp.Children.Add(it.Item2);
            }
            return sp;
        }

        private StackPanel _acctHost;

        private UIElement BuildAccountsTab()
        {
            _curPw = Pw(); _newPw = Pw(); _newPw2 = Pw();
            var change = Labeled(Tuple.Create("Current password", (UIElement)_curPw), Tuple.Create("New password", (UIElement)_newPw), Tuple.Create("Confirm new password", (UIElement)_newPw2));
            Button changeBtn = Ui.Btn("Change password", (s, e) => ChangeMyPassword(), "SecondaryButton", 190);
            changeBtn.HorizontalAlignment = HorizontalAlignment.Left;
            change.Children.Add(changeBtn);

            _acctMsg = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 12, 0, 6));
            _acctHost = new StackPanel();
            RefreshAccounts();

            var rows = new System.Collections.Generic.List<UIElement>();
            if (AppSession.IsAdministrator)
            {
                Button addBtn = Ui.Btn("Create account", (s, e) => { if (ProfileDialog.CreateAccount(Application.Current.MainWindow)) { RefreshAccounts(); AcctMessage("The account was created.", true); } }, "PrimaryButton", 200);
                addBtn.HorizontalAlignment = HorizontalAlignment.Left;
                addBtn.Margin = new Thickness(0, 10, 0, 0);
                var host = new StackPanel();
                host.Children.Add(_acctHost);
                host.Children.Add(addBtn);
                rows.Add(Row("Accounts", "Everyone listed here can sign in.", host));
            }
            rows.Add(Row("Change my password", "You are signed in as " + (string.IsNullOrEmpty(AppSession.UserId) ? "(no account)" : AppSession.UserId) + ".", change));
            rows.Add(new Border { Child = _acctMsg });
            return RowCard(rows.ToArray());
        }

        private void RefreshAccounts()
        {
            if (_acctHost == null) return;
            _acctHost.Children.Clear();
            try
            {
                var g = new Grid();
                double[] w = { 150, 230, 140, 130 };
                foreach (double x in w) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(x) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string[] head = { "Username", "Name", "Type", "Created", "" };
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int k = 0; k < head.Length; k++) { var t = Ui.Text(head[k], 11, true, Ui.Muted, new Thickness(0, 0, 8, 4)); Grid.SetColumn(t, k); g.Children.Add(t); }
                int row = 1;
                foreach (string[] a in AuthService.Accounts())
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    string[] cells = { a[0], string.IsNullOrWhiteSpace(a[1]) ? "—" : a[1], a[2], Fmt.Date(a[3]) };
                    for (int k = 0; k < cells.Length; k++)
                    {
                        var t = Ui.Text(cells[k], 12.5, k == 0, Ui.Ink, new Thickness(0, 5, 8, 5));
                        Grid.SetRow(t, row); Grid.SetColumn(t, k); g.Children.Add(t);
                    }
                    if (!string.Equals(a[0], AppSession.UserId, StringComparison.OrdinalIgnoreCase))
                    {
                        string who = a[0];
                        Button reset = Ui.Btn("Reset password", (o, e) => { if (PasswordDialogs.AdminReset(Application.Current.MainWindow, who)) AcctMessage("The password for \"" + who + "\" was reset.", true); }, "SmallButton");
                        reset.Height = 38; reset.Margin = new Thickness(0, 2, 0, 2);
                        Grid.SetRow(reset, row); Grid.SetColumn(reset, 4);
                        g.Children.Add(reset);
                    }
                    row++;
                }
                _acctHost.Children.Add(g);
            }
            catch (Exception ex) { _acctHost.Children.Add(Ui.Text("The accounts could not be read: " + ex.Message, 12.5, false, Ui.Bad)); }
        }

        private void AcctMessage(string text, bool ok)
        {
            _acctMsg.Foreground = ok ? Ui.Good : Ui.Bad;
            _acctMsg.Text = text;
        }

        private void ChangeMyPassword()
        {
            if (string.IsNullOrEmpty(AppSession.UserId)) { AcctMessage("Please sign in first.", false); return; }
            if (_newPw.Password != _newPw2.Password) { AcctMessage("The two new passwords do not match.", false); return; }
            try
            {
                AuthService.ChangePassword(AppSession.UserId, _curPw.Password, _newPw.Password);
                _curPw.Clear(); _newPw.Clear(); _newPw2.Clear();
                AcctMessage("Your password was changed.", true);
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        // ----------------------------------------------------------- history tab --

        private UIElement BuildHistoryTab()
        {
            _historyHost = new StackPanel();
            return RowCard(new UIElement[]
            {
                Row("Recent changes", "Who changed which setting, and when.", _historyHost)
            });
        }

        private static string Describe(AuditEntry a)
        {
            // details look like "old -> new | model facial_only"
            string d = a.Details ?? "";
            int bar = d.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) d = d.Substring(0, bar);
            string[] parts = d.Split(new[] { " -> " }, StringSplitOptions.None);
            string name;
            if (!Friendly.TryGetValue(a.EntityID ?? "", out name)) name = a.EntityID;
            string from = parts.Length > 1 ? parts[0] : "", to = parts.Length > 1 ? parts[1] : d;
            if (from == "(default)") from = "the default";
            return name + ": " + (parts.Length > 1 ? "changed from " + from + " to " + to : to);
        }

        private void RefreshHistory()
        {
            _historyHost.Children.Clear();
            try
            {
                List<AuditEntry> rows = StudyRepository.RecentAudit(200)
                    .Where(a => a.Entity == "SystemSettings" && Friendly.ContainsKey(a.EntityID ?? "")).Take(25).ToList();
                if (rows.Count == 0) _historyHost.Children.Add(Ui.Text("No settings have been changed yet.", 12.5, false, Ui.Muted, new Thickness(0)));
                foreach (AuditEntry a in rows)
                {
                    var line = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                    line.Children.Add(Ui.Text(Describe(a), 12.5, true, Ui.Ink, new Thickness(0)));
                    line.Children.Add(Ui.Text(Fmt.When(a.At) + "  ·  " + a.Actor, 11, false, Ui.Muted, new Thickness(0)));
                    _historyHost.Children.Add(line);
                }
            }
            catch (Exception ex) { _historyHost.Children.Add(Ui.Text("The history could not be read: " + ex.Message, 12, false, Ui.Bad, new Thickness(0))); }
        }

        // ---------------------------------------------------------------- data --

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void Fill(ScanSettings s)
        {
            _camera.SelectedItem = string.IsNullOrEmpty(s.CameraName) ? AutoCamera : s.CameraName;
            if (_camera.SelectedItem == null) _camera.SelectedIndex = 0;

            _bool["camera.max_resolution"].IsChecked = s.MaxResolution;
            _num["camera.width"].Text = s.CameraWidth.ToString(CultureInfo.InvariantCulture);
            _num["camera.height"].Text = s.CameraHeight.ToString(CultureInfo.InvariantCulture);
            _num["preview.zoom_min"].Text = F(s.PreviewZoomMin);
            _num["preview.zoom_max"].Text = F(s.PreviewZoomMax);
            _bool["preview.zoom_buttons"].IsChecked = s.PreviewZoomButtons;
            _bool["touch.always_keyboard"].IsChecked = s.AlwaysShowKeyboard;
            _bool["capture.auto"].IsChecked = s.AutoCapture;
            _bool["capture.review_before_finish"].IsChecked = s.ReviewBeforeFinish;
            _num["capture.stable_frames"].Text = s.StableFramesRequired.ToString(CultureInfo.InvariantCulture);
            _num["capture.hold_ms"].Text = s.HoldStillMs.ToString(CultureInfo.InvariantCulture);
            _num["capture.cooldown_s"].Text = F(s.CooldownSeconds);
            _num["capture.min_sharpness"].Text = F(s.MinSharpness);
            _num["capture.min_brightness"].Text = F(s.MinBrightness);
            _num["capture.max_brightness"].Text = F(s.MaxBrightness);
            _num["pose.front_max_yaw"].Text = F(s.FrontMaxAbsYaw);
            _num["pose.side_min_yaw"].Text = F(s.SideMinAbsYaw);
        }

        private bool Read(out ScanSettings s, out string error)
        {
            s = new ScanSettings();          // the fixed (AI / heatmap / detector) values keep their built-in defaults
            error = null;
            var bad = new List<string>();

            int I(string k, int lo, int hi)
            {
                int v;
                if (!int.TryParse(_num[k].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(_label[k] + " must be a whole number from " + lo + " to " + hi); return lo; }
                return v;
            }
            double D(string k, double lo, double hi)
            {
                double v;
                if (!double.TryParse(_num[k].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < lo || v > hi)
                { bad.Add(_label[k] + " must be a number from " + F(lo) + " to " + F(hi)); return lo; }
                return v;
            }

            string cam = _camera.SelectedItem as string;
            s.CameraName = cam == null || cam == AutoCamera ? "" : cam;
            s.MaxResolution = _bool["camera.max_resolution"].IsChecked == true;
            s.CameraWidth = I("camera.width", 160, 4096);
            s.CameraHeight = I("camera.height", 120, 2160);
            s.PreviewZoomMin = D("preview.zoom_min", 0.5, 1);
            s.PreviewZoomMax = D("preview.zoom_max", 1.5, 8);
            s.PreviewZoomButtons = _bool["preview.zoom_buttons"].IsChecked == true;
            s.AlwaysShowKeyboard = _bool["touch.always_keyboard"].IsChecked == true;
            s.AutoCapture = _bool["capture.auto"].IsChecked == true;
            s.ReviewBeforeFinish = _bool["capture.review_before_finish"].IsChecked == true;
            s.StableFramesRequired = I("capture.stable_frames", 1, 60);
            s.HoldStillMs = I("capture.hold_ms", 0, 10000);
            s.CooldownSeconds = D("capture.cooldown_s", 0.5, 30);
            s.MinSharpness = D("capture.min_sharpness", 0, 1000);
            s.MinBrightness = D("capture.min_brightness", 0, 255);
            s.MaxBrightness = D("capture.max_brightness", 0, 255);
            s.FrontMaxAbsYaw = D("pose.front_max_yaw", 0.02, 0.5);
            s.SideMinAbsYaw = D("pose.side_min_yaw", 0.05, 0.8);

            if (s.MinBrightness >= s.MaxBrightness) bad.Add("The minimum brightness must be lower than the maximum brightness");
            if (s.FrontMaxAbsYaw >= s.SideMinAbsYaw) bad.Add("The front-view head turn must be smaller than the side-view head turn");

            if (bad.Count > 0) { error = string.Join(". ", bad) + "."; return false; }
            return true;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            ScanSettings s; string error;
            if (!Read(out s, out error)) { StatusTxt.Foreground = Ui.Bad; StatusTxt.Text = error; return; }
            try
            {
                int n = s.Save();
                StatusTxt.Foreground = Ui.Good;
                StatusTxt.Text = n == 0 ? "Nothing changed." : (n == 1 ? "1 setting was" : n + " settings were") + " saved at " + DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US")) + ".";
                Toast.Show(n == 0 ? "Nothing changed" : "Settings saved", n == 0 ? ToastKind.Info : ToastKind.Success);
            }
            catch (Exception ex)
            {
                StatusTxt.Foreground = Ui.Bad;
                StatusTxt.Text = "The settings could not be saved: " + ex.Message;
            }
        }

        private void DefaultsBtn_Click(object sender, RoutedEventArgs e)
        {
            Fill(new ScanSettings());
            StatusTxt.Foreground = Ui.Muted;
            StatusTxt.Text = "The default values are shown. Press Save settings to apply them.";
        }
    }
}
