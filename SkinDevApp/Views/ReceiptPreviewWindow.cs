// ReceiptPreviewWindow.cs - the thermal receipt as it will come out of the QR701, with Print / Cancel / Close.
//
// The paper is drawn from the SAME rendered lines that are encoded and sent to the printer (ReceiptService.PrintLines),
// at the printer's own scale: 57.5 mm paper, 48 mm (384 dot) print width, one character cell per 384 / characters-per-line dots.
// Printing needs a deliberate press of Print; a second press while sending does nothing, and a repeat within a minute asks first.
// "Sent to printer" is never shown as "printed": the QR701 cannot report paper, so staff confirm it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SkinDevApp.Data;
using SkinDevApp.Printing;

namespace SkinDevApp.Views
{
    public sealed class ReceiptPreviewWindow : Window
    {
        private const double PrintDots = 384, PaperMargin = 38;          // 48 mm of 57.5 mm paper at 8 dots per mm

        private readonly SessionDetail _d;                                // null for the layout sample
        private readonly ReceiptRecord _r;                                // null for the layout sample
        private readonly bool _sample;
        private PrinterSettings _ps;
        private List<RenderedLine> _lines;
        private bool _copy, _superseded, _busy;
        private CancellationTokenSource _cts;
        private long _awaitingConfirm;

        private Border _paperHost;
        private StackPanel _notes;
        private Border _printerBox;
        private TextBlock _message;
        private ProgressBar _progress;
        private StackPanel _confirmRow;
        private Button _print, _cancel, _close;

        // ------------------------------------------------------------------ open --

