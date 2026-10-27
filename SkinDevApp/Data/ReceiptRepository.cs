// ============================================================================
// ReceiptRepository.cs  -  namespace SkinDevApp.Data
//
// Patient receipts and their print history.
//
//   Receipts              one row per receipt VERSION. The printed content is
//                         stored as a snapshot and is never updated: a reprint
//                         prints exactly what was generated, even after the
//                         scan's review status has changed.
//   ReceiptPrintAttempts  one row per print request: requested, sent to the
//                         printer, failed, cancelled, simulated, or confirmed
//                         by staff (the QR701 cannot confirm paper itself).
//
// Nothing here reads or writes the AI tables: generating or printing a receipt
// never changes a saved prediction, detector output or review.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkinDevApp.Printing;

namespace SkinDevApp.Data
{
    public static class ReceiptOutcome
    {
        public const string Requested = "PrintRequested";
        public const string Sent = "SentToPrinter";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
        public const string Simulated = "Simulated";
        public const string Confirmed = "ConfirmedByStaff";

        /// <summary>Wording for people. "Sent to printer" is never shown as "printed".</summary>
        public static string Text(string outcome)
        {
            switch (outcome)
            {
                case Requested: return "Print requested (not finished)";
                case Sent: return "Sent to printer (paper not confirmed)";
                case Failed: return "Failed";
                case Cancelled: return "Cancelled";
                case Simulated: return "Simulation only (nothing printed)";
                case Confirmed: return "Printed (confirmed by staff)";
                default: return outcome ?? "—";
            }
        }
    }

    public sealed class ReceiptRecord
    {
        public string ReceiptID { get; set; }
        public int Seq { get; set; }
        public string ScanSessionID { get; set; }
        public string ParticipantID { get; set; }
        public string ReceiptType { get; set; }              // Preliminary | Final
        public int ReceiptVersion { get; set; }              // 1, 2, ... per scan and type
        public string ReviewStatus { get; set; }             // dermatologist review status when generated: Pending | Completed
        public long? ValidationID { get; set; }              // the dermatologist assessment a final receipt reflects
        public string AIClass { get; set; }                  // the saved AI class shown (copied for the history; the scan is never changed)
        public string ContentJson { get; set; }
        public string ContentSha256 { get; set; }
        public int LayoutWidth { get; set; }
        public bool LayoutStyles { get; set; }
        public string GeneratedAt { get; set; }
        public string GeneratedBy { get; set; }

        public List<ReceiptLine> Lines() => JsonSerializer.Deserialize<List<ReceiptLine>>(ContentJson) ?? new List<ReceiptLine>();
        public string Label => (ReceiptType == ReceiptKinds.Final ? "Final" : "Preliminary") + " receipt, version " + ReceiptVersion;
    }

    public sealed class ReceiptAttempt
    {
        public long AttemptID { get; set; }
        public string ReceiptID { get; set; }
        public string RequestedAt { get; set; }
        public string FinishedAt { get; set; }
        public string OperatorID { get; set; }
        public string PrinterID { get; set; }
        public string Outcome { get; set; }
        public int? BytesSent { get; set; }
        public int? BytesTotal { get; set; }
        public bool IsCopy { get; set; }
        public string Authorization { get; set; }
        public string ErrorDetail { get; set; }
    }

    public static class ReceiptRepository
    {
        // ------------------------------------------------------------ receipts --

        /// <summary>
        /// Saves the draft as a new receipt version, or returns the latest version of the same type when its content is identical
        /// (so opening the preview again does not create a new version). The receipt number and issue date are added here.
        /// </summary>
        public static ReceiptRecord SaveOrReuse(ReceiptDraft draft)
        {
            string hash = Sha256(JsonSerializer.Serialize(draft.Head.Concat(draft.Body).ToList()) + "|" + draft.Width + "|" + (draft.Styles ? 1 : 0) + "|" + draft.ReceiptType);

            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                ReceiptRecord latest = null;
                using (var cmd = StudyRepository.Cmd(c, "SELECT * FROM Receipts WHERE ScanSessionID=@s AND ReceiptType=@t ORDER BY ReceiptVersion DESC LIMIT 1;", tx,
                    "@s", draft.ScanSessionID, "@t", draft.ReceiptType))
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) latest = Read(r);
                if (latest != null && latest.ContentSha256 == hash) { tx.Commit(); return latest; }

                int seq, version;
                using (var cmd = StudyRepository.Cmd(c, "SELECT COALESCE(MAX(Seq),0)+1 FROM Receipts;", tx))
                    seq = Convert.ToInt32(cmd.ExecuteScalar());
                version = latest == null ? 1 : latest.ReceiptVersion + 1;

