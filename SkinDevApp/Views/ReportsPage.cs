// ============================================================================
// ReportsPage.cs  -  namespace SkinDevApp.Views
//
// "Reports": every A4 report that was saved from a scan, newest first. From here a report can be opened, or copied to a USB drive so
// it can be printed on any computer with a printer. The files themselves are PDFs in the Reports folder (Open folder).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class ReportsPage : Page
    {
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _count = Ui.Text("", 12.5, false, Ui.Muted, new Thickness(0, 4, 0, 0));

        public ReportsPage()
        {
            Title = "Reports";
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Ui.Header("Reports"));
            var body = new StackPanel { Margin = new Thickness(28, 18, 28, 24) };
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;

            var top = new StackPanel();
            top.Children.Add(Ui.Text("Saved A4 reports", 17, true, Ui.Ink, new Thickness(0)));
            top.Children.Add(Ui.Text("Reports are saved here as PDF files. To print one, copy it to a USB drive and print it from any computer, or open it here.", 12.5, false, Ui.Muted, new Thickness(0, 4, 0, 12)));
            var buttons = new WrapPanel();
            buttons.Children.Add(Ui.Btn("Open the Reports folder", (s, e) => OpenFolder(), "PrimaryButton"));
            Button all = Ui.Btn("Copy all to USB", (s, e) => CopyToUsb(Files()), "SecondaryButton");
            all.Margin = new Thickness(10, 0, 0, 0);
            buttons.Children.Add(all);
            Button refresh = Ui.Btn("Refresh", (s, e) => Reload(), "GhostButton");
            refresh.Margin = new Thickness(10, 0, 0, 0);
            buttons.Children.Add(refresh);
            top.Children.Add(buttons);
            top.Children.Add(_count);
            body.Children.Add(Ui.Card(top, new Thickness(0, 0, 0, 12)));
            body.Children.Add(_list);
            Loaded += (s, e) => Reload();
        }

        private static List<FileInfo> Files()
        {
            try
            {
                string dir = ReportPdf.Folder;
                return new DirectoryInfo(dir).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTime).ToList();
            }
            catch { return new List<FileInfo>(); }
        }

        private void Reload()
        {
            _list.Children.Clear();
            List<FileInfo> files = Files();
            _count.Text = files.Count == 0 ? "No report has been saved yet. Open a scan, choose the A4 report and press \"Save to Reports\"."
                                           : (files.Count == 1 ? "1 report" : files.Count + " reports");
            foreach (FileInfo f in files)
            {
                FileInfo file = f;
                var row = new DockPanel();
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                buttons.Children.Add(Ui.Btn("Open", (s, e) => Open(file.FullName), "PrimaryButton"));
                Button usb = Ui.Btn("Copy to USB", (s, e) => CopyToUsb(new List<FileInfo> { file }), "SecondaryButton");
                usb.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(usb);
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(Ui.Text(Describe(file.Name), 14, true, Ui.Ink, new Thickness(0)));
                info.Children.Add(Ui.Text("Saved " + Fmt.When(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)) + "  ·  " + Size(file.Length), 12, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
                row.Children.Add(info);
                _list.Children.Add(Ui.Card(row, new Thickness(0, 0, 0, 8)));
            }
        }

        private static string Describe(string fileName)
        {
            // LUMYVUE_Report_PS-0001-S03_20261111_0030.pdf  ->  "Scan PS-0001-S03"
            string n = Path.GetFileNameWithoutExtension(fileName);
            string[] p = n.Split('_');
            return p.Length >= 3 ? "Scan " + p[2] : n;
        }

        private static string Size(long bytes) => bytes >= 1024 * 1024 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB";

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { LumyDialog.Show("The report could not be opened: " + ex.Message + "\n\nYou can still copy it to a USB drive.", "LUMYVUE Reports", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private static void OpenFolder()
        {
            try { Process.Start("explorer.exe", "\"" + ReportPdf.Folder + "\""); }
            catch (Exception ex) { LumyDialog.Show("The folder could not be opened: " + ex.Message, "LUMYVUE Reports", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private static void CopyToUsb(List<FileInfo> files)
        {
            if (files.Count == 0) { LumyDialog.Show("There are no reports to copy yet.", "LUMYVUE Reports"); return; }
            DriveInfo usb = null;
            try { usb = DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Removable && d.IsReady); } catch { }
            if (usb == null)
            {
                LumyDialog.Show("No USB drive was found.\n\nInsert a USB drive and try again.", "LUMYVUE Reports", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                string dir = Path.Combine(usb.RootDirectory.FullName, "LUMYVUE Reports");
                Directory.CreateDirectory(dir);
                foreach (FileInfo f in files) File.Copy(f.FullName, Path.Combine(dir, f.Name), true);
                try { StudyRepository.Audit("Reports copied to USB", "Report", files.Count.ToString(CultureInfo.InvariantCulture), usb.Name); } catch { }
                LumyDialog.Show((files.Count == 1 ? "The report was" : files.Count + " reports were") + " copied to the USB drive (" + usb.Name.TrimEnd('\\') + ") in the folder \"LUMYVUE Reports\".\n\nYou can remove the USB drive safely now.",
                                "LUMYVUE Reports", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LumyDialog.Show("The copy to the USB drive failed: " + ex.Message, "LUMYVUE Reports", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
