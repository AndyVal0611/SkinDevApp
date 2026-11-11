// ============================================================================
// RecordsQueries.cs  -  namespace SkinDevApp.Data
//
// Read-only views of the study database for the "Records & database" screen (patients, scans, AI findings, verification records,
// audit log), each with search and filters. Every query is fixed and parameterised here: the screen never runs SQL typed by a user.
// Changes go through the normal, audited repository methods (registration, verification status, linking a scan, reviews).
// ============================================================================

using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace SkinDevApp.Data
{
    public static class RecordsQueries
    {
        private static DataTable Table(string sql, params object[] args)
        {
            var t = new DataTable();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, sql, null, args))
            using (var a = new SQLiteDataAdapter(cmd))
                a.Fill(t);
            return t;
        }

        private static string Like(string text) => "%" + (text ?? "").Trim() + "%";

        public static DataTable Patients(string search, string status)
        {
            return Table(@"
                SELECT p.ParticipantID AS ""Patient ID"",
                       TRIM(COALESCE(p.LastName,'') || ', ' || COALESCE(p.FirstName,'') || ' ' || COALESCE(p.MiddleName,'')) AS ""Name"",
                       COALESCE(p.Sex,'') AS ""Sex"", COALESCE(CAST(p.Age AS TEXT),'') AS ""Age"",
                       substr(p.RegisteredAt,1,16) AS ""Registered"", p.Status AS ""Status"",
                       (SELECT COUNT(*) FROM ScanSessions s WHERE s.ParticipantID=p.ParticipantID) AS ""Scans"",
                       (SELECT COUNT(*) FROM ScanSessions s WHERE s.ParticipantID=p.ParticipantID AND COALESCE(s.ReviewStatus,'Pending verification')<>'Verified') AS ""Awaiting review"",
                       (SELECT substr(MAX(s.StartedAt),1,16) FROM ScanSessions s WHERE s.ParticipantID=p.ParticipantID) AS ""Last scan""
                FROM Participants p
                WHERE (@q = '%%' OR p.ParticipantID LIKE @q OR p.FirstName LIKE @q OR p.LastName LIKE @q OR p.MiddleName LIKE @q)
                  AND (@st = 'All' OR p.Status = @st)
                ORDER BY p.Seq DESC;", "@q", Like(search), "@st", status ?? "All");
        }

        public static DataTable Scans(string search, string review, string aiResult)
        {
            return Table(@"
                SELECT s.ScanSessionID AS ""_id"", COALESCE(s.SessionLabel, s.ScanSessionID) AS ""Scan"", COALESCE(s.ParticipantID,'Unassigned') AS ""Patient"",
                       substr(s.StartedAt,1,16) AS ""Date"", COALESCE(s.ReviewStatus,'Pending verification') AS ""Verification"",
                       COALESCE(s.AIResult,'Pending') AS ""AI result"", COALESCE(s.AITitle,'') AS ""AI summary"",
                       CASE s.ScanMode WHEN 'Single' THEN '1 photo' ELSE s.ViewsCompleted || ' of ' || s.ViewsExpected END AS ""Photos""
                FROM ScanSessions s
                WHERE (@q = '%%' OR s.SessionLabel LIKE @q OR s.ParticipantID LIKE @q OR s.ScanSessionID LIKE @q OR s.AITitle LIKE @q)
                  AND (@rv = 'All' OR COALESCE(s.ReviewStatus,'Pending verification') = @rv)
                  AND (@ai = 'All' OR COALESCE(s.AIResult,'Pending') = @ai)
                ORDER BY s.StartedAt DESC;", "@q", Like(search), "@rv", review ?? "All", "@ai", aiResult ?? "All");
        }

        public static DataTable Findings(string search, string condition, string status)
        {
            return Table(@"
                SELECT a.ScanSessionID AS ""_id"", COALESCE(s.SessionLabel, s.ScanSessionID) AS ""Scan"", COALESCE(s.ParticipantID,'Unassigned') AS ""Patient"",
                       CASE v.ViewType WHEN 'Single' THEN 'Photograph' WHEN 'Left' THEN 'Left side' WHEN 'Right' THEN 'Right side' ELSE v.ViewType END AS ""Photo"",
                       f.Condition AS ""Condition"", f.Status AS ""AI result"", COALESCE(f.Certainty,'') AS ""Certainty"",
                       COALESCE(NULLIF(f.Regions,''),'—') AS ""Facial regions"", a.Category AS ""Photo outcome"",
                       a.Origin AS ""Origin"", substr(a.AssessedAt,1,16) AS ""Assessed""
                FROM AIFindings f JOIN AIAssessments a ON a.CaptureID=f.CaptureID JOIN CaptureViews v ON v.CaptureID=a.CaptureID
                     JOIN ScanSessions s ON s.ScanSessionID=a.ScanSessionID
                WHERE (@q = '%%' OR s.SessionLabel LIKE @q OR s.ParticipantID LIKE @q OR f.Regions LIKE @q)
                  AND (@c = 'All' OR f.Condition = @c) AND (@st = 'All' OR f.Status = @st)
                ORDER BY s.StartedAt DESC, v.ViewType, f.Condition;", "@q", Like(search), "@c", condition ?? "All", "@st", status ?? "All");
        }

        public static DataTable Verifications(string search, string kind)
        {
            return Table(@"
                SELECT * FROM (
                    SELECT d.ScanSessionID AS ""_id"", 'Dermatologist' AS ""Reviewer type"", COALESCE(s.SessionLabel, s.ScanSessionID) AS ""Scan"", COALESCE(s.ParticipantID,'Unassigned') AS ""Patient"",
                           d.DermatologistID || COALESCE(' · ' || d.ValidatorName,'') AS ""Reviewer"", COALESCE(d.DermatologistAssessment,'—') AS ""Assessment"",
                           COALESCE(d.AgreementWithAI,'—') AS ""Agreement with AI"",
                           CASE d.ValidationStatus WHEN 'Completed' THEN 'Verified' WHEN 'Further review' THEN 'Requires further review' ELSE 'Draft' END AS ""Outcome"",
                           substr(d.ValidationDate,1,16) AS ""Date"", COALESCE(d.Notes,'') AS ""Notes""
                    FROM DermatologistValidations d JOIN ScanSessions s ON s.ScanSessionID=d.ScanSessionID
)
                WHERE (@q = '%%' OR ""Scan"" LIKE @q OR ""Patient"" LIKE @q OR ""Reviewer"" LIKE @q OR ""Assessment"" LIKE @q)
                  AND (@k = 'All' OR ""Reviewer type"" = @k)
                ORDER BY ""Date"" DESC;", "@q", Like(search), "@k", kind ?? "All");
        }

        public static DataTable Audit(string search)
        {
            return Table(@"
                SELECT substr(At,1,19) AS ""When"", COALESCE(Actor,'') AS ""User"", Action AS ""Action"", COALESCE(Entity,'') AS ""Record type"",
                       COALESCE(EntityID,'') AS ""Record"", COALESCE(Details,'') AS ""Details""
                FROM AuditLog
                WHERE (@q = '%%' OR Action LIKE @q OR EntityID LIKE @q OR Details LIKE @q OR Actor LIKE @q)
                ORDER BY AuditID DESC LIMIT 1000;", "@q", Like(search));
        }

        public static int Count(string sql)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, sql))
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        /// <summary>Active / Withdrawn. A withdrawn patient's scans are kept but left out of every statistic.</summary>
        public static void SetParticipantStatus(string participantId, string status, string reason)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var cmd = StudyRepository.Cmd(c, "UPDATE Participants SET Status=@s, UpdatedAt=@at WHERE ParticipantID=@p;", tx,
                    "@s", status, "@at", StudyRepository.Now(), "@p", participantId))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, "Patient status", "Participant", participantId, status + (string.IsNullOrWhiteSpace(reason) ? "" : " | " + reason));
                tx.Commit();
            }
        }

        /// <summary>Once a day, the first time the application starts: a copy of the database in Backups\; the newest 14 automatic copies are kept. Returns the file made, or null if today's exists.</summary>
        public static string AutoBackup()
        {
            string dir = DataLocation.BackupsDir;
            Directory.CreateDirectory(dir);
            string today = "lumyvue_db_auto_" + DateTime.Now.ToString("yyyyMMdd");
            if (Directory.GetFiles(dir, today + "*.sqlite").Length > 0) return null;
            string file = BackupNow("auto");
            var old = new DirectoryInfo(dir).GetFiles("lumyvue_db_auto_*.sqlite");
            Array.Sort(old, (a, b) => b.Name.CompareTo(a.Name));
            for (int i = 14; i < old.Length; i++) try { old[i].Delete(); } catch { }
            return file;
        }

        /// <summary>The newest backup file name and time, or null.</summary>
        public static string LastBackupText()
        {
            string dir = DataLocation.BackupsDir;
            if (!Directory.Exists(dir)) return null;
            var files = new DirectoryInfo(dir).GetFiles("lumyvue_db_*.sqlite");
            if (files.Length == 0) return null;
            Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            return files[0].LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>A consistent copy of the whole database (SQLite online backup) in Backups\. Returns the file written.</summary>
        public static string BackupNow(string kind = "manual")
        {
            string dir = DataLocation.BackupsDir;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "lumyvue_db_" + (kind == "auto" ? "auto_" : "") + DateTime.Now.ToString(kind == "auto" ? "yyyyMMdd_HHmmss" : "yyyyMMdd_HHmmss") + ".sqlite");
            using (var src = StudyDatabase.Open())
            using (var dst = new SQLiteConnection("Data Source=" + file + ";Version=3;"))
            {
                dst.Open();
                src.BackupDatabase(dst, "main", "main", -1, null, 0);
            }
            StudyRepository.Audit(kind == "auto" ? "Automatic database backup" : "Database backup", "Database", null, file);
            return file;
        }
    }
}