                var rec = new ReceiptRecord
                {
                    ReceiptID = "RC-" + seq.ToString("D5", CultureInfo.InvariantCulture),
                    Seq = seq,
                    ScanSessionID = draft.ScanSessionID,
                    ParticipantID = draft.ParticipantID,
                    ReceiptType = draft.ReceiptType,
                    ReceiptVersion = version,
                    ReviewStatus = draft.ReviewStatus,
                    ValidationID = draft.ValidationID,
                    AIClass = draft.AIClass,
                    ContentSha256 = hash,
                    LayoutWidth = draft.Width,
                    LayoutStyles = draft.Styles,
                    GeneratedAt = StudyRepository.Now(),
                    GeneratedBy = AppSession.ActorId
                };

                // the snapshot: heading lines, receipt number and issue date, then the body
                var id = new ReceiptWriter(draft.Width);
                id.Pair("Receipt", rec.ReceiptID + " v" + version);
                DateTime now = DateTime.Now;
                id.Pair("Issued", now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant());
                rec.ContentJson = JsonSerializer.Serialize(draft.Head.Concat(id.Lines).Concat(draft.Body).ToList());

                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO Receipts
                    (ReceiptID, Seq, ScanSessionID, ParticipantID, ReceiptType, ReceiptVersion, ReviewStatus, ValidationID, AIClass,
                     ContentJson, ContentSha256, LayoutWidth, LayoutStyles, GeneratedAt, GeneratedBy)
                    VALUES (@id,@seq,@s,@p,@t,@v,@rs,@val,@ai,@cj,@h,@w,@st,@at,@by);", tx,
                    "@id", rec.ReceiptID, "@seq", seq, "@s", rec.ScanSessionID, "@p", rec.ParticipantID, "@t", rec.ReceiptType, "@v", version,
                    "@rs", rec.ReviewStatus, "@val", rec.ValidationID, "@ai", rec.AIClass, "@cj", rec.ContentJson, "@h", hash,
                    "@w", rec.LayoutWidth, "@st", rec.LayoutStyles ? 1 : 0, "@at", rec.GeneratedAt, "@by", rec.GeneratedBy))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, "Receipt generated", "Receipt", rec.ReceiptID,
                    rec.ReceiptType + " v" + version + " for scan " + rec.ScanSessionID + " (review " + rec.ReviewStatus + ")");
                tx.Commit();
                return rec;
            }
        }

        public static ReceiptRecord Get(string receiptId)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT * FROM Receipts WHERE ReceiptID=@id;", null, "@id", receiptId))
            using (var r = cmd.ExecuteReader())
                return r.Read() ? Read(r) : null;
        }

        /// <summary>Every receipt version of a scan, newest first.</summary>
        public static List<ReceiptRecord> ForScan(string scanSessionId)
        {
            var list = new List<ReceiptRecord>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT * FROM Receipts WHERE ScanSessionID=@s ORDER BY Seq DESC;", null, "@s", scanSessionId))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(Read(r));
            return list;
        }

        // ------------------------------------------------------------ attempts --

        /// <summary>Records the print request BEFORE anything is sent, so every attempt is in the history even if the app stops.</summary>
        public static long StartAttempt(string receiptId, string printerId, bool isCopy, string authorization)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                long id;
                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO ReceiptPrintAttempts (ReceiptID, RequestedAt, OperatorID, PrinterID, Outcome, IsCopy, Authorization)
                    VALUES (@r,@at,@op,@pr,@o,@cp,@au); SELECT last_insert_rowid();", tx,
                    "@r", receiptId, "@at", StudyRepository.Now(), "@op", AppSession.ActorId, "@pr", printerId, "@o", ReceiptOutcome.Requested,
                    "@cp", isCopy ? 1 : 0, "@au", authorization))
                    id = Convert.ToInt64(cmd.ExecuteScalar());
                StudyRepository.Audit(c, tx, "Receipt print requested", "Receipt", receiptId, printerId + (isCopy ? " (copy)" : "") +
                    (string.IsNullOrEmpty(authorization) ? "" : " | " + authorization));
                tx.Commit();
                return id;
            }
        }

        public static void FinishAttempt(long attemptId, string outcome, int bytesSent, int bytesTotal, string error)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                string receiptId;
                using (var cmd = StudyRepository.Cmd(c, "SELECT ReceiptID FROM ReceiptPrintAttempts WHERE AttemptID=@a;", tx, "@a", attemptId))
                    receiptId = Convert.ToString(cmd.ExecuteScalar());
                using (var cmd = StudyRepository.Cmd(c, @"UPDATE ReceiptPrintAttempts SET Outcome=@o, FinishedAt=@at, BytesSent=@b, BytesTotal=@t, ErrorDetail=@e
                    WHERE AttemptID=@a;", tx, "@o", outcome, "@at", StudyRepository.Now(), "@b", bytesSent, "@t", bytesTotal, "@e", error, "@a", attemptId))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, "Receipt print " + outcome, "Receipt", receiptId, bytesSent + " of " + bytesTotal + " bytes" + (string.IsNullOrEmpty(error) ? "" : " | " + error));
                tx.Commit();
            }
        }

        /// <summary>Staff looked at the paper: it printed correctly, or it did not (the attempt then counts as failed).</summary>
        public static void ConfirmAttempt(long attemptId, bool printedCorrectly)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                string receiptId;
                using (var cmd = StudyRepository.Cmd(c, "SELECT ReceiptID FROM ReceiptPrintAttempts WHERE AttemptID=@a;", tx, "@a", attemptId))
                    receiptId = Convert.ToString(cmd.ExecuteScalar());
                using (var cmd = StudyRepository.Cmd(c, @"UPDATE ReceiptPrintAttempts SET Outcome=@o, ErrorDetail=COALESCE(ErrorDetail, @e)
                    WHERE AttemptID=@a AND Outcome=@sent;", tx,
                    "@o", printedCorrectly ? ReceiptOutcome.Confirmed : ReceiptOutcome.Failed,
                    "@e", printedCorrectly ? null : "Staff reported that the receipt did not print correctly.",
                    "@a", attemptId, "@sent", ReceiptOutcome.Sent))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, printedCorrectly ? "Receipt confirmed printed" : "Receipt reported not printed", "Receipt", receiptId, "attempt " + attemptId);
                tx.Commit();
            }
        }

        public static List<ReceiptAttempt> Attempts(string receiptId)
        {
            var list = new List<ReceiptAttempt>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT * FROM ReceiptPrintAttempts WHERE ReceiptID=@r ORDER BY AttemptID DESC;", null, "@r", receiptId))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadAttempt(r));
            return list;
        }

        /// <summary>The latest print attempt of any receipt of the scan (null when nothing was requested yet).</summary>
        public static ReceiptAttempt LastAttemptForScan(string scanSessionId)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, @"SELECT a.* FROM ReceiptPrintAttempts a JOIN Receipts r ON r.ReceiptID = a.ReceiptID
                WHERE r.ScanSessionID=@s ORDER BY a.AttemptID DESC LIMIT 1;", null, "@s", scanSessionId))
            using (var r = cmd.ExecuteReader())
                return r.Read() ? ReadAttempt(r) : null;
        }

        // ------------------------------------------------------------- helpers --

        private static ReceiptRecord Read(SQLiteDataReader r) => new ReceiptRecord
        {
            ReceiptID = StudyRepository.S(r, "ReceiptID"),
            Seq = (int)(StudyRepository.L(r, "Seq") ?? 0),
            ScanSessionID = StudyRepository.S(r, "ScanSessionID"),
            ParticipantID = StudyRepository.S(r, "ParticipantID"),
            ReceiptType = StudyRepository.S(r, "ReceiptType"),
            ReceiptVersion = (int)(StudyRepository.L(r, "ReceiptVersion") ?? 1),
            ReviewStatus = StudyRepository.S(r, "ReviewStatus"),
            ValidationID = StudyRepository.L(r, "ValidationID"),
            AIClass = StudyRepository.S(r, "AIClass"),
            ContentJson = StudyRepository.S(r, "ContentJson"),
            ContentSha256 = StudyRepository.S(r, "ContentSha256"),
            LayoutWidth = (int)(StudyRepository.L(r, "LayoutWidth") ?? 32),
            LayoutStyles = StudyRepository.B(r, "LayoutStyles"),
            GeneratedAt = StudyRepository.S(r, "GeneratedAt"),
            GeneratedBy = StudyRepository.S(r, "GeneratedBy")
        };

        private static ReceiptAttempt ReadAttempt(SQLiteDataReader r) => new ReceiptAttempt
        {
            AttemptID = StudyRepository.L(r, "AttemptID") ?? 0,
            ReceiptID = StudyRepository.S(r, "ReceiptID"),
            RequestedAt = StudyRepository.S(r, "RequestedAt"),
            FinishedAt = StudyRepository.S(r, "FinishedAt"),
            OperatorID = StudyRepository.S(r, "OperatorID"),
            PrinterID = StudyRepository.S(r, "PrinterID"),
            Outcome = StudyRepository.S(r, "Outcome"),
            BytesSent = (int?)StudyRepository.L(r, "BytesSent"),
            BytesTotal = (int?)StudyRepository.L(r, "BytesTotal"),
            IsCopy = StudyRepository.B(r, "IsCopy"),
            Authorization = StudyRepository.S(r, "Authorization"),
            ErrorDetail = StudyRepository.S(r, "ErrorDetail")
        };

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }
    }
}
