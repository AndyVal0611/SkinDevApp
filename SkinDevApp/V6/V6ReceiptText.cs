// ============================================================================
// V6ReceiptText.cs  -  namespace SkinDevApp.V6
//
// The receipt block that replaces "AI CLASSIFICATION" + "AI INTERPRETATION" when a scan has a saved V6 assessment (flag on). Plain, short lines
// for a thermal printer. Nothing here changes the scan record or the receipt history format.
// ============================================================================

using SkinDevApp.Printing;

namespace SkinDevApp.V6
{
    public static class V6ReceiptText
    {
        public static void Append(ReceiptWriter b, V6SessionSummary s, bool final)
        {
            b.Rule();
            b.Heading(final ? "AI ASSESSMENT (ORIGINAL)" : "AI ASSESSMENT");
            b.Text("Experimental. One result per photograph from a trained model that combines the whole-photograph model and the lesion detectors.");
            if (!s.Single) b.Text(s.Headline + ".");
            foreach (V6ViewSummary v in s.Views)
            {
                b.Left(v.Label + ":");
                b.Text("  " + v.Headline);
                foreach (string line in v.ConditionLines) b.Text("  " + line);
                b.Text("  " + v.BoxesLine);
                foreach (string flag in v.FlagLines) b.Text("  - " + flag);
            }
            foreach (string note in s.Notes) b.Text(note);
            if (final) b.Text("Shown exactly as the AI saved it. The review below does not change it.");
            b.Text("Marked areas are possible locations only. They do not confirm a diagnosis, and their number is not a measure of severity. \"Not found\" does not rule out a skin condition.");
        }
    }
}