        /// <summary>Builds (or reuses) the current receipt of this type for the scan and shows it.</summary>
        public static void ShowFor(SessionDetail d, string type, Action changed)
        {
            PrinterSettings ps = PrinterSettings.Load();
            ReceiptRecord r;
            try { r = ReceiptService.Prepare(d, type, ps); }
            catch (InvalidOperationException ex)
            {
                LumyDialog.Show(ex.Message, "LUMYVUE receipt", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch (Exception ex)
            {
                LumyDialog.Show("The receipt could not be saved, so it cannot be previewed or printed. The scan and its results are not affected.\n\n" + ex.Message,
                    "LUMYVUE receipt", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Open(new ReceiptPreviewWindow(d, r, false), changed);
        }

        /// <summary>Shows a saved receipt version exactly as it was generated (history, reprint, retry).</summary>
        public static void ShowRecord(SessionDetail d, ReceiptRecord r, Action changed) => Open(new ReceiptPreviewWindow(d, r, false), changed);

        /// <summary>Settings "Receipt layout test": sample content only, nothing is saved.</summary>
        public static void ShowSample(PrinterSettings ps) => Open(new ReceiptPreviewWindow(null, null, true, ps), null);

        private static void Open(ReceiptPreviewWindow w, Action changed)
        {
            w.Owner = Application.Current.MainWindow;
            w.ShowDialog();
            changed?.Invoke();
        }

        private ReceiptPreviewWindow(SessionDetail d, ReceiptRecord r, bool sample, PrinterSettings ps = null)
        {
            _d = d;
            _r = r;
            _sample = sample;
            _ps = ps ?? PrinterSettings.Load();
            Title = "LUMYVUE · Receipt preview";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LumyDialog.Frame(this, Build(), 940);
            Refresh();
        }

        // ----------------------------------------------------------------- build --

        private FrameworkElement Build()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // the paper, on a soft tray, scrolling vertically
            _paperHost = new Border { Padding = new Thickness(22, 22, 22, 6) };
            var tray = new Border
            {
                Background = Ui.Brush("#ECE6D8"), CornerRadius = new CornerRadius(18),
                Child = new ScrollViewer { Content = _paperHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640, PanningMode = PanningMode.VerticalOnly }
            };
            var left = new StackPanel();
            left.Children.Add(tray);
            left.Children.Add(Ui.Text("Shown at printer size: 57.5 mm paper, 48 mm print width, " + LayoutWidthOf(_r) + " characters per line.", 10.5, false, Ui.Muted, new Thickness(4, 6, 0, 0)));
            g.Children.Add(left);

            var side = new DockPanel();
            Grid.SetColumn(side, 2);
            g.Children.Add(side);

            // buttons at the bottom of the side panel
            var buttons = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            _print = Ui.Btn("Print", (s, e) => Print(), "PrimaryButton");
            _print.Content = Ui.IconLabel("Icon.Printer", "Print", 18);
            _cancel = Ui.Btn("Cancel printing", (s, e) => _cts?.Cancel(), "SecondaryButton");
            _cancel.Visibility = Visibility.Collapsed;
            _close = Ui.Btn("Close", (s, e) => Close(), "GhostButton");
            foreach (Button b in new[] { _print, _cancel, _close }) { b.Margin = new Thickness(0, 0, 0, 8); buttons.Children.Add(b); }
            side.Children.Add(buttons);

            var info = new StackPanel();
            info.Children.Add(Ui.Text(_sample ? "LAYOUT TEST" : "PATIENT RECEIPT", 11, true, Ui.Brush("#8D8D76"), new Thickness(0, 0, 0, 2)));
            bool final = _r != null && _r.ReceiptType == ReceiptKinds.Final;
            info.Children.Add(new TextBlock
            {
                Text = _sample ? "Sample receipt" : final ? "Final receipt" : "Preliminary receipt",
                Style = (Style)FindResource("SectionTitle"), FontSize = 21, Margin = new Thickness(0, 0, 0, 6)
            });
            info.Children.Add(TypeChip());
            if (_r != null)
                info.Children.Add(Ui.InfoRows(new[]
                {
                    Ui.KV("Receipt", _r.ReceiptID + " (version " + _r.ReceiptVersion + ")"),
                    Ui.KV("Scan", _d != null ? _d.Session.DisplayId : _r.ScanSessionID),
                    Ui.KV("Patient", _r.ParticipantID ?? "Not linked"),
                    Ui.KV("Generated", Fmt.When(_r.GeneratedAt)),
                    Ui.KV("Review status", _r.ReviewStatus == "Completed" ? "Completed by dermatologist" : "Dermatologist review pending")
                }, 104, 12));
            else
                info.Children.Add(Ui.Text("Sample content to check the layout and width on the paper. It is not saved and contains no patient data.", 12, false, Ui.Ink, new Thickness(0, 8, 0, 0)));

            _notes = new StackPanel();
            info.Children.Add(_notes);
            _printerBox = new Border();
            info.Children.Add(_printerBox);
            _progress = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Background = Ui.Brush("#EEE8DA"), Foreground = Ui.Brush("#818263") };
            info.Children.Add(_progress);
            _message = Ui.Text("", 12, false, Ui.Ink, new Thickness(0, 10, 0, 0));
            info.Children.Add(_message);

            _confirmRow = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            _confirmRow.Children.Add(Ui.Text("Did the receipt print completely and clearly?", 12.5, true, Ui.Ink, new Thickness(0, 0, 0, 6)));
            var yes = Ui.Btn("Yes, it printed correctly", (s, e) => Confirm(true), "SecondaryButton");
            var no = Ui.Btn("No, it did not print", (s, e) => Confirm(false), "GhostButton");
            yes.Margin = no.Margin = new Thickness(0, 0, 0, 6);
            _confirmRow.Children.Add(yes);
            _confirmRow.Children.Add(no);
            info.Children.Add(_confirmRow);

            side.Children.Add(new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 });
            return g;
        }

        private int LayoutWidthOf(ReceiptRecord r) => r != null ? r.LayoutWidth : Math.Max(24, Math.Min(48, _ps.CharsPerLine));

        private Border TypeChip()
        {
            string text = _sample ? "SAMPLE - NOT A RESULT" : _r.ReceiptType == ReceiptKinds.Final ? "FINAL / REVIEWED" : "PRELIMINARY";
            bool final = !_sample && _r.ReceiptType == ReceiptKinds.Final;
            var chip = Ui.Pill(text, Ui.Brush(final ? "#3E4A2C" : "#7A4B3C"), final ? "#DCE3C9" : "#F6E1D8");
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.Margin = new Thickness(0, 0, 0, 10);
            return chip;
        }

        /// <summary>Re-reads the printer settings and history, and redraws the paper and the notes.</summary>
        private void Refresh()
        {
            if (!_sample) _ps = PrinterSettings.Load();             // the layout test keeps the unsaved values it was opened with
            _notes.Children.Clear();

            if (_sample) _lines = ReceiptService.SampleLines(_ps);
            else
            {
                List<ReceiptAttempt> attempts;
                List<ReceiptRecord> all;
                try
                {
                    attempts = ReceiptRepository.Attempts(_r.ReceiptID);
                    all = ReceiptRepository.ForScan(_r.ScanSessionID);
                }
                catch (Exception ex)
                {
                    attempts = new List<ReceiptAttempt>();
                    all = new List<ReceiptRecord> { _r };
                    _notes.Children.Add(Ui.Callout("Print history unavailable", ex.Message, 2));
                }
                _copy = ReceiptService.WasSentBefore(attempts);
                _superseded = ReceiptService.IsSuperseded(_r, all);
                _lines = ReceiptService.PrintLines(_r, _ps, _copy, _superseded);

                if (_superseded)
                    _notes.Children.Add(Ui.Callout("Earlier version", "A newer receipt exists for this scan. Reprinting this version needs a researcher and is marked on the paper.", 1));
                if (_copy)
                    _notes.Children.Add(Ui.Callout("Already sent to the printer", "Printing again makes a copy, marked \"COPY\" on the paper.", 1));
                ReceiptState st = _d != null ? ReceiptService.State(_d) : null;
                if (st != null && st.Restricted)
                    _notes.Children.Add(Ui.Callout("Restricted record", st.RestrictionReason + " Printing needs a researcher's authorization.", 2));
                if (_r.LayoutWidth != _ps.CharsPerLine)
                    _notes.Children.Add(Ui.Callout("Different paper width", "This receipt was laid out for " + _r.LayoutWidth + " characters per line; the printer is now set to " + _ps.CharsPerLine + ". It prints as laid out.", 1));
            }

            _paperHost.Child = Paper(_lines, LayoutWidthOf(_r));

            PrinterStatus ps = ThermalPrinter.Status(_ps);
            _printerBox.Child = Ui.Callout("Printer: " + ps.Headline, ps.Detail, ps.Level);
            _print.IsEnabled = ps.CanSend && !_busy;
            _print.Content = Ui.IconLabel("Icon.Printer", !ps.CanSend ? "Printing unavailable" : _copy ? "Print a copy" : _superseded ? "Reprint this version" : "Print", 18);
        }

        // ---------------------------------------------------------------- paper --

        /// <summary>The receipt drawn at printer scale: one character cell = 384 / characters-per-line dots, 1 dot = 1 pixel.</summary>
        internal static FrameworkElement Paper(IList<RenderedLine> lines, int width)
        {
            double cell = PrintDots / width;
            double size = cell / 0.55;                          // Consolas: every character is 0.55 em wide
            double lineHeight = Math.Round(cell * 2.5);
            var ink = Ui.Brush("#141414");
            var font = new FontFamily("Consolas, Courier New");

            var text = new StackPanel { Width = PrintDots };
            foreach (RenderedLine l in lines)
            {
                var tb = new TextBlock
                {
                    Text = l.Text, FontFamily = font, FontSize = size, Foreground = ink, TextWrapping = TextWrapping.NoWrap,
                    LineHeight = lineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Height = lineHeight,
                    FontWeight = l.Style == ReceiptStyle.Normal ? FontWeights.Normal : FontWeights.Bold
                };
                if (l.Style == ReceiptStyle.Large) tb.LayoutTransform = new ScaleTransform(2, 2);
                text.Children.Add(tb);
            }

            // faint guides at the edges of the 48 mm print area
            var area = new Grid();
            area.Children.Add(text);
            foreach (HorizontalAlignment side in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
                area.Children.Add(new Rectangle
                {
                    Width = 1, HorizontalAlignment = side, Stroke = Ui.Brush("#E3D9C6"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 4 },
                    Margin = new Thickness(side == HorizontalAlignment.Left ? -6 : 0, 0, side == HorizontalAlignment.Right ? -6 : 0, 0), IsHitTestVisible = false
                });

            var paper = new Border
            {
                Background = Brushes.White, Width = PrintDots + 2 * PaperMargin, Padding = new Thickness(PaperMargin, 26, PaperMargin, 22),
                BorderBrush = Ui.Brush("#E2DCCD"), BorderThickness = new Thickness(1, 1, 1, 0), Child = area, SnapsToDevicePixels = true
            };

            // torn edge at the bottom (the QR701 has a tear bar, no cutter)
            double w = paper.Width, tooth = 10;
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w, 0), false, false);
                for (double x = w; x > 0; x -= tooth)
                {
                    ctx.LineTo(new Point(x - tooth / 2, 7), false, false);
                    ctx.LineTo(new Point(Math.Max(0, x - tooth), 0), false, false);
                }
            }
            var edge = new Path { Data = geo, Fill = Brushes.White, Stroke = Ui.Brush("#E2DCCD"), StrokeThickness = 1, Width = w, Height = 8 };

