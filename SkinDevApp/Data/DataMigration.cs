// ============================================================================
// DataMigration.cs  -  namespace SkinDevApp.Data
//
// Older LUMYVUE versions kept the database BESIDE THE EXE (lumyvue_db.sqlite next to SkinDevApp.exe) and the scan files in
// %LOCALAPPDATA%\LUMYVUE. That is why replacing or moving the exe folder made the application look like a new installation.
// Now everything lives in C:\ProgramData\LUMYVUE (see DataLocation). This file brings an existing older installation over, safely:
//
//   * it runs only when the permanent database does NOT exist yet, and never touches an existing permanent database;
//   * the old database is read with the SQLite online-backup API into a temporary file, checked (integrity_check and the row count of every
//     table), and only then moved into place; the old database and the old scan folders are never changed or deleted;
//   * the scan files are COPIED (never moved) and every copy is verified; the stored file paths are rewritten in the NEW database only;
//   * an empty old database (no account, no patient, no scan) is not migrated: that is simply a first installation;
//   * if anything fails, the temporary file is removed, no empty database is created, and the problem is reported.
//
// It also holds the internal restore of a backup (SkinDevApp.exe --restore-db <backup> --confirm), see DatabaseRestore.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SkinDevApp.Data
{
    public sealed class MigrationResult
    {
        public bool Migrated { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public override string ToString() => Message;
    }

    public static class DataMigration
    {
        private const string MutexName = @"Global\LUMYVUE_DataMigration";

        /// <summary>The result of this start-up's migration (null when none was needed).</summary>
        public static MigrationResult Last { get; private set; }

        /// <summary>
        /// Called when the permanent database does not exist yet. Returns when it is safe to create a fresh database (nothing to migrate) or after the
        /// old data has been brought over. THROWS when an old database with data exists but could not be migrated safely: the caller must stop.
        /// </summary>
        public static void EnsureDatabase()
        {
            if (DataLocation.IsShared) return;                                   // a shared folder has its own (older) rule in DataLocation
            bool owned = false;
            using (var mutex = new Mutex(false, MutexName))
            {
                try { owned = mutex.WaitOne(TimeSpan.FromSeconds(60)); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new IOException("Another LUMYVUE window is setting up the database. Close it and start LUMYVUE again.");
                try
                {
                    if (File.Exists(DataLocation.DbFile)) return;                // someone else finished first: never touch an existing database
                    Candidate best = FindBest();
                    if (best == null) { Last = new MigrationResult { Message = "First installation: no older data was found." }; Log("First installation: no older database with data was found."); return; }
                    Last = Migrate(best);
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }

        // ---------------------------------------------------------------- finding the old database

        private sealed class Candidate
        {
            public string Path;
            public long Score;
            public string Describe() => Path + " (" + Score + " records)";
        }

        private static Candidate FindBest()
        {
            var paths = new List<string> { DataLocation.LegacyDbFile };
            try
            {
                // development layout: bin\Debug and bin\Release are siblings that could each hold their own old database
                string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent != null && string.Equals(parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
                    foreach (DirectoryInfo sib in parent.GetDirectories())
                        if (File.Exists(System.IO.Path.Combine(sib.FullName, "SkinDevApp.exe")))
                            paths.Add(System.IO.Path.Combine(sib.FullName, "lumyvue_db.sqlite"));
            }
            catch { }

            Candidate best = null;
            foreach (string p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(p)) continue;
                long score = Score(p);
                if (score < 0)
                    throw new IOException("An older LUMYVUE database was found at " + p + " but it could not be read, so nothing was migrated and no new database was created. " +
                                          "The older database was not changed.");
                if (score > 0 && (best == null || score > best.Score)) best = new Candidate { Path = p, Score = score };
            }
            return best;
        }

        /// <summary>Accounts + patients + scans in an old database (0 = empty, -1 = cannot be read).</summary>
        private static long Score(string path)
        {
            try
            {
                using (var c = new SQLiteConnection("Data Source=" + path + ";Version=3;Read Only=True;"))
                {
                    c.Open();
                    long total = 0;
                    foreach (string t in new[] { "ResearcherAccounts", "Participants", "ScanSessions" })
                        total += CountOrZero(c, t);
                    return total;
                }
            }
            catch { return -1; }
        }

        private static long CountOrZero(SQLiteConnection c, string table)
        {
            try { using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM \"" + table + "\";", c)) return Convert.ToInt64(cmd.ExecuteScalar()); }
            catch (SQLiteException) { return 0; }                                 // the table does not exist (very old or empty database)
        }

        // ---------------------------------------------------------------- the migration

        private static MigrationResult Migrate(Candidate src)
        {
            string target = DataLocation.DbFile;
            string temp = target + ".migrating";
            var report = new StringBuilder();
            try
            {
                Log("Migrating the older database " + src.Describe() + " to " + target);
                if (File.Exists(temp)) File.Delete(temp);                         // a leftover of an interrupted earlier attempt (never a finished database)

                // 1. consistent copy with the SQLite online-backup API (the source is opened read-only)
                using (var from = new SQLiteConnection("Data Source=" + src.Path + ";Version=3;Read Only=True;"))
                using (var to = new SQLiteConnection("Data Source=" + temp + ";Version=3;"))
                {
                    from.Open(); to.Open();
                    from.BackupDatabase(to, "main", "main", -1, null, 0);
                }

                // 2. verify it: integrity and the row count of every table
                using (var from = new SQLiteConnection("Data Source=" + src.Path + ";Version=3;Read Only=True;"))
                using (var to = new SQLiteConnection("Data Source=" + temp + ";Version=3;"))
                {
                    from.Open(); to.Open();
                    string integrity;
                    using (var cmd = new SQLiteCommand("PRAGMA integrity_check;", to)) integrity = Convert.ToString(cmd.ExecuteScalar());
                    if (integrity != "ok") throw new IOException("The copied database failed its integrity check (" + integrity + ").");
                    foreach (string t in Tables(from))
                    {
                        long a = CountOrZero(from, t), b = CountOrZero(to, t);
                        if (a != b) throw new IOException("The copied database does not match the original (table " + t + ": " + a + " rows, copy " + b + ").");
                        if (a > 0) report.Append(t).Append('=').Append(a).Append(' ');
                    }
                }

                // 3. the scan files: copy (never move) and verify every file
                string oldRoot = DataLocation.LegacyUserRoot;
                int files = 0;
                var rewrites = new List<Tuple<string, string>>();
                foreach (string folder in new[] { "Patient Records", "Captures" })
                {
                    string from = System.IO.Path.Combine(oldRoot, folder), to = System.IO.Path.Combine(DataLocation.DataRoot, folder);
                    if (!Directory.Exists(from)) continue;
                    if (string.Equals(System.IO.Path.GetFullPath(from).TrimEnd('\\'), System.IO.Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    files += CopyVerified(from, to);
                    rewrites.Add(Tuple.Create(from, to));
                }

                // 4. point the NEW database at the new file locations (the original database keeps its paths)
                if (rewrites.Count > 0)
                    using (var to = new SQLiteConnection("Data Source=" + temp + ";Version=3;"))
                    {
                        to.Open();
                        using (var tx = to.BeginTransaction())
                        {
                            foreach (var rw in rewrites) RewritePaths(to, tx, rw.Item1, rw.Item2);
                            tx.Commit();
                        }
                    }

                // 5. only now does the new database take its place
                File.Move(temp, target);
                string message = "Older data was brought over from " + src.Path + " (" + report.ToString().Trim() + "; " + files + " scan files copied). " +
                                 "The older database and the older scan folders were left untouched.";
                Log(message);
                return new MigrationResult { Migrated = true, Source = src.Path, Message = message };
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                string message = "The older data could not be migrated safely: " + ex.Message + " Nothing was changed and no new database was created. " +
                                 "The older database is still at " + src.Path + ".";
                Log("FAILED: " + message);
                throw new IOException(message, ex);
            }
        }

        private static List<string> Tables(SQLiteConnection c)
        {
            var list = new List<string>();
            using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", c))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

        /// <summary>Copies a folder tree; a file that already exists with the same size is kept. Every file is compared afterwards. Returns the file count.</summary>
        private static int CopyVerified(string from, string to)
        {
            int n = 0;
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(from.TrimEnd('\\').Length).TrimStart('\\');
                string dest = System.IO.Path.Combine(to, rel);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                var fi = new FileInfo(file);
                if (!File.Exists(dest) || new FileInfo(dest).Length != fi.Length) File.Copy(file, dest, true);
                if (!File.Exists(dest) || new FileInfo(dest).Length != fi.Length) throw new IOException("The scan file " + rel + " was not copied correctly.");
                n++;
            }
            return n;
        }

        /// <summary>Replaces the old folder path by the new one in every text column of the NEW database (file paths, and paths inside stored JSON).</summary>
        private static void RewritePaths(SQLiteConnection c, SQLiteTransaction tx, string oldPrefix, string newPrefix)
        {
            foreach (string table in Tables(c))
            {
                var cols = new List<string>();
                using (var cmd = new SQLiteCommand("PRAGMA table_info(\"" + table + "\");", c, tx))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string type = (r.IsDBNull(2) ? "" : r.GetString(2)).ToUpperInvariant();
                        if (type.Contains("CHAR") || type.Contains("TEXT") || type.Contains("CLOB") || type == "") cols.Add(r.GetString(1));
                    }
                foreach (string col in cols)
                    using (var cmd = new SQLiteCommand("UPDATE \"" + table + "\" SET \"" + col + "\" = REPLACE(\"" + col + "\", @o, @n) WHERE typeof(\"" + col + "\") = 'text' AND instr(\"" + col + "\", @o) > 0;", c, tx))
                    {
                        cmd.Parameters.AddWithValue("@o", oldPrefix);
                        cmd.Parameters.AddWithValue("@n", newPrefix);
                        cmd.ExecuteNonQuery();
                    }
            }
        }

        // ---------------------------------------------------------------- log

        public static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(DataLocation.LogsDir);
                File.AppendAllText(System.IO.Path.Combine(DataLocation.LogsDir, "database.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }
    }

    // ================================================================================================================================
    //  Restore of a backup (internal; no screen). Run from a command prompt while LUMYVUE is closed:
    //      SkinDevApp.exe --restore-db "<backup file>"             shows what would happen and changes nothing
    //      SkinDevApp.exe --restore-db "<backup file>" --confirm   replaces the active database by the backup
    //  The active database is first saved (verified) in the Backups folder, so the restore can always be undone. Nothing is ever restored
    //  automatically.
    // ================================================================================================================================
    public static class DatabaseRestore
    {
        public static bool TryRunFromArgs(string[] args)
        {
            if (args == null) return false;
            int at = Array.FindIndex(args, a => string.Equals(a, "--restore-db", StringComparison.OrdinalIgnoreCase));
            if (at < 0) return false;
            string backup = at + 1 < args.Length ? args[at + 1] : null;
            bool confirm = args.Any(a => string.Equals(a, "--confirm", StringComparison.OrdinalIgnoreCase));
            string text;
            try { text = Run(backup, confirm); }
            catch (Exception ex) { text = "The restore was NOT done and nothing was changed.\n\n" + ex.Message; }
            DataMigration.Log("restore: " + text.Replace("\n", " "));
            try { File.WriteAllText(System.IO.Path.Combine(DataLocation.LogsDir, "restore_result.txt"), text); } catch { }
            bool quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));
            if (!quiet) { try { System.Windows.MessageBox.Show(text, "LUMYVUE database restore"); } catch { } }
            return true;
        }

        public static string Run(string backup, bool confirm)
        {
            if (string.IsNullOrEmpty(backup) || !File.Exists(backup)) throw new FileNotFoundException("Usage: SkinDevApp.exe --restore-db \"<backup file>\" [--confirm]. The backup file was not found.", backup);
            string active = DataLocation.DbFile;
            string self = Process.GetCurrentProcess().ProcessName;
            if (Process.GetProcessesByName(self).Length > 1) throw new InvalidOperationException("LUMYVUE is open. Close it completely before restoring a backup.");

            string backupStats = Stats(backup);
            if (backupStats == null) throw new InvalidDataException("The backup file is not a valid LUMYVUE database (it failed the integrity check or has no account or patient tables).");
            string activeStats = File.Exists(active) ? Stats(active) : "no active database";
            string summary = "Backup: " + backupStats + "\nActive database: " + activeStats + "\nActive file: " + active;
            if (!confirm) return summary + "\n\nNothing was changed. Run the same command with --confirm to replace the active database by this backup. The active database is saved first.";

            Directory.CreateDirectory(DataLocation.DatabaseDir);
            Directory.CreateDirectory(DataLocation.BackupsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string saved = null;
            if (File.Exists(active))
            {
                saved = System.IO.Path.Combine(DataLocation.BackupsDir, "lumyvue_db_before_restore_" + stamp + ".sqlite");
                CopyDatabase(active, saved, false);
                if (Stats(saved) == null) throw new IOException("The safety copy of the active database could not be verified, so nothing was restored.");
            }
            string temp = active + ".restoring";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                CopyDatabase(backup, temp, true);
                if (Stats(temp) == null) throw new IOException("The restored copy failed its integrity check.");
                string old = active + ".replaced_" + stamp;
                if (File.Exists(active)) File.Move(active, old);
                try { File.Move(temp, active); }
                catch { if (File.Exists(old) && !File.Exists(active)) File.Move(old, active); throw; }     // put the original back
                if (File.Exists(old)) File.Move(old, System.IO.Path.Combine(DataLocation.BackupsDir, "lumyvue_db_replaced_" + stamp + ".sqlite"));
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
            return "The backup was restored.\n\n" + summary + (saved == null ? "" : "\n\nThe previous database was kept as " + saved + ".");
        }

        private static void CopyDatabase(string from, string to, bool readOnlySource)
        {
            using (var s = new SQLiteConnection("Data Source=" + from + ";Version=3;" + (readOnlySource ? "Read Only=True;" : "")))
            using (var d = new SQLiteConnection("Data Source=" + to + ";Version=3;"))
            {
                s.Open(); d.Open();
                s.BackupDatabase(d, "main", "main", -1, null, 0);
            }
        }

        /// <summary>"3 accounts, 12 patients, 40 scans, last activity ..." or null when the file is not a usable LUMYVUE database.</summary>
        private static string Stats(string path)
        {
            try
            {
                using (var c = new SQLiteConnection("Data Source=" + path + ";Version=3;Read Only=True;"))
                {
                    c.Open();
                    string integrity;
                    using (var cmd = new SQLiteCommand("PRAGMA integrity_check;", c)) integrity = Convert.ToString(cmd.ExecuteScalar());
                    if (integrity != "ok") return null;
                    long a = One(c, "SELECT COUNT(*) FROM ResearcherAccounts;"), p = One(c, "SELECT COUNT(*) FROM Participants;"), s = One(c, "SELECT COUNT(*) FROM ScanSessions;");
                    string last = "";
                    try { using (var cmd = new SQLiteCommand("SELECT MAX(At) FROM AuditLog;", c)) last = Convert.ToString(cmd.ExecuteScalar()); } catch { }
                    return a + " accounts, " + p + " patients, " + s + " scans" + (string.IsNullOrEmpty(last) ? "" : ", last activity " + last);
                }
            }
            catch { return null; }
        }

        private static long One(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c)) return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }
}
