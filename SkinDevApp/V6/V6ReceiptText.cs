// ============================================================================
// V6ReceiptText.cs  -  namespace SkinDevApp.V6
//
// The AI part of the patient's thermal receipt: the same clinical reading as the Results page (ClinicalSummary), in short lines for a
// 58 mm printer. Outcome, each condition with its result, certainty and facial regions, why a review is needed, the verification status
// and the not-a-diagnosis statement. Nothing here changes the scan record or the receipt history format.
// ============================================================================

using System.Linq;
using SkinDevApp.Printing;

namespace SkinDevApp.V6
{
    public static class V6ReceiptText
    {
        public static void Append(ReceiptWriter b, V6SessionSummary s, bool final)
        {
            b.Rule();
            b.Heading(final ? "AI SCREENING RESULT (ORIGINAL)" : "AI SCREENING RESULT");
            b.Left(s.Clinical.CategoryLabel.ToUpperInvariant());
            b.Text(s.Clinical.Title);
            foreach (ClinicalFinding f in s.Clinical.Findings)
            {
                b.Left(f.Condition + ":");
                b.Text("  " + f.Status + (f.Status == ClinicalSummary.Detected || f.Status == ClinicalSummary.NotDetected ? ", " + f.Certainty.ToLowerInvariant() + " certainty" : ", review needed"));
                if (f.Regions.Count > 0) b.Text("  Area: " + string.Join("; ", f.Regions));
            }
            var reasons = s.Clinical.ReviewReasons.Distinct().ToList();
            if (reasons.Count > 0)
            {
                b.Left("Review needed because:");
                foreach (string r in reasons) b.Text("- " + r);
            }
            foreach (string note in s.Notes) b.Text(note);
            b.Left("Verification: " + s.ReviewStatus);
            if (final) b.Text("Shown exactly as the AI saved it. The dermatologist's review below does not change it.");
            b.Text("Not a diagnosis. \"Not detected\" does not rule out a skin condition. Marked areas are possible locations only.");
        }
    }
}
