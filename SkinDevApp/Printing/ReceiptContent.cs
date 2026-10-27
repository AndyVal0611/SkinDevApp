// ============================================================================
// ReceiptContent.cs  -  namespace SkinDevApp.Printing
//
// A thermal receipt is a list of lines (text, alignment, style). The SAME lines
// are rendered for the on-screen preview and encoded for the printer
// (EscPos.cs), so what is previewed is what is sent.
//
// Everything is plain ASCII (0x20-0x7E): the QR701's code pages are not
// documented, and plain ASCII prints the same on every ESC/POS code page.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkinDevApp.Printing
{
    public enum ReceiptAlign { Left, Center, Right }

    /// <summary>Large = double width and height, so only half as many characters fit on the line.</summary>
    public enum ReceiptStyle { Normal, Bold, Large }

    public sealed class ReceiptLine
    {
        public string Text { get; set; } = "";
        public ReceiptAlign Align { get; set; }
        public ReceiptStyle Style { get; set; }

        public ReceiptLine() { }

        public ReceiptLine(string text, ReceiptAlign align = ReceiptAlign.Left, ReceiptStyle style = ReceiptStyle.Normal)
        {
            Text = text ?? "";
            Align = align;
            Style = style;
        }
    }

    /// <summary>One line exactly as it leaves the printer: ASCII, padded to the paper width.</summary>
    public sealed class RenderedLine
    {
        public string Text { get; set; }
        public ReceiptStyle Style { get; set; }      // the style actually sent (Normal when text styles are turned off)
        public int Columns { get; set; }             // characters that fit on this line
    }

    public static class ReceiptText
    {
        /// <summary>Keeps printable ASCII only; common typographic characters get their ASCII look-alike.</summary>
        public static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (ch >= 0x20 && ch <= 0x7E) { sb.Append(ch); continue; }
                switch (ch)
                {
                    case '—': case '–': case '−': sb.Append('-'); break;      // dashes, minus
                    case '·': case '•': sb.Append('-'); break;                     // middle dot, bullet
                    case '‘': case '’': sb.Append('\''); break;
                    case '“': case '”': sb.Append('"'); break;
                    case '…': sb.Append("..."); break;
                    case ' ': case '\t': sb.Append(' '); break;
                    default:
                        if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) break;   // accents: e + ´ -> e
                        if (ch >= ' ') sb.Append('?');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Word wrap to <paramref name="width"/> characters; continuation lines keep the first line's indent.</summary>
        public static List<string> Wrap(string text, int width)
        {
            var result = new List<string>();
            text = (text ?? "").TrimEnd();
            if (text.Length <= width) { result.Add(text); return result; }

            int indent = text.Length - text.TrimStart().Length;
            if (indent > width / 2) indent = 0;
            string pad = new string(' ', indent);
            string line = pad;
            foreach (string word in text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word;
                while (pad.Length + w.Length > width)                      // a word longer than the line is broken
                {
                    if (line.Trim().Length > 0) { result.Add(line.TrimEnd()); line = pad; }
                    int take = width - pad.Length;
                    result.Add(pad + w.Substring(0, take));
                    w = w.Substring(take);
                }
                if (w.Length == 0) continue;
                if (line.Trim().Length == 0) line = pad + w;
                else if (line.Length + 1 + w.Length <= width) line += " " + w;
                else { result.Add(line.TrimEnd()); line = pad + w; }
            }
            if (line.Trim().Length > 0) result.Add(line.TrimEnd());
            return result;
        }

        /// <summary>
        /// Lays the lines out on the paper: ASCII only, wrapped, aligned and padded. Used by both the preview and the printer.
        /// With <paramref name="styles"/> off, bold and large lines are printed as normal text (only basic ESC/POS is used).
        /// </summary>
        public static List<RenderedLine> Render(IEnumerable<ReceiptLine> lines, int width, bool styles)
        {
            var output = new List<RenderedLine>();
            foreach (ReceiptLine l in lines)
            {
                ReceiptStyle style = styles ? l.Style : ReceiptStyle.Normal;
                int cols = style == ReceiptStyle.Large ? width / 2 : width;
                foreach (string piece in Wrap(Ascii(l.Text), cols))
                {
                    string t = piece;
                    if (l.Align == ReceiptAlign.Center) t = new string(' ', Math.Max(0, (cols - t.Trim().Length) / 2)) + t.Trim();
                    else if (l.Align == ReceiptAlign.Right) t = t.Trim().PadLeft(cols);
                    output.Add(new RenderedLine { Text = t.PadRight(cols).Substring(0, cols), Style = style, Columns = cols });
                }
            }
            return output;
        }

        /// <summary>Plain-text copy (for the simulation file and the history).</summary>
        public static string ToPlainText(IEnumerable<RenderedLine> lines) =>
            string.Join(Environment.NewLine, lines.Select(l => l.Text.TrimEnd()));
    }

    /// <summary>Small helpers that build receipt lines for a given paper width.</summary>
    public sealed class ReceiptWriter
    {
        public readonly List<ReceiptLine> Lines = new List<ReceiptLine>();
        public int Width { get; }

        public ReceiptWriter(int width) { Width = width; }

        public void Center(string text, ReceiptStyle style = ReceiptStyle.Normal) => Lines.Add(new ReceiptLine(text, ReceiptAlign.Center, style));
        public void Left(string text, ReceiptStyle style = ReceiptStyle.Normal) => Lines.Add(new ReceiptLine(text, ReceiptAlign.Left, style));
        public void Heading(string text) => Lines.Add(new ReceiptLine(text, ReceiptAlign.Left, ReceiptStyle.Bold));
        public void Rule() => Lines.Add(new ReceiptLine(new string('-', Width)));
        public void Blank() => Lines.Add(new ReceiptLine(""));

        /// <summary>"Label:          value" on one line when it fits, otherwise the value goes on the next line, indented.</summary>
        public void Pair(string label, string value)
        {
            label = ReceiptText.Ascii(label) + ":";
            value = ReceiptText.Ascii(value);
            if (label.Length + 1 + value.Length <= Width)
                Lines.Add(new ReceiptLine(label + new string(' ', Width - label.Length - value.Length) + value));
            else
            {
                Lines.Add(new ReceiptLine(label));
                Lines.Add(new ReceiptLine("  " + value));
            }
        }

        /// <summary>A paragraph (wrapped when rendered).</summary>
        public void Text(string text) => Lines.Add(new ReceiptLine(text));
    }
}
