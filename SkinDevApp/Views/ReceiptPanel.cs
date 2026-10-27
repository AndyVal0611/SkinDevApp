// ReceiptPanel.cs - the receipt choices for one saved scan, shared by the Results page and the patient record,
// so both always offer the same receipts with the same enabled / disabled states and explanations.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Printing;

namespace SkinDevApp.Views
{
    public static class ReceiptPanel
    {
        public sealed class Item
        {
            public string Text;
            public string Icon;
            public Action Run;
            public bool Enabled;
            public bool Preferred;
        }

        /// <summary>The receipt buttons, the preferred one first.</summary>
        public static List<Item> Items(SessionDetail d, ReceiptState st, PrinterSettings ps, Action changed)
        {
            string def = ReceiptService.DefaultType(st, ps);
            var prints = new List<Item>
            {
                new Item
                {
                    Text = "Print final receipt", Icon = "Icon.Printer", Enabled = st.CanFinal, Preferred = def == ReceiptKinds.Final,
                    Run = () => ReceiptPreviewWindow.ShowFor(d, ReceiptKinds.Final, changed)
                }
            };
            if (st.CanPreliminary)
                prints.Add(new Item
                {
                    Text = "Print preliminary receipt", Icon = "Icon.Printer", Enabled = true, Preferred = def == ReceiptKinds.Preliminary,
                    Run = () => ReceiptPreviewWindow.ShowFor(d, ReceiptKinds.Preliminary, changed)
                });
            else if (st.ReviewCompleted && st.LatestPreliminary != null)
                prints.Add(new Item       // the review is complete: a preliminary receipt is only reprinted as the earlier version it was
                {
                    Text = "Reprint preliminary receipt", Icon = "Icon.Printer", Enabled = AppSession.IsResearcher,
                    Run = () => ReceiptPreviewWindow.ShowRecord(d, st.LatestPreliminary, changed)
                });
            else
                prints.Add(new Item { Text = "Print preliminary receipt", Icon = "Icon.Printer", Enabled = false });

            var items = prints.OrderByDescending(i => i.Preferred).ToList();
            items.Add(new Item
            {
                Text = "Receipt preview", Icon = "Icon.Receipt", Enabled = def != null,
                Run = () => ReceiptPreviewWindow.ShowFor(d, def, changed)
            });
            items.Add(new Item
            {
                Text = "View print history", Icon = "Icon.Clock", Enabled = st.Receipts.Count > 0,
                Run = () => ReceiptHistoryWindow.Open(d, changed)
            });
            if (st.LastFailed && st.LastAttempt != null)
            {
                ReceiptRecord failed = st.Receipts.FirstOrDefault(r => r.ReceiptID == st.LastAttempt.ReceiptID);
                if (failed != null)
                    items.Insert(0, new Item
                    {
                        Text = "Retry last print", Icon = "Icon.Refresh", Enabled = true, Preferred = true,
                        Run = () => ReceiptPreviewWindow.ShowRecord(d, failed, changed)
                    });
            }
            return items;
        }

        /// <summary>Plain status lines: (text, level) with level 0 fine, 1 note, 2 problem, -1 neutral.</summary>
        public static List<Tuple<string, int>> Status(ReceiptState st, PrinterSettings ps)
        {
            var lines = new List<Tuple<string, int>>();
            if (!st.Saved) lines.Add(Tuple.Create(st.NotSavedReason + " Printing is turned off for this scan.", 2));
            else if (st.ReviewCompleted)
                lines.Add(Tuple.Create("Dermatologist review completed on " + Fmt.Date(st.Review.ValidationDate) + ". " +
                                       (ps.FinalByDefault ? "The final receipt is the recommended choice." : "The final receipt is available."), 0));
            else
                lines.Add(Tuple.Create("Dermatologist review pending. A preliminary receipt can be printed now; the final receipt becomes available after the review.", 1));

            if (st.Restricted) lines.Add(Tuple.Create(st.RestrictionReason + " Printing needs a researcher's authorization.", 2));

            PrinterStatus p = ThermalPrinter.Status(ps);
            lines.Add(Tuple.Create("Printer: " + p.Headline + (p.CanSend ? "." : ". Preview only."), p.Level));

            if (st.Receipts.Count > 0)
                lines.Add(Tuple.Create("Receipts: " + string.Join(", ", st.Receipts.OrderBy(r => r.Seq).Select(r => r.ReceiptType + " v" + r.ReceiptVersion)) + ".", -1));
            if (st.LastAttempt != null)
            {
                ReceiptRecord which = st.Receipts.FirstOrDefault(r => r.ReceiptID == st.LastAttempt.ReceiptID);
                lines.Add(Tuple.Create("Last print" + (which == null ? "" : " (" + which.ReceiptType + " v" + which.ReceiptVersion + ")") + ": " +
                                       ReceiptOutcome.Text(st.LastAttempt.Outcome) + ", " + Fmt.When(st.LastAttempt.RequestedAt) +
                                       (st.LastFailed ? ". Retry is available." : "."), st.LastFailed ? 2 : -1));
            }
            if (st.HistoryError != null) lines.Add(Tuple.Create(st.HistoryError, 2));
            return lines;
        }

        public static System.Windows.Media.Brush LevelBrush(int level) =>
            level == 0 ? Ui.Good : level == 1 ? Ui.Warn : level == 2 ? Ui.Bad : Ui.Muted;

        /// <summary>The "Patient receipts" card under the selected scan on the patient record.</summary>
        public static Border Card(SessionDetail d, Action changed)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Patient receipts", 16, true));
            ReceiptState st = ReceiptService.State(d);
            PrinterSettings ps = PrinterSettings.Load();
            foreach (Tuple<string, int> line in Status(st, ps))
                sp.Children.Add(Ui.Text(line.Item1, 12, false, LevelBrush(line.Item2), new Thickness(0, 2, 0, 2)));

            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (Item it in Items(d, st, ps, changed))
            {
                Item item = it;
                Button b = Ui.Btn(it.Text, (s, e) => item.Run?.Invoke(), it.Preferred && it.Enabled ? "PrimaryButton" : "SmallButton");
                b.Content = Ui.IconLabel(it.Icon, it.Text, 16);
                b.IsEnabled = it.Enabled;
                b.Height = 40;
                b.Margin = new Thickness(0, 0, 8, 8);
                b.Padding = new Thickness(16, 0, 16, 0);
                buttons.Children.Add(b);
            }
            sp.Children.Add(buttons);
            sp.Children.Add(Ui.Text("Receipts show the saved AI result and, once complete, the dermatologist's review. They never change the scan or its results.",
                10.5, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            return Ui.Card(sp);
        }
    }
}
