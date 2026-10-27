// ReceiptHistoryWindow.cs - every receipt version of one scan and every print attempt, newest first.
// Each version opens in the preview exactly as it was generated (stored snapshot); earlier versions can be reprinted by a researcher.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Printing;

namespace SkinDevApp.Views
{
    public sealed class ReceiptHistoryWindow : Window
    {
        private readonly SessionDetail _d;
        private readonly StackPanel _list = new StackPanel();
        private bool _changed;

        public static void Open(SessionDetail d, Action changed)
        {
            var w = new ReceiptHistoryWindow(d) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            if (w._changed) changed?.Invoke();
        }

        private ReceiptHistoryWindow(SessionDetail d)
        {
            _d = d;
            Title = "LUMYVUE · Receipt history";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var body = new DockPanel();
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(Ui.Text("PATIENT RECEIPTS", 11, true, Ui.Brush("#8D8D76"), new Thickness(0, 0, 0, 2)));
            head.Children.Add(new TextBlock { Text = "Receipt history · " + d.Session.DisplayId, Style = (Style)FindResource("SectionTitle"), FontSize = 21 });
            head.Children.Add(Ui.Text("Every version keeps the content it was printed with. Reprinting never runs the AI again and never changes the saved results.",
                12, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            body.Children.Add(head);

            Button close = Ui.Btn("Close", (s, e) => Close(), "GhostButton", 140);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 14, 0, 0);
            DockPanel.SetDock(close, Dock.Bottom);
            body.Children.Add(close);

            body.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560, PanningMode = PanningMode.VerticalOnly });
            LumyDialog.Frame(this, body, 780);
            Fill();
        }

        private void Fill()
        {
            _list.Children.Clear();
            List<ReceiptRecord> all;
            try { all = ReceiptRepository.ForScan(_d.Session.ScanSessionID); }
            catch (Exception ex) { _list.Children.Add(Ui.Callout("The history could not be read", ex.Message, 2)); return; }
            if (all.Count == 0)
            {
                _list.Children.Add(Ui.EmptyState("Icon.Receipt", "No receipts yet", "A receipt is saved here the first time it is previewed or printed."));
                return;
            }

            foreach (ReceiptRecord r in all)
            {
                ReceiptRecord rec = r;
                bool superseded = ReceiptService.IsSuperseded(r, all);
                bool final = r.ReceiptType == ReceiptKinds.Final;

                var sp = new StackPanel();
                var top = new DockPanel();
                Button open = Ui.Btn(superseded ? "Preview / reprint" : "Preview / print", (s, e) =>
                {
                    ReceiptPreviewWindow.ShowRecord(_d, rec, null);
                    _changed = true;
                    Fill();
                }, "SmallButton");
                DockPanel.SetDock(open, Dock.Right);
                top.Children.Add(open);

                var title = new WrapPanel();
                title.Children.Add(Ui.Text(r.Label, 14, true, Ui.Ink, new Thickness(0, 0, 10, 0)));
                Border chip = Ui.Pill(final ? "FINAL / REVIEWED" : "PRELIMINARY", Ui.Brush(final ? "#3E4A2C" : "#7A4B3C"), final ? "#DCE3C9" : "#F6E1D8");
                chip.Margin = new Thickness(0, 0, 8, 0);
                title.Children.Add(chip);
                title.Children.Add(Ui.Pill(superseded ? "Earlier version" : "Current", superseded ? Ui.Muted : Ui.Good, superseded ? "#EFEBE1" : "#EEF6EC"));
                top.Children.Add(title);
                sp.Children.Add(top);

                sp.Children.Add(Ui.Text(r.ReceiptID + "  ·  generated " + Fmt.When(r.GeneratedAt) + (string.IsNullOrEmpty(r.GeneratedBy) ? "" : " by " + r.GeneratedBy) +
                                        "  ·  dermatologist review " + (r.ReviewStatus ?? "").ToLowerInvariant(), 11.5, false, Ui.Muted, new Thickness(0, 4, 0, 6)));

                List<ReceiptAttempt> attempts;
                try { attempts = ReceiptRepository.Attempts(r.ReceiptID); }
                catch (Exception ex) { attempts = new List<ReceiptAttempt>(); sp.Children.Add(Ui.Text("Attempts could not be read: " + ex.Message, 11.5, false, Ui.Bad)); }
                if (attempts.Count == 0) sp.Children.Add(Ui.Text("Not printed yet.", 12, false, Ui.Ink));
                foreach (ReceiptAttempt a in attempts)
                {
                    bool bad = a.Outcome == ReceiptOutcome.Failed || a.Outcome == ReceiptOutcome.Requested;
                    var line = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                    line.Children.Add(Ui.Text(Fmt.When(a.RequestedAt) + "  —  " + ReceiptOutcome.Text(a.Outcome) + (a.IsCopy ? "  (copy)" : ""), 12.5, true,
                        bad ? Ui.Bad : a.Outcome == ReceiptOutcome.Confirmed ? Ui.Good : Ui.Ink, new Thickness(0)));
                    string detail = a.PrinterID + (string.IsNullOrEmpty(a.OperatorID) ? "" : "  ·  by " + a.OperatorID) +
                                    (a.BytesTotal.HasValue && a.BytesTotal > 0 ? "  ·  " + a.BytesSent + " of " + a.BytesTotal + " bytes" : "");
                    line.Children.Add(Ui.Text(detail, 11, false, Ui.Muted, new Thickness(0)));
                    if (!string.IsNullOrEmpty(a.Authorization)) line.Children.Add(Ui.Text(a.Authorization, 11, false, Ui.Warn, new Thickness(0)));
                    if (!string.IsNullOrEmpty(a.ErrorDetail)) line.Children.Add(Ui.Text(a.ErrorDetail, 11, false, bad ? Ui.Bad : Ui.Muted, new Thickness(0)));
                    sp.Children.Add(line);
                }

                _list.Children.Add(new Border
                {
                    Child = sp, Background = Ui.Brush(superseded ? "#FAF8F3" : "#FFFDF8"), BorderBrush = Ui.Brush("#E8E2D3"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 10), Margin = new Thickness(0, 0, 0, 10)
                });
            }
        }
    }
}
