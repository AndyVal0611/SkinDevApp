// ============================================================================
// ReviewStatus.cs  -  namespace SkinDevApp.Data
//
// The verification status of a scan, stored in ScanSessions.ReviewStatus and mirrored by the folder the scan's files are kept in.
//   Pending verification     no completed dermatologist review yet (every new scan starts here)
//   Verified                 a dermatologist completed the review
//   Requires further review  the dermatologist asked for further review (e.g. another visit, a retake or a second opinion)
// The status follows the LATEST dermatologist record of the scan; a draft ("Pending") review does not change it.
// ============================================================================

using System;

namespace SkinDevApp.Data
{
    public static class ReviewStatus
    {
        public const string Pending = "Pending verification";
        public const string Verified = "Verified";
        public const string FurtherReview = "Requires further review";

        public static readonly string[] All = { Pending, Verified, FurtherReview };

        // DermatologistValidations.ValidationStatus values (kept compatible with the rows already stored)
        public const string ValidationCompleted = "Completed";
        public const string ValidationFurtherReview = "Further review";
        public const string ValidationDraft = "Pending";

        /// <summary>Scan status that a dermatologist record leads to (null = the record does not change the status, e.g. a draft).</summary>
        public static string FromValidation(string validationStatus)
        {
            if (string.Equals(validationStatus, ValidationCompleted, StringComparison.OrdinalIgnoreCase)) return Verified;
            if (string.Equals(validationStatus, ValidationFurtherReview, StringComparison.OrdinalIgnoreCase)) return FurtherReview;
            return null;
        }

        public static string Normalise(string status)
        {
            foreach (string s in All) if (string.Equals(s, status, StringComparison.OrdinalIgnoreCase)) return s;
            return Pending;
        }

        /// <summary>Folder name under "Patient Records" for a scan with this status (and no participant: "Unassigned scans").</summary>
        public static string FolderFor(string status, bool hasParticipant) => !hasParticipant ? "Unassigned scans" : Normalise(status);
    }
}
