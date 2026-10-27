// ============================================================================
// EscPos.cs  -  namespace SkinDevApp.Printing
//
// Turns rendered receipt lines into the bytes sent to the QR701. Only the most
// basic ESC/POS is used unless text styles are turned on in Settings:
//
//   always            text (ASCII) + LF (0x0A)
//   "reset" setting   ESC @        (1B 40)        initialise the printer
//   "styles" setting  ESC E n      (1B 45 n)      bold on / off
//                     GS ! n       (1D 21 n)      double width + height / normal
//
// No status queries (DLE EOT, GS r) are sent: the QR701's TTL interface is
// treated as write-only, so LUMYVUE never claims to know paper or power state.
// One chunk per line, so the sender can pause between lines.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkinDevApp.Printing
{
    public static class EscPos
    {
        private static readonly byte[] Init = { 0x1B, 0x40 };
        private static byte[] Bold(bool on) => new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) };
        private static byte[] Size(bool large) => new byte[] { 0x1D, 0x21, (byte)(large ? 0x11 : 0x00) };
        private const byte LF = 0x0A;

        /// <summary>The receipt as one chunk per printed line.</summary>
        public static List<byte[]> Encode(IList<RenderedLine> lines, PrinterSettings s)
        {
            var chunks = new List<byte[]>();
            if (s.SendReset) chunks.Add(Init);
            foreach (RenderedLine l in lines)
            {
                var b = new List<byte>();
                bool styled = s.Styles && l.Style != ReceiptStyle.Normal;
                if (styled)
                {
                    b.AddRange(Bold(true));
                    if (l.Style == ReceiptStyle.Large) b.AddRange(Size(true));
                }
                b.AddRange(Encoding.ASCII.GetBytes(l.Text.TrimEnd()));
                b.Add(LF);
                if (styled)
                {
                    if (l.Style == ReceiptStyle.Large) b.AddRange(Size(false));
                    b.AddRange(Bold(false));
                }
                chunks.Add(b.ToArray());
            }
            for (int i = 0; i < s.FeedLines; i++) chunks.Add(new[] { LF });
            return chunks;
        }

        /// <summary>
        /// The Settings "Test print": a ruler to check the characters per line, plain text, and a clearly labelled style test
        /// that always sends ESC E and GS ! so staff can see whether the printer supports them.
        /// </summary>
        public static List<byte[]> TestPage(PrinterSettings s)
        {
            var chunks = new List<byte[]>();
            void Line(string t) => chunks.Add(Encoding.ASCII.GetBytes(ReceiptText.Ascii(t)).Concat(new[] { LF }).ToArray());

            if (s.SendReset) chunks.Add(Init);
            Line("LUMYVUE PRINTER TEST");
            Line("Not a patient receipt.");
            Line(new string('-', s.CharsPerLine));
            Line("Settings: " + s.PrinterId);
            Line("Characters per line: " + s.CharsPerLine);
            Line("Ruler (48 characters):");
            Line("123456789012345678901234567890123456789012345678");
            Line("Ruler (" + s.CharsPerLine + " characters, must fit on one line):");
            Line("|" + new string('=', s.CharsPerLine - 2) + "|");
            Line("Plain text: ABC abc 0123 +-/:%.");
            Line(new string('-', s.CharsPerLine));
            Line("STYLE TEST (ESC E, GS !).");
            Line("If the next 2 lines show odd");
            Line("characters, keep styles off.");
            chunks.Add(Bold(true).Concat(Encoding.ASCII.GetBytes("Bold text")).Concat(new[] { LF }).Concat(Bold(false)).ToArray());
            chunks.Add(Size(true).Concat(Encoding.ASCII.GetBytes("LARGE")).Concat(new[] { LF }).Concat(Size(false)).ToArray());
            Line(new string('-', s.CharsPerLine));
            Line("End of test.");
            for (int i = 0; i < s.FeedLines; i++) chunks.Add(new[] { LF });
            return chunks;
        }
    }
}
