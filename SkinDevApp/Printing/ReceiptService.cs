// ============================================================================
// ReceiptService.cs  -  namespace SkinDevApp.Printing
//
// Patient-facing receipts for one saved scan:
//
//   Preliminary  after the AI analysis is saved, while the dermatologist review
//                is pending ("AI screening only, not reviewed").
//   Final        after the dermatologist saved a COMPLETED assessment. The
//                original AI result is shown unchanged next to the
//                dermatologist's assessment; agreement is printed only when it
//                was explicitly recorded.
//
// Rules kept here:
//   * Everything is read from the saved scan: nothing is re-run and nothing is
//     written to the scan, its AI rows or its reviews.
//   * No Grad-CAM++ images, model metrics, box coordinates or photographs.
//   * Detector boxes are "possible areas", never confirmed lesions; zero boxes
//     is "none found", never "absent"; a detector that did not run is
//     "not assessed" (the eczema detector is not installed, so eczema is
//     "not assessed" unless a saved scan shows it actually ran).
//   * Only the Patient ID identifies the patient (no name).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SkinDevApp.Data;

namespace SkinDevApp.Printing
{
    public static class ReceiptKinds
    {
        public const string Preliminary = "Preliminary";
        public const string Final = "Final";
    }

    /// <summary>Receipt content before it is saved: the receipt number and issue date are added between Head and Body.</summary>
    public sealed class ReceiptDraft
    {
        public string ScanSessionID { get; set; }
        public string ParticipantID { get; set; }
        public string ReceiptType { get; set; }
        public string ReviewStatus { get; set; }
        public long? ValidationID { get; set; }
        public string AIClass { get; set; }
        public int Width { get; set; }
        public bool Styles { get; set; }
        public List<ReceiptLine> Head { get; set; } = new List<ReceiptLine>();
        public List<ReceiptLine> Body { get; set; } = new List<ReceiptLine>();
    }

    /// <summary>What can be printed for a scan right now, and why not.</summary>
    public sealed class ReceiptState
    {
        public bool Saved { get; set; }
        public string NotSavedReason { get; set; }
        public DermatologistValidation Review { get; set; }          // the latest COMPLETED assessment, or null
        public bool Restricted { get; set; }
        public string RestrictionReason { get; set; }
        public List<ReceiptRecord> Receipts { get; set; } = new List<ReceiptRecord>();
        public ReceiptAttempt LastAttempt { get; set; }
        public string HistoryError { get; set; }

        public bool ReviewCompleted => Review != null;
        public bool CanPreliminary => Saved && !ReviewCompleted;
        public bool CanFinal => Saved && ReviewCompleted;
        public ReceiptRecord LatestPreliminary => Receipts.FirstOrDefault(r => r.ReceiptType == ReceiptKinds.Preliminary);
        public ReceiptRecord LatestFinal => Receipts.FirstOrDefault(r => r.ReceiptType == ReceiptKinds.Final);
        public bool LastFailed => LastAttempt != null && (LastAttempt.Outcome == ReceiptOutcome.Failed || LastAttempt.Outcome == ReceiptOutcome.Requested);
    }

    public sealed class PrintOutcome
    {
        public long AttemptID { get; set; }
        public SendResult Send { get; set; }
        public string HistoryWarning { get; set; }                     // the print happened but the history could not be updated
    }

    public static class ReceiptService
    {
        // ---------------------------------------------------------- availability --

