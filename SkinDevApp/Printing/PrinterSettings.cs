// ============================================================================
// PrinterSettings.cs  -  namespace SkinDevApp.Printing
//
// Thermal receipt printer (Kamenda QR701, 58 mm, TTL serial, ESC/POS) settings,
// stored in SystemSettings like the camera settings (every change is audited by
// StudyRepository.SaveSettings).
//
// None of the serial values is confirmed for the QR701: no manufacturer
// datasheet was available. They must match the printer's own self-test page.
// The defaults are deliberately conservative: printer off, plain ASCII text,
// no text-style commands, no flow control.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using SkinDevApp.Data;

namespace SkinDevApp.Printing
{
    public sealed class PrinterSettings
    {
        public bool Enabled { get; set; } = false;
        public bool Simulation { get; set; } = false;           // write the receipt to a file instead of a printer (clearly labelled; nothing is printed)
        public string PortName { get; set; } = "";              // e.g. COM3 (the USB-to-TTL serial adapter)
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "None";            // None | Even | Odd
        public string StopBits { get; set; } = "One";           // One | Two
        public string Handshake { get; set; } = "None";         // None | XOnXOff | RequestToSend
        public int CharsPerLine { get; set; } = 32;             // 48 mm at 8 dots/mm = 384 dots = 32 characters of the standard 12-dot font
        public bool Styles { get; set; } = false;               // bold (ESC E) and double size (GS !): only after the test print shows they work
        public bool SendReset { get; set; } = true;             // ESC @ before each receipt
        public int FeedLines { get; set; } = 4;                 // blank lines after the receipt, so it can be torn off (no cutter)
        public int LineDelayMs { get; set; } = 20;              // pause after each line (no flow control on a TTL link)
        public bool FinalByDefault { get; set; } = true;        // the final (dermatologist-reviewed) receipt is the default once it exists
        public bool ShowScores { get; set; } = true;            // the four AI similarity scores
        public bool ShowAssessment { get; set; } = true;        // the dermatologist's assessment summary on final receipts

        public static readonly int[] BaudRates = { 9600, 19200, 38400, 57600, 115200 };

        /// <summary>Short, non-personal description stored with every print attempt.</summary>
        public string PrinterId =>
            Simulation ? "Simulation (no printer)"
                       : "QR701 " + (string.IsNullOrEmpty(PortName) ? "(no port)" : PortName) + " " + BaudRate.ToString(CultureInfo.InvariantCulture) + " " +
                         DataBits.ToString(CultureInfo.InvariantCulture) + (Parity ?? "N").Substring(0, 1) + (StopBits == "Two" ? "2" : "1") +
                         (Handshake == "None" ? "" : " " + Handshake);

        public static PrinterSettings Load()
        {
            var s = new PrinterSettings();
            try
            {
                if (!StudyDatabase.IsReady) return s;
                Dictionary<string, string> d = StudyRepository.LoadSettings();
                s.Enabled = Bool(d, "printer.enabled", s.Enabled);
                s.Simulation = Bool(d, "printer.simulation", s.Simulation);
                s.PortName = Str(d, "printer.port", s.PortName);
                s.BaudRate = Int(d, "printer.baud", s.BaudRate);
                s.DataBits = Int(d, "printer.data_bits", s.DataBits);
                s.Parity = Str(d, "printer.parity", s.Parity);
                s.StopBits = Str(d, "printer.stop_bits", s.StopBits);
                s.Handshake = Str(d, "printer.handshake", s.Handshake);
                s.CharsPerLine = Int(d, "printer.chars_per_line", s.CharsPerLine);
                s.Styles = Bool(d, "printer.styles", s.Styles);
                s.SendReset = Bool(d, "printer.reset", s.SendReset);
                s.FeedLines = Int(d, "printer.feed_lines", s.FeedLines);
                s.LineDelayMs = Int(d, "printer.line_delay_ms", s.LineDelayMs);
                s.FinalByDefault = Bool(d, "printer.final_default", s.FinalByDefault);
                s.ShowScores = Bool(d, "printer.show_scores", s.ShowScores);
                s.ShowAssessment = Bool(d, "printer.show_assessment", s.ShowAssessment);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[PrinterSettings] " + ex.Message);
            }
            return s;
        }

        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>
            {
                { "printer.enabled", F(Enabled) },
                { "printer.simulation", F(Simulation) },
                { "printer.port", PortName ?? "" },
                { "printer.baud", F(BaudRate) },
                { "printer.data_bits", F(DataBits) },
                { "printer.parity", Parity ?? "None" },
                { "printer.stop_bits", StopBits ?? "One" },
                { "printer.handshake", Handshake ?? "None" },
                { "printer.chars_per_line", F(CharsPerLine) },
                { "printer.styles", F(Styles) },
                { "printer.reset", F(SendReset) },
                { "printer.feed_lines", F(FeedLines) },
                { "printer.line_delay_ms", F(LineDelayMs) },
                { "printer.final_default", F(FinalByDefault) },
                { "printer.show_scores", F(ShowScores) },
                { "printer.show_assessment", F(ShowAssessment) }
            };
        }

        /// <summary>Save (audited). Returns how many values changed.</summary>
        public int Save() => StudyRepository.SaveSettings(ToDictionary(), new PrinterSettings().ToDictionary());

        private static string F(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string F(bool v) => v ? "true" : "false";

        private static string Str(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v != null ? v : def;
        }

        private static int Int(Dictionary<string, string> d, string k, int def)
        {
            string v; int r;
            return d.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static bool Bool(Dictionary<string, string> d, string k, bool def)
        {
            string v; bool r;
            return d.TryGetValue(k, out v) && bool.TryParse(v, out r) ? r : def;
        }
    }
}
