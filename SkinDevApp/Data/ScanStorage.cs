// ============================================================================
// ScanStorage.cs  -  namespace SkinDevApp.Data
//
// One structured place for every scan's files (photographs, AI results, evidence maps, receipts, report exports, review records):
//
//   <data folder>\Patient Records\
//       Pending verification\<PatientID>\<yyyy-MM-dd>\<scan folder>\      new scans start here (captured straight into it)
//       Verified\<PatientID>\<yyyy-MM-dd>\<scan folder>\                   a dermatologist completed the review
//       Requires further review\<PatientID>\<yyyy-MM-dd>\<scan folder>\    the dermatologist asked for further review
//       Unassigned scans\<yyyy-MM-dd>\<scan folder>\                       scans not linked to a registered patient
//
// The folder always follows ScanSessions.ReviewStatus. When the status (or the patient link) changes, the scan folder is moved and
// every stored path of that scan is rewritten in ONE database transaction; if the database update fails the folder is moved back,
// and if the move is blocked (a file is open) nothing changes and the move is retried on the next start.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SkinDevApp.Data
{
    public static class ScanStorage
    {
        public const string UnassignedFolder = "Unassigned scans";
        private static readonly object Gate = new object();

        /// <summary>Where a NEW capture of this patient is written (status Pending verification); the caller adds the date and scan folder.</summary>
        public static string CaptureRoot(string participantId)
        {
            bool linked = IsParticipantId(participantId);
            string dir = linked ? Path.Combine(DataLocation.RecordsDir, ReviewStatus.Pending, participantId.Trim())
                                : Path.Combine(DataLocation.RecordsDir, UnassignedFolder);
            Directory.CreateDirectory(dir);
            EnsureReadme();
            return dir;
        }

        public static bool IsParticipantId(string id) =>
            !string.IsNullOrWhiteSpace(id) && System.Text.RegularExpressions.Regex.IsMatch(id.Trim(), @"^PS-\d{4,}$");

        // Every column that stores a path inside a scan folder.
        private static readonly string[][] PathColumns =
        {
            new[] { "ScanSessions", "SessionFolder" },
            new[] { "CaptureViews", "OriginalImagePath" }, new[] { "CaptureViews", "AnalysedImagePath" }, new[] { "CaptureViews", "RecordFolder" },
            new[] { "AnalysisImages", "Path" },
            new[] { "AttributionMaps", "RawMapPath" }, new[] { "AttributionMaps", "RenderedMapPath" }, new[] { "AttributionMaps", "OverlayPath" },
            new[] { "LocalizationRuns", "OverlayPath" }, new[] { "LocalizationRuns", "CombinedPath" },
            new[] { "AIAssessments", "AssessmentFile" }
        };

        /// <summary>The folder a scan belongs in for its current status and patient link.</summary>
        public static string TargetFolder(string participantId, string reviewStatus, string currentFolder, string startedAt)
        {
            string name = Path.GetFileName(currentFolder.TrimEnd('\\', '/'));
            string parent = Path.GetFileName(Path.GetDirectoryName(currentFolder.TrimEnd('\\', '/')) ?? "");
            string day = DateTime.TryParseExact(parent, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? parent
                       : DateTime.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime s) ? s.ToString("yyyy-MM-dd") : "undated";
            return IsParticipantId(participantId)
                ? Path.Combine(DataLocation.RecordsDir, ReviewStatus.Normalise(reviewStatus), participantId.Trim(), day, name)
                : Path.Combine(DataLocation.RecordsDir, UnassignedFolder, day, name);
        }

        /// <summary>Moves one scan's folder to where its status says it belongs. Returns the folder it is in afterwards (unchanged on failure).</summary>
        public static string Place(string scanSessionId)
        {
            lock (Gate)
            {
                string pid, status, folder, started;
                using (var c = StudyDatabase.Open())
                using (var cmd = StudyRepository.Cmd(c, "SELECT ParticipantID, ReviewStatus, SessionFolder, StartedAt FROM ScanSessions WHERE ScanSessionID=@id;", null, "@id", scanSessionId))
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    pid = StudyRepository.S(r, "ParticipantID"); status = StudyRepository.S(r, "ReviewStatus");
                    folder = StudyRepository.S(r, "SessionFolder"); started = StudyRepository.S(r, "StartedAt");
                }
                if (string.IsNullOrEmpty(folder)) return folder;
                string current = Directory.Exists(folder) ? folder : DataLocation.Rebase(folder);
                if (!Directory.Exists(current)) return folder;                               // files not on this PC: nothing to move

                string target = TargetFolder(pid, status, current, started);
                if (string.Equals(Path.GetFullPath(current).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(current, folder, StringComparison.OrdinalIgnoreCase)) Rewrite(scanSessionId, folder, current);
                    return current;
                }
                if (Directory.Exists(target)) target = target + "_" + scanSessionId.Substring(Math.Max(0, scanSessionId.Length - 6));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                try { Directory.Move(current, target); }
                catch (Exception ex)
                {
                    StudyRepository.Audit("Scan files not moved", "ScanSession", scanSessionId, "kept in " + current + " (" + ex.Message + "); retried at next start");
                    return current;
                }
                try { Rewrite(scanSessionId, folder, target); }
                catch (Exception ex)
                {
                    try { Directory.Move(target, current); } catch { }
                    StudyRepository.Audit("Scan files not moved", "ScanSession", scanSessionId, "database update failed: " + ex.Message);
                    return current;
                }
                RemoveEmptyParents(Path.GetDirectoryName(current));
                StudyRepository.Audit("Scan files moved", "ScanSession", scanSessionId, current + " -> " + target);
                EnsureReadme();
                return target;
            }
        }

        private static void Rewrite(string scanSessionId, string oldPrefix, string newPrefix)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                foreach (string[] tc in PathColumns)
                {
                    string scope = tc[0] == "ScanSessions" ? "ScanSessionID=@sid"
                                 : tc[0] == "CaptureViews" || tc[0] == "AIAssessments" ? "ScanSessionID=@sid"
                                 : "CaptureID IN (SELECT CaptureID FROM CaptureViews WHERE ScanSessionID=@sid)";
                    string sql = "UPDATE " + tc[0] + " SET " + tc[1] + " = @new || substr(" + tc[1] + ", length(@old) + 1) WHERE " + scope +
                                 " AND " + tc[1] + " IS NOT NULL AND lower(substr(" + tc[1] + ", 1, length(@old))) = lower(@old);";
                    using (var cmd = StudyRepository.Cmd(c, sql, tx, "@new", newPrefix, "@old", oldPrefix, "@sid", scanSessionId)) cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        private static void RemoveEmptyParents(string dir)
        {
            try
            {
                string stop = Path.GetFullPath(DataLocation.DataRoot).TrimEnd('\\');
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
                {
                    string full = Path.GetFullPath(dir).TrimEnd('\\');
                    if (full.Length <= stop.Length || !Directory.Exists(full) || Directory.EnumerateFileSystemEntries(full).Any()) return;
                    if (string.Equals(Path.GetFileName(full), "Captures", StringComparison.OrdinalIgnoreCase)) return;
                    if (DataLocation.RecordsDir.TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return;
                    if (ReviewStatus.All.Concat(new[] { UnassignedFolder }).Any(n => string.Equals(Path.Combine(DataLocation.RecordsDir, n).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) return;
                    Directory.Delete(full);
                    dir = Path.GetDirectoryName(full);
                }
            }
            catch { }
        }

        /// <summary>Start-up: put every scan in its place (first run moves the older Captures folders; later runs retry blocked moves). Returns how many moved.</summary>
        public static int PlaceAll()
        {
            var ids = new List<string>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT ScanSessionID, SessionFolder FROM ScanSessions WHERE Finalized=1;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) ids.Add(r.GetString(0));
            int moved = 0;
            foreach (string id in ids)
            {
                string before = null;
                using (var c = StudyDatabase.Open())
                using (var cmd = StudyRepository.Cmd(c, "SELECT SessionFolder FROM ScanSessions WHERE ScanSessionID=@id;", null, "@id", id))
                    before = Convert.ToString(cmd.ExecuteScalar());
                string after = Place(id);
                if (after != null && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) moved++;
            }
            if (moved > 0) StudyRepository.Audit("Scan storage organised", "ScanSessions", null, moved + " scan folder(s) placed under Patient Records");
            return moved;
        }

        /// <summary>Folders on disk (under Patient Records and the older Captures folder) that hold a scan: a session.json or a stand-alone record.json.</summary>
        public static IEnumerable<string> ScanFoldersOnDisk()
        {
            foreach (string root in new[] { DataLocation.RecordsDir, DataLocation.CapturesDir })
            {
                if (!Directory.Exists(root)) continue;
                var stack = new Stack<KeyValuePair<string, int>>();
                stack.Push(new KeyValuePair<string, int>(root, 0));
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    string[] subs;
                    try { subs = Directory.GetDirectories(cur.Key); } catch { continue; }
                    foreach (string d in subs)
                    {
                        if (File.Exists(Path.Combine(d, "session.json")) || File.Exists(Path.Combine(d, "record.json"))) yield return d;
                        else if (cur.Value < 4) stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1));
                    }
                }
            }
        }

        public static void EnsureReadme()
        {
            try
            {
                string dir = DataLocation.RecordsDir;
                Directory.CreateDirectory(dir);
                foreach (string n in ReviewStatus.All.Concat(new[] { UnassignedFolder })) Directory.CreateDirectory(Path.Combine(dir, n));
                string p = Path.Combine(dir, "README.txt");
                if (File.Exists(p)) return;
                File.WriteAllText(p,
                    "LUMYVUE patient records\r\n=======================\r\n\r\n" +
                    "Every scan has one folder: <status>\\<PatientID>\\<date>\\<scan folder>.\r\n\r\n" +
                    "  Pending verification     new scans, not yet reviewed by a dermatologist\r\n" +
                    "  Verified                 reviewed and completed by a dermatologist\r\n" +
                    "  Requires further review  the dermatologist asked for further review\r\n" +
                    "  Unassigned scans         scans not linked to a registered patient\r\n\r\n" +
                    "A scan folder holds the photographs (original.png, analysed.png), the final AI result (v6_assessment.json, sealed with SHA-256),\r\n" +
                    "the evidence maps (v6_*.png), and the subfolders Receipts, Reports and Verification.\r\n" +
                    "LUMYVUE moves the folder itself when the verification status changes. Do not rename or move folders by hand:\r\n" +
                    "the database stores where each file is. Use the Records & database screen in the app instead.\r\n");
            }
            catch { }
        }

        /// <summary>A subfolder of the scan folder (Receipts, Reports, Verification), created on demand; null when the scan has no folder on this PC.</summary>
        public static string ScanSubfolder(string sessionFolder, string name)
        {
            if (string.IsNullOrEmpty(sessionFolder)) return null;
            string f = Directory.Exists(sessionFolder) ? sessionFolder : DataLocation.Rebase(sessionFolder);
            if (!Directory.Exists(f)) return null;
            string sub = Path.Combine(f, name);
            Directory.CreateDirectory(sub);
            return sub;
        }
    }
}