        public static ReceiptState State(SessionDetail d)
        {
            var st = new ReceiptState();
            ScanSessionRow s = d.Session;
            if (!s.Finalized) st.NotSavedReason = "The scan is still being saved. Receipts become available once it is finished.";
            else if (string.IsNullOrEmpty(s.OverallPredictedClass)) st.NotSavedReason = "No AI result was saved for this scan, so there is nothing to print.";
            st.Saved = st.NotSavedReason == null;

            // Validations are newest first: the latest COMPLETED assessment is the one a final receipt reflects
            st.Review = d.Validations.FirstOrDefault(v => v.ValidationStatus == "Completed");

            if (d.Participant != null)
            {
                if (d.Participant.Status == "Withdrawn") { st.Restricted = true; st.RestrictionReason = "This patient has withdrawn from the study."; }
                else
                {
                    Consent consent = null;
                    try { consent = StudyRepository.GetLatestConsent(d.Participant.ParticipantID); } catch { }
                    if (consent != null && consent.Decision == "Declined") { st.Restricted = true; st.RestrictionReason = "The patient's latest consent decision is \"Declined\"."; }
                }
            }

            try
            {
                st.Receipts = ReceiptRepository.ForScan(s.ScanSessionID);
                st.LastAttempt = ReceiptRepository.LastAttemptForScan(s.ScanSessionID);
            }
            catch (Exception ex) { st.HistoryError = "The receipt history could not be read: " + ex.Message; }
            return st;
        }

        /// <summary>The type to offer first: the final receipt once the review is complete (unless turned off in Settings).</summary>
        public static string DefaultType(ReceiptState st, PrinterSettings ps)
        {
            if (st.CanFinal && ps.FinalByDefault) return ReceiptKinds.Final;
            if (st.CanPreliminary) return ReceiptKinds.Preliminary;
            return st.CanFinal ? ReceiptKinds.Final : null;
        }

        /// <summary>A receipt that is no longer the current one for its scan (a newer version, or a preliminary after a final exists).</summary>
        public static bool IsSuperseded(ReceiptRecord r, IList<ReceiptRecord> all)
        {
            if (all.Any(x => x.ReceiptType == r.ReceiptType && x.ReceiptVersion > r.ReceiptVersion)) return true;
            return r.ReceiptType == ReceiptKinds.Preliminary && all.Any(x => x.ReceiptType == ReceiptKinds.Final);
        }

        // ---------------------------------------------------------------- build --

