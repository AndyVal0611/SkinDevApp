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
            { "capture.auto", "Automatic capture" }, { "capture.review_before_finish", "Review photographs before results" },
            { "capture.stable_frames", "Steady frames needed" }, { "capture.hold_ms", "Hold-still time" },
            { "capture.cooldown_s", "Pause between photographs" }, { "capture.min_sharpness", "Minimum sharpness" },
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

            _tabs.Add(Tuple.Create("Camera", BuildCameraTab(), true));
            _tabs.Add(Tuple.Create("Capture", BuildCaptureTab(), true));
            _tabs.Add(Tuple.Create("Thermal printer", BuildPrinterTab(), false));      // saved with its own button: never mixed with the camera settings
            _tabs.Add(Tuple.Create("Accounts", BuildAccountsTab(), false));
            _tabs.Add(Tuple.Create("Change history", BuildHistoryTab(), false));

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
            if (_tabs[index].Item1 == "Change history") RefreshHistory();
            if (_tabs[index].Item1 == "Thermal printer") RefreshPrinterStatus();
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
                Padding = new Thickness(0, 16, 0, 16),
                BorderBrush = Ui.Brush("#EDE8DA"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
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

        // ------------------------------------------------------------ camera tab --

        private UIElement BuildCameraTab()
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

            return RowCard(new[]
            {
                Row("Camera", "The Logitech BRIO is chosen automatically when it is connected. Change this only if the study uses another camera.", _camera),
                BoolRow("camera.max_resolution", "Sharpest picture", "Use the sharpest picture the camera can give (for example Full HD). Turn this off to use the size below."),
                NumRow("camera.width", "Picture width (pixels)", "Used only when the sharpest picture is turned off. The closest supported camera mode is used. Default 640."),
                NumRow("camera.height", "Picture height (pixels)", "Used only when the sharpest picture is turned off. Default 480."),
                Note("Frame rate and exposure follow the camera driver, which keeps every photograph consistent."),
                NumRow("preview.zoom_min", "Smallest zoom of the live picture", "1 shows the whole picture. Values below 1 let it be shrunk on the screen. Zooming only changes what is shown, never the photograph that is taken. Default 1."),
                NumRow("preview.zoom_max", "Largest zoom of the live picture", "How far the picture can be enlarged by pinching with two fingers or using the + button. Default 4."),
                BoolRow("preview.zoom_buttons", "Show the zoom buttons", "Show + and − buttons on the live picture. Pinching with two fingers always works."),
                BoolRow("touch.always_keyboard", "Always show the on-screen keyboard", "Open the on-screen keyboard whenever a text box is selected, also with a mouse. " +
                        "Turn this on if the keyboard does not open when a field is tapped (some touchscreens are reported to Windows as a mouse). Off by default.")
            });
        }

        // ----------------------------------------------------------- capture tab --

        private UIElement BuildCaptureTab()
        {
            return RowCard(new[]
            {
                BoolRow("capture.auto", "Automatic capture", "Take each photograph automatically once the face is steady. The manual button always stays available."),
                BoolRow("capture.review_before_finish", "Review photographs before the results", "Show each photograph so it can be accepted or retaken before the results appear."),
                NumRow("capture.stable_frames", "Steady frames needed", "How many steady readings in a row are needed before a photograph is taken. Default 8."),
                NumRow("capture.hold_ms", "Hold-still time (milliseconds)", "How long the face must stay still before capture. Default 1200."),
                NumRow("capture.cooldown_s", "Pause between photographs (seconds)", "Prevents duplicate photographs. Default 4."),
                NumRow("capture.min_sharpness", "Minimum sharpness", "A photograph must be at least this sharp to be accepted. Default 25."),
                NumRow("capture.min_brightness", "Minimum brightness", "Average brightness from 0 (black) to 255 (white). Default 45."),
                NumRow("capture.max_brightness", "Maximum brightness", "Average brightness from 0 (black) to 255 (white). Default 215."),
                NumRow("pose.front_max_yaw", "Front view: largest head turn", "How far the head may turn and still count as facing the camera. Default 0.12."),
                NumRow("pose.side_min_yaw", "Side views: smallest head turn", "How far the head must turn for a left or right photograph. Default 0.20."),
                Note("Image analysis, heatmap and lesion-marking settings are fixed for this study and cannot be changed here.")
            });
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
            tests.Children.Add(Ui.Text("The tests use the values on this page, even before they are saved.", 11, false, Ui.Muted, new Thickness(0)));

            Button save = Ui.Btn("Save printer settings", (s, e) => SavePrinter(), "PrimaryButton", 230);
            Button defaults = Ui.Btn("Restore printer defaults", (s, e) => { FillPrinter(new PrinterSettings()); PrinterMessage("The default printer values are shown. Press Save printer settings to apply them.", true); }, "SecondaryButton", 240);
            defaults.Margin = new Thickness(10, 0, 0, 0);
            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 4) };
            saveRow.Children.Add(save);
            saveRow.Children.Add(defaults);

            FillPrinter(PrinterSettings.Load());
            return RowCard(new UIElement[]
            {
                Row("Receipt printer", "Print patient receipts on the QR701 thermal printer built into LUMYVUE. When off, receipts can still be previewed.", _pEnabled),
                Row("Simulation mode", "For testing without the printer: receipts are saved as files and NOTHING is printed. Every screen says so while it is on.", _pSim),
                Row("Connection status", "What LUMYVUE can check. The QR701 cannot report paper or power, so these are never shown as \"ready\".", tests),
                Row("COM port", "The serial port of the USB-to-TTL adapter the printer is wired to. Connect the printer's TTL pins only through such an adapter: never to an RS-232 port.", portRow),
                Row("Baud rate", "Must match the printer. Not confirmed for the QR701: hold the printer's feed button while switching it on to print its self-test page, which shows the baud rate.", _pBaud),
                Row("Serial format", "Usually 8 data bits, no parity, 1 stop bit (8N1). Change only if the self-test page says otherwise.", serial),
                Row("Flow control", "TTL printers usually have no flow-control wire: keep None unless the printer's documentation says otherwise.", _pFlow),
                Row("Characters per line", "32 fits the 48 mm print width in the standard font. The Test print ruler shows whether every line fits. From 24 to 48.", _pCols),
                Row("Bold and large text", "Uses the ESC E and GS ! commands. Turn on only after the Test print shows its style test correctly; otherwise receipts print as plain text.", _pStyles),
                Row("Reset the printer before each receipt", "Sends ESC @ first, so a half-finished earlier job cannot change the layout.", _pReset),
                Row("Paper feed after a receipt (lines)", "Blank lines so the receipt clears the tear bar (the QR701 has no cutter). From 0 to 12. Default 4.", _pFeed),
                Row("Pause after each line (ms)", "Gives the printer time to print, because a TTL link has no flow control. Increase it if lines go missing. From 0 to 500. Default 20.", _pDelay),
                Row("Final receipt by default", "Once the dermatologist has completed the review, offer the final receipt first.", _pFinal),
                Row("Show the four AI scores", "Print the four similarity scores under the AI classification (they fit the 48 mm width).", _pScores),
                Row("Show the dermatologist's assessment", "Print the dermatologist's assessment on final receipts. The notes are never printed.", _pAssess),
                Note("Troubleshooting: garbled characters usually mean the baud rate or serial format does not match the printer. Nothing at all: check the 5 to 9 V power supply, the paper roll (shiny side towards the print head), and that TX of the adapter goes to RX of the printer with a shared ground. Lines missing or cut off: increase the pause after each line, or lower the characters per line."),
                new Border { Child = new StackPanel { Children = { saveRow, _pMsg } }, Padding = new Thickness(0, 0, 0, 12) }
            });
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
        private PasswordBox _curPw, _newPw, _newPw2, _addPw, _addPw2;
        private TextBox _addUser;

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

        private UIElement BuildAccountsTab()
        {
            _acctList = Ui.Text("", 13, false, Ui.Ink, new Thickness(0));

            _curPw = Pw(); _newPw = Pw(); _newPw2 = Pw();
            var change = Labeled(Tuple.Create("Current password", (UIElement)_curPw), Tuple.Create("New password", (UIElement)_newPw), Tuple.Create("Confirm new password", (UIElement)_newPw2));
            Button changeBtn = Ui.Btn("Change password", (s, e) => ChangeMyPassword(), "SecondaryButton", 190);
            changeBtn.HorizontalAlignment = HorizontalAlignment.Left;
            change.Children.Add(changeBtn);

            _addUser = new TextBox { Style = (Style)FindResource("Field"), Height = 36, Width = 300, Margin = new Thickness(0, 0, 0, 8) };
            _addPw = Pw(); _addPw2 = Pw();
            var add = Labeled(Tuple.Create("Username", (UIElement)_addUser),
                              Tuple.Create("Password (at least " + AuthService.MinPasswordLength + " characters)", (UIElement)_addPw),
                              Tuple.Create("Confirm password", (UIElement)_addPw2));
            Button addBtn = Ui.Btn("Add account", (s, e) => AddAccount(), "SecondaryButton", 190);
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;
            add.Children.Add(addBtn);

            _acctMsg = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 12, 0, 6));

            RefreshAccounts();
            return RowCard(new UIElement[]
            {
                Row("Accounts", "Everyone listed here can sign in and use every screen.", _acctList),
                Row("Change my password", "You are signed in as " + (string.IsNullOrEmpty(AppSession.UserId) ? "(no account)" : AppSession.UserId) + ".", change),
                Row("Add an account", "Create an account for another dermatologist or assistant. Passwords are stored only as salted hashes.", add),
                new Border { Child = _acctMsg }
            });
        }

        private void RefreshAccounts()
        {
            try { _acctList.Text = string.Join(",  ", AuthService.Usernames()); }
            catch (Exception ex) { _acctList.Text = "The accounts could not be read: " + ex.Message; }
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

        private void AddAccount()
        {
            if (_addPw.Password != _addPw2.Password) { AcctMessage("The two passwords do not match.", false); return; }
            try
            {
                AuthService.CreateAccount(_addUser.Text, _addPw.Password, AppSession.ActorId);
                AcctMessage("The account \"" + _addUser.Text.Trim() + "\" was created.", true);
                _addUser.Clear(); _addPw.Clear(); _addPw2.Clear();
                RefreshAccounts();
            }
            catch (Exception ex) { AcctMessage(ex.Message, false); }
        }

        // ----------------------------------------------------------- history tab --

        private UIElement BuildHistoryTab()
        {
            _historyHost = new StackPanel();
            return RowCard(new UIElement[]
            {
                Row("Recent changes", "Every change to the camera, capture and printer settings is recorded here with the date, time and person.", _historyHost)
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
