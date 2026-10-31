// ReceiptInterpretation.cs - the receipt's "AI interpretation" block. It prints the same ScanInterpretation, with the same wording
// (InterpretationText), as the Results page and the A4 report. Pure text; no printer or database code.
using System.Collections.Generic;
using SkinDevApp.Views;

namespace SkinDevApp.Printing
{
    public static class ReceiptInterpretation
    {
        public static void Append(ReceiptWriter b, ScanInterpretation i)
        {
            b.Rule();
            b.Heading("AI INTERPRETATION");
            if (i.Category == InterpretationCategory.Unavailable) { b.Text(i.Detail); return; }
            foreach (string line in InterpretationText.Summary(i)) b.Text(line);
            b.Left("Lesion detectors (separate):");
            foreach (string line in InterpretationText.DetectorLines(i)) b.Text("  " + line);
            b.Text("Marked areas are possible locations only. They do not confirm a diagnosis, and their number is not a measure of severity. \"No qualifying detections\" does not rule out a skin condition.");
        }
    }
}