        /// <summary>Builds the receipt for a saved scan. Throws InvalidOperationException (plain message) when it may not be built.</summary>
        public static ReceiptDraft Build(SessionDetail d, string type, PrinterSettings ps)
        {
            ReceiptState st = State(d);
            if (!st.Saved) throw new InvalidOperationException(st.NotSavedReason);
            if (type == ReceiptKinds.Final && !st.CanFinal)
                throw new InvalidOperationException("The final receipt becomes available after the dermatologist has completed and saved the review.");
            if (type == ReceiptKinds.Preliminary && !st.CanPreliminary)
                throw new InvalidOperationException("The dermatologist review is complete, so a new preliminary receipt is not issued. Print the final receipt instead.");

            ScanSessionRow s = d.Session;
            bool final = type == ReceiptKinds.Final;
            int width = Math.Max(24, Math.Min(48, ps.CharsPerLine));
            var draft = new ReceiptDraft
            {
                ScanSessionID = s.ScanSessionID,
                ParticipantID = s.ParticipantID,
                ReceiptType = type,
                ReviewStatus = final ? "Completed" : "Pending",
                ValidationID = final ? st.Review.ValidationID : (long?)null,
                AIClass = s.OverallPredictedClass,
                Width = width,
                Styles = ps.Styles
            };

            var h = new ReceiptWriter(width);
            h.Center("LUMYVUE", ReceiptStyle.Large);
            h.Center("AI SKIN HEALTH MONITORING");
            h.Rule();
            h.Center("SKIN SCREENING SUMMARY", ReceiptStyle.Bold);
            h.Center(final ? "FINAL / REVIEWED" : "PRELIMINARY", ReceiptStyle.Bold);
            h.Rule();
            h.Pair("Patient ID", string.IsNullOrEmpty(s.ParticipantID) ? "Not linked" : s.ParticipantID);
            h.Pair("Scan ID", s.DisplayId);
            h.Pair("Scan date", Stamp(s.StartedAt));
            h.Pair("Receipt type", final ? "FINAL / REVIEWED" : "PRELIMINARY");
            draft.Head = h.Lines;

            var b = new ReceiptWriter(width);
            b.Rule();
            b.Heading(final ? "AI CLASSIFICATION (ORIGINAL)" : "AI CLASSIFICATION");
            b.Pair("Most resembles", Plain(s.OverallPredictedClass).ToUpperInvariant());
            if (ps.ShowScores)
            {
                double[] pct = OverallPercents(d);
                b.Left("AI similarity scores:");
                for (int i = 0; i < StudyText.Classes.Length; i++)
                    b.Pair("  " + Plain(StudyText.Classes[i]), pct[i].ToString("0", CultureInfo.InvariantCulture) + "%");
                b.Text("The scores compare the four groups. They are not chances of having a disease.");
            }
            if (final) b.Text("Shown exactly as the AI saved it. The review below does not change it.");

            b.Rule();
            b.Heading("POSSIBLE SKIN FINDINGS");
            foreach (var f in new[] { Tuple.Create("Acne", "Acne-like spots"), Tuple.Create("Hyperpigmentation", "Pigmentation patches"), Tuple.Create("Eczema", "Eczema-like patches") })
            {
                b.Left(f.Item2 + ":");
                b.Left("  " + Finding(d, f.Item1));
            }
            b.Text("\"Found\" means possible areas were marked for review. They are not confirmed lesions. \"None found\" does not rule out a skin condition.");

            b.Rule();
            b.Heading("DERMATOLOGIST REVIEW");
            if (!final) b.Pair("Status", "PENDING");
            else
            {
                DermatologistValidation v = st.Review;
                b.Pair("Status", "COMPLETED");
                b.Pair("Reviewed", Day(v.ValidationDate));
                if (ps.ShowAssessment && !string.IsNullOrWhiteSpace(v.DermatologistAssessment))
                {
                    b.Left("Dermatologist's assessment:");
                    b.Left("  " + AssessmentText(v.DermatologistAssessment));
                }
                string agree = AgreementText(v.AgreementWithAI);
                if (agree != null)
                {
                    b.Left("AI and dermatologist:");
                    b.Left("  " + agree);
                    if (v.AgreementWithAI == "Disagree" || v.AgreementWithAI == "Partially agree")
                        b.Text("Both results are shown as recorded. Please follow the dermatologist's advice.");
                }
            }

            b.Rule();
            if (!final)
            {
                b.Heading("PRELIMINARY AI SCREENING RESULT");
                b.Text("This result was generated by an AI screening system and has not yet been reviewed by a licensed dermatologist.");
                b.Text("It is not a medical diagnosis. Professional evaluation is advised.");
            }
            else
            {
                b.Heading("DERMATOLOGIST-REVIEWED RESULT");
                b.Text("A licensed dermatologist reviewed this scan separately from the AI.");
                b.Text("This receipt is not a medical diagnosis. Please follow the advice of your healthcare professional.");
            }
            b.Rule();
            b.Center("LUMYVUE research prototype");
            b.Center("Thank you for participating.");
            draft.Body = b.Lines;
            return draft;
        }

        /// <summary>Builds the receipt and saves it (or reuses the identical latest version). Throws on any problem.</summary>
        public static ReceiptRecord Prepare(SessionDetail d, string type, PrinterSettings ps) => ReceiptRepository.SaveOrReuse(Build(d, type, ps));