            var sheet = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sheet.Children.Add(paper);
            sheet.Children.Add(edge);
            return sheet;
        }

        // ---------------------------------------------------------------- print --

        private void Show(string text, int level)
        {
            _message.Text = text;
            _message.Foreground = level == 0 ? Ui.Good : level == 1 ? Ui.Warn : level == 2 ? Ui.Bad : Ui.Ink;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _print.IsEnabled = !busy && ThermalPrinter.Status(_ps).CanSend;
            _close.IsEnabled = !busy;
            _cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy) _progress.Value = 0;
        }

        /// <summary>A print request that was stopped before sending (declined duplicate or authorization) is kept in the history.</summary>
        private void RecordStopped(string reason)
        {
            try
            {
                long id = ReceiptRepository.StartAttempt(_r.ReceiptID, _ps.PrinterId, _copy, null);
                ReceiptRepository.FinishAttempt(id, ReceiptOutcome.Cancelled, 0, 0, reason);
            }
            catch { }
            Show("Not printed: " + reason.TrimEnd('.') + ".", -1);
        }

        private async void Print()
        {
            if (_busy) return;                                            // a second tap while sending does nothing
            _confirmRow.Visibility = Visibility.Collapsed;
            _awaitingConfirm = 0;
            Refresh();
            PrinterStatus status = ThermalPrinter.Status(_ps);
            if (!status.CanSend) { Show(status.Headline + ". " + status.Detail, 2); return; }

            if (_sample) { await PrintSample(); return; }

            // authorization: withdrawn / declined records and earlier versions need a researcher
            var why = new List<string>();
            ReceiptState st = ReceiptService.State(_d);
            if (st.Restricted)
            {
                if (!AppSession.IsResearcher) { Show(st.RestrictionReason + " Only a signed-in researcher can print this receipt.", 2); return; }
                if (LumyDialog.Show(st.RestrictionReason + "\n\nPrint this receipt anyway? Your user name is recorded with the print.", "LUMYVUE receipt",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                { RecordStopped("Restricted record: printing not authorized"); return; }
                why.Add("Restricted record printed with researcher authorization (" + AppSession.ActorId + ")");
            }
            if (_superseded)
            {
                if (!AppSession.IsResearcher) { Show("This is an earlier receipt version. Only a signed-in researcher can reprint it.", 2); return; }
                why.Add("Earlier version reprinted by researcher " + AppSession.ActorId);
            }

            // a repeat within a minute is almost always a double tap: ask first
            List<ReceiptAttempt> attempts;
            try { attempts = ReceiptRepository.Attempts(_r.ReceiptID); }
            catch (Exception ex) { Show("The print history could not be read, so nothing was printed: " + ex.Message, 2); return; }
            ReceiptAttempt recent = attempts.FirstOrDefault(a => a.Outcome == ReceiptOutcome.Sent || a.Outcome == ReceiptOutcome.Confirmed || a.Outcome == ReceiptOutcome.Simulated);
            DateTime when;
            if (recent != null && Fmt.TryParse(recent.FinishedAt ?? recent.RequestedAt, out when) && (DateTime.Now - when).TotalSeconds < 60)
            {
                int secs = Math.Max(1, (int)(DateTime.Now - when).TotalSeconds);
                if (LumyDialog.Show("This receipt was sent to the printer " + secs + (secs == 1 ? " second" : " seconds") + " ago.\n\nPrint another copy?", "LUMYVUE receipt",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                { RecordStopped("Repeat print request cancelled"); return; }
            }

            List<RenderedLine> lines = _lines;                             // exactly what the preview shows
            SetBusy(true);
            Show(_ps.Simulation ? "Simulating the print..." : "Sending the receipt to the printer...", -1);
            _cts = new CancellationTokenSource();
            var progress = new Progress<double>(v => _progress.Value = v);
            PrintOutcome result;
            try
            {
                result = await ReceiptService.PrintAsync(_r, lines, _ps, _copy, why.Count == 0 ? null : string.Join("; ", why), progress, _cts.Token);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                Show("The print request could not be recorded in the database, so nothing was printed. The patient record is not affected.\n\n" + ex.Message, 2);
                return;
            }
            SetBusy(false);
            Refresh();

            SendResult s = result.Send;
            switch (s.Outcome)
            {
                case SendOutcome.SentToPrinter:
                    _awaitingConfirm = result.AttemptID;
                    Show("Sent to the printer (" + s.BytesSent + " bytes). The QR701 cannot report whether paper came out, so please check the receipt.", 0);
                    _confirmRow.Visibility = Visibility.Visible;
                    Toast.Show("Receipt sent to the printer", ToastKind.Success);
                    break;
                case SendOutcome.Simulated:
                    Show("SIMULATION: nothing was printed. The receipt was saved for checking as " + System.IO.Path.ChangeExtension(s.SimulationFile, ".txt"), 1);
                    break;
                case SendOutcome.Cancelled:
                    Show(s.Error ?? "Cancelled.", 1);
                    break;
                default:
                    Show("Printing failed. " + s.Error + "\n\nThe receipt is kept; press Retry when the problem is fixed.", 2);
                    _print.Content = Ui.IconLabel("Icon.Refresh", "Retry", 18);
                    break;
            }
            if (result.HistoryWarning != null) _notes.Children.Add(Ui.Callout("History not updated", result.HistoryWarning, 1));
        }

        private async System.Threading.Tasks.Task PrintSample()
        {
            SetBusy(true);
            _cts = new CancellationTokenSource();
            SendResult s = await ThermalPrinter.SendAsync(EscPos.Encode(_lines, _ps), _ps, new Progress<double>(v => _progress.Value = v), _cts.Token);
            SetBusy(false);
            if (s.Outcome == SendOutcome.SentToPrinter) Show("Sample sent to the printer. Check that every line fits on the paper.", 0);
            else if (s.Outcome == SendOutcome.Simulated) Show("SIMULATION: nothing was printed. Saved as " + System.IO.Path.ChangeExtension(s.SimulationFile, ".txt"), 1);
            else Show((s.Outcome == SendOutcome.Cancelled ? "" : "Printing failed. ") + s.Error, s.Outcome == SendOutcome.Cancelled ? 1 : 2);
        }

        private void Confirm(bool printed)
        {
            if (_awaitingConfirm == 0) return;
            try
            {
                ReceiptRepository.ConfirmAttempt(_awaitingConfirm, printed);
                _confirmRow.Visibility = Visibility.Collapsed;
                _awaitingConfirm = 0;
                Refresh();
                if (printed) Show("Recorded as printed.", 0);
                else
                {
                    Show("Recorded as not printed. Check the paper roll, the printer's power and the cable, then press Retry.", 2);
                    _print.Content = Ui.IconLabel("Icon.Refresh", "Retry", 18);
                }
            }
            catch (Exception ex) { Show("The answer could not be saved: " + ex.Message, 2); }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;                                          // never leave a print half-recorded
                Show("Please wait until sending has finished, or press Cancel printing.", 1);
                return;
            }
            base.OnClosing(e);
        }
    }
}
