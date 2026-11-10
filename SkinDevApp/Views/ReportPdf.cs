// ============================================================================
// ReportPdf.cs  -  namespace SkinDevApp.Views
//
// Saves an A4 report as a PDF file in the Reports folder, with no print dialog and no extra software. Each page is drawn at about
// 200 dpi (sharp enough to print) and written into a small standard PDF; the file opens in any PDF viewer and prints on any printer.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class ReportPdf
    {
        /// <summary>The folder where every saved A4 report is kept (created when needed).</summary>
        public static string Folder
        {
            get
            {
                string dir = DataLocation.ReportsDir;
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Renders the pages and writes the PDF. Returns the file path.</summary>
        public static string Save(IList<FrameworkElement> pages, double pageWidth, double pageHeight, string displayId)
        {
            string name = "LUMYVUE_Report_" + Safe(displayId) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".pdf";
            string path = Path.Combine(Folder, name);

            var jpegs = new List<byte[]>();
            int widthPx = 0, heightPx = 0;
            double k = 1654.0 / pageWidth;                                       // about 200 dpi on A4
            foreach (FrameworkElement page in pages)
            {
                page.Measure(new Size(pageWidth, pageHeight));
                page.Arrange(new Rect(0, 0, pageWidth, pageHeight));
                page.UpdateLayout();
                widthPx = (int)Math.Round(pageWidth * k);
                heightPx = (int)Math.Round(pageHeight * k);
                var bmp = new RenderTargetBitmap(widthPx, heightPx, 96 * k, 96 * k, PixelFormats.Pbgra32);
                var white = new DrawingVisual();
                using (DrawingContext dc = white.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));
                bmp.Render(white);
                bmp.Render(page);
                var enc = new JpegBitmapEncoder { QualityLevel = 92 };
                enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
                using (var ms = new MemoryStream()) { enc.Save(ms); jpegs.Add(ms.ToArray()); }
            }
            if (jpegs.Count == 0) throw new InvalidOperationException("The report has no pages.");

            string temp = path + ".tmp";
            WritePdf(temp, jpegs, widthPx, heightPx, "LUMYVUE analysis report " + displayId);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            return path;
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s ?? "scan") sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_');
            return sb.ToString();
        }

        // A4 in PDF points
        private const string W = "595.276", H = "841.89";

        private static void WritePdf(string path, List<byte[]> jpegs, int widthPx, int heightPx, string title)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var offsets = new List<long>();
                Action<string> text = s => { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
                Action<int> begin = n => { while (offsets.Count < n) offsets.Add(0); offsets[n - 1] = fs.Position; text(n + " 0 obj\n"); };

                text("%PDF-1.4\n");
                int pageCount = jpegs.Count;
                // object numbers: 1 catalog, 2 pages, 3 info, then for page i: page = 4+3i, content = 5+3i, image = 6+3i
                begin(1); text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                var kids = new StringBuilder();
                for (int i = 0; i < pageCount; i++) kids.Append(4 + 3 * i).Append(" 0 R ");
                begin(2); text("<< /Type /Pages /Count " + pageCount + " /Kids [ " + kids + "] >>\nendobj\n");
                begin(3); text("<< /Title (" + title.Replace("\\", "").Replace("(", "").Replace(")", "") + ") /Producer (LUMYVUE) >>\nendobj\n");
                for (int i = 0; i < pageCount; i++)
                {
                    int page = 4 + 3 * i, content = 5 + 3 * i, image = 6 + 3 * i;
                    begin(page);
                    text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + W + " " + H + "] /Resources << /XObject << /Im0 " + image + " 0 R >> >> /Contents " + content + " 0 R >>\nendobj\n");
                    string draw = "q " + W + " 0 0 " + H + " 0 0 cm /Im0 Do Q";
                    begin(content);
                    text("<< /Length " + draw.Length + " >>\nstream\n" + draw + "\nendstream\nendobj\n");
                    begin(image);
                    text("<< /Type /XObject /Subtype /Image /Width " + widthPx + " /Height " + heightPx +
                         " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpegs[i].Length + " >>\nstream\n");
                    fs.Write(jpegs[i], 0, jpegs[i].Length);
                    text("\nendstream\nendobj\n");
                }
                long xref = fs.Position;
                int total = 3 + 3 * pageCount;
                text("xref\n0 " + (total + 1) + "\n0000000000 65535 f \n");
                for (int i = 0; i < total; i++) text(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                text("trailer\n<< /Size " + (total + 1) + " /Root 1 0 R /Info 3 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
    }
}