        /// <summary>
        /// The lines that go to the paper: the saved snapshot, plus a COPY note when this receipt was already sent to the printer,
        /// and a note when a newer receipt exists. The snapshot itself is never changed.
        /// </summary>
        public static List<RenderedLine> PrintLines(ReceiptRecord r, PrinterSettings ps, bool copy, bool superseded)
        {
            List<ReceiptLine> lines = r.Lines();
            var notes = new ReceiptWriter(r.LayoutWidth);
            if (copy)
            {
                notes.Center("** COPY **", ReceiptStyle.Bold);
                notes.Center("Reprinted " + DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant());
            }
            if (superseded) notes.Text("NOTE: A newer receipt exists for this scan. This is an earlier version.");
            if (notes.Lines.Count > 0)
            {
                int at = lines.FindIndex(l => l.Text.Length > 0 && l.Text.Trim('-').Length == 0);   // under the first rule
                lines.InsertRange(at < 0 ? 0 : at + 1, notes.Lines.Concat(new[] { new ReceiptLine(new string('-', r.LayoutWidth)) }));
            }
            return ReceiptText.Render(lines, r.LayoutWidth, r.LayoutStyles && ps.Styles);
        }

        /// <summary>True when an earlier attempt of this receipt reached the printer, so a new print is a copy.</summary>
        public static bool WasSentBefore(IEnumerable<ReceiptAttempt> attempts) =>
            attempts.Any(a => a.Outcome == ReceiptOutcome.Sent || a.Outcome == ReceiptOutcome.Confirmed);

        // ---------------------------------------------------------------- print --

        /// <summary>
        /// Records the request, sends the receipt and records the outcome. If the request cannot be recorded, nothing is printed.
        /// A database problem after sending never loses the print: it is reported in HistoryWarning.
        /// </summary>
        public static async Task<PrintOutcome> PrintAsync(ReceiptRecord r, List<RenderedLine> lines, PrinterSettings ps, bool copy, string authorization,
                                                          IProgress<double> progress, CancellationToken cancel)
        {
            var result = new PrintOutcome();
            result.AttemptID = await Task.Run(() => ReceiptRepository.StartAttempt(r.ReceiptID, ps.PrinterId, copy, authorization));

            result.Send = await ThermalPrinter.SendAsync(EscPos.Encode(lines, ps), ps, progress, cancel);
            string outcome = result.Send.Outcome == SendOutcome.SentToPrinter ? ReceiptOutcome.Sent
                           : result.Send.Outcome == SendOutcome.Simulated ? ReceiptOutcome.Simulated
                           : result.Send.Outcome == SendOutcome.Cancelled ? ReceiptOutcome.Cancelled
                           : ReceiptOutcome.Failed;
            try
            {
                await Task.Run(() => ReceiptRepository.FinishAttempt(result.AttemptID, outcome, result.Send.BytesSent, result.Send.BytesTotal, result.Send.Error));
            }
            catch (Exception ex)
            {
                result.HistoryWarning = "The print history could not be updated (" + ex.Message + "). The receipt and the patient record are not affected.";
            }
            return result;
        }

        /// <summary>The Settings "Receipt layout test": clearly marked sample content, never saved and never linked to a patient.</summary>
        public static List<RenderedLine> SampleLines(PrinterSettings ps)
        {
            int width = Math.Max(24, Math.Min(48, ps.CharsPerLine));
            var w = new ReceiptWriter(width);
            w.Center("LUMYVUE", ReceiptStyle.Large);
            w.Center("AI SKIN HEALTH MONITORING");
            w.Rule();
            w.Center("*** SAMPLE - NOT A RESULT ***", ReceiptStyle.Bold);
            w.Center("LAYOUT TEST", ReceiptStyle.Bold);
            w.Rule();
            w.Pair("Patient ID", "PS-0000");
            w.Pair("Scan ID", "PS-0000-S00");
            w.Pair("Scan date", DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant());
            w.Pair("Receipt type", "SAMPLE");
            w.Rule();
            w.Heading("AI CLASSIFICATION");
            w.Pair("Most resembles", "SAMPLE");
            w.Rule();
            w.Heading("POSSIBLE SKIN FINDINGS");
            w.Left("Acne-like spots:");
            w.Left("  Sample text");
            w.Left("Pigmentation patches:");
            w.Left("  Sample text");
            w.Rule();
            w.Text("This sample shows the receipt layout only. It contains no patient data and no screening result.");
            w.Rule();
            w.Center("Thank you for participating.");
            return ReceiptText.Render(w.Lines, width, ps.Styles);
        }

        // -------------------------------------------------------------- wording --

        private static string Plain(string cls) => cls == "Normal" ? "Normal skin" : cls ?? "-";

        private static string Stamp(string stored)
        {
            DateTime t;
            return SkinDevApp.Views.Fmt.TryParse(stored, out t) ? t.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant() : (stored ?? "-");
        }

        private static string Day(string stored)
        {
            DateTime t;
            return SkinDevApp.Views.Fmt.TryParse(stored, out t) ? t.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() : (stored ?? "-");
        }

        private static string AssessmentText(string stored)
        {
            var parts = stored.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p =>
                p == "Normal" ? "Normal skin" : p == "Other / uncertain" ? "Other or uncertain finding" : p);
            return string.Join(", ", parts).ToUpperInvariant();
        }

        /// <summary>Only what the dermatologist explicitly recorded; null when nothing was recorded.</summary>
        private static string AgreementText(string stored)
        {
            switch (stored)
            {
                case "Agree": return "Agree";
                case "Partially agree": return "Partly agree";
                case "Disagree": return "Do not agree";
                case "Uncertain": return "Could not be determined";
                default: return null;
            }
        }

        /// <summary>The pooled four-class scores (as on the Results page): fused over the views, or the single analysed view.</summary>
        private static double[] OverallPercents(SessionDetail d)
        {
            Scanning.FusedResult f = d.Fusion;
            if (f != null && f.PooledPercent != null && f.PooledPercent.Count > 0)
                return StudyText.Classes.Select(c => { double v; return f.PooledPercent.TryGetValue(c, out v) ? v : 0; }).ToArray();
            CaptureViewRow only = d.Views.FirstOrDefault(v => v.Scores != null);
            return only != null ? only.Scores.AsArray() : new double[4];
        }

        /// <summary>
        /// One detector finding over the analysed photographs. A class counts as assessed in a photograph only when the saved
        /// detector run for that photograph covered the class (read from the run's saved model tag).
        /// </summary>
        public static string Finding(SessionDetail d, string cls)
        {
            List<CaptureViewRow> views = d.Views.Where(v => v.Scores != null).ToList();
            List<CaptureViewRow> assessed = views.Where(v => v.Localization != null && v.Localization.Status == "OK" && Covers(v.Localization.ModelTag, cls)).ToList();
            if (assessed.Count == 0)
            {
                if (views.Any(v => v.Localization != null && v.Localization.Status == "OK")) return "Not assessed (no detector)";
                if (views.Any(v => v.Localization != null && v.Localization.Status == "Failed")) return "Not assessed (detector error)";
                return "Not assessed";
            }
            int found = assessed.Count(v => v.Lesions.Any(l => string.Equals(l.ClassName, cls, StringComparison.OrdinalIgnoreCase)));
            string of = assessed.Count < views.Count ? " (" + assessed.Count + " of " + views.Count + " photos checked)" : "";
            if (found == 0) return "None found" + of;
            if (views.Count <= 1) return "Found";
            return "Found in " + found + " of " + assessed.Count + " photos";
        }

        /// <summary>
        /// Did the saved detector run cover this class? Specialist models are saved as "tag [Acne, 800 px]"; a part without brackets
        /// is the older 3-class detector, which covered acne, hyperpigmentation and eczema.
        /// </summary>
        public static bool Covers(string modelTag, string cls)
        {
            if (string.IsNullOrWhiteSpace(modelTag)) return false;
            foreach (string part in modelTag.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = Regex.Match(part, @"\[([^\]]*)\]");
                if (!m.Success) return true;
                string names = m.Groups[1].Value.Split(',')[0];
                if (names.Split('/').Any(n => string.Equals(n.Trim(), cls, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
    }
}
