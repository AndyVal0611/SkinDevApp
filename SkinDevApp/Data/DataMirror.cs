using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;

namespace SkinDevApp.Data
{
    /// <summary>
    /// A second, plain copy of everything that was gathered, in an ordinary folder the person can open in File Explorer:
    ///     Documents\LUMYVUE Data\Database           lumyvue_db_latest.sqlite + one verified copy per day
    ///     Documents\LUMYVUE Data\Patient Records    every scan image, analysis and review file
    ///     Documents\LUMYVUE Data\Reports            the A4 report PDFs
    ///     Documents\LUMYVUE Data\Captures           raw captures
    ///     Documents\LUMYVUE Data\Receipts           receipt files (when the receipt is simulated)
    /// It is copy-only: files are added or refreshed, NEVER deleted or moved, and the working data is never modified.
    /// It runs at start-up, every 10 minutes, when the application closes and on "Back up now". A failure is written to
    /// Logs\database.log and never stops the application.
    /// </summary>
    public static class DataMirror
    {
        private static readonly object Gate = new object();

        /// <summary>The folder of the copy (LUMYVUE_MIRROR_DIR overrides it, for tests).</summary>
        public static string MirrorRoot
        {
            get
            {
                string e = Environment.GetEnvironmentVariable("LUMYVUE_MIRROR_DIR");
                if (!string.IsNullOrWhiteSpace(e)) return e;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LUMYVUE Data");
            }
        }

        /// <summary>Copies what is new or changed. Returns a one-line summary; never throws.</summary>
        public static string Run()
        {
            if (!Monitor.TryEnter(Gate)) return "A copy is already running.";
            try
            {
                if (!StudyDatabase.IsReady) return "Database not available, nothing copied.";
                string root = MirrorRoot;
                string data = Path.GetFullPath(DataLocation.DataRoot).TrimEnd('\\') + "\\";
                if ((Path.GetFullPath(root).TrimEnd('\\') + "\\").StartsWith(data, StringComparison.OrdinalIgnoreCase)) return "The copy folder must be outside the data folder.";
                Directory.CreateDirectory(root);

                int db = CopyDatabase(root);
                int files = 0;
                files += CopyTree(DataLocation.RecordsDir, Path.Combine(root, "Patient Records"));
                files += CopyTree(DataLocation.ReportsDir, Path.Combine(root, "Reports"));
                files += CopyTree(DataLocation.CapturesDir, Path.Combine(root, "Captures"));
                try { files += CopyTree(SkinDevApp.Printing.ThermalPrinter.SimulationFolder, Path.Combine(root, "Receipts")); } catch { }

                string s = "Copy to " + root + ": database " + (db > 0 ? "saved" : "not saved") + ", " + files + " new or changed file(s).";
                if (files > 0 || db > 0) DataMigration.Log("mirror: " + s);
                return s;
            }
            catch (Exception ex)
            {
                DataMigration.Log("mirror FAILED: " + ex.Message);
                return "The copy could not be made: " + ex.Message;
            }
            finally { Monitor.Exit(Gate); }
        }

        /// <summary>Verified copy of the database through the SQLite online-backup API. A copy that fails the check never replaces the previous one.</summary>
        private static int CopyDatabase(string root)
        {
            string dir = Path.Combine(root, "Database");
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, "lumyvue_db_new.tmp");
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                using (var src = StudyDatabase.Open())
                using (var dst = new SQLiteConnection("Data Source=" + temp + ";Version=3;Pooling=False;"))
                {
                    dst.Open();
                    src.BackupDatabase(dst, "main", "main", -1, null, 0);
                    using (var cmd = new SQLiteCommand("PRAGMA integrity_check;", dst))
                        if (!string.Equals(Convert.ToString(cmd.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase)) throw new IOException("The copy failed its integrity check.");
                }
                SQLiteConnection.ClearAllPools();
                File.Copy(temp, Path.Combine(dir, "lumyvue_db_" + DateTime.Now.ToString("yyyyMMdd") + ".sqlite"), true);
                File.Copy(temp, Path.Combine(dir, "lumyvue_db_latest.sqlite"), true);
                return 1;
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        /// <summary>Adds or refreshes files (same name, size and time = skipped). Never deletes anything. Returns the number of files copied.</summary>
        private static int CopyTree(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || !Directory.Exists(from)) return 0;
            int n = 0;
            foreach (string f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string rel = f.Substring(from.TrimEnd('\\').Length).TrimStart('\\');
                    string dest = Path.Combine(to, rel);
                    var a = new FileInfo(f);
                    if (File.Exists(dest))
                    {
                        var b = new FileInfo(dest);
                        if (b.Length == a.Length && Math.Abs((b.LastWriteTimeUtc - a.LastWriteTimeUtc).TotalSeconds) < 2) continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(f, dest, true);
                    File.SetLastWriteTimeUtc(dest, a.LastWriteTimeUtc);
                    n++;
                }
                catch (Exception ex) { DataMigration.Log("mirror: could not copy " + f + ": " + ex.Message); }       // a locked file is picked up next time
            }
            return n;
        }
    }
}
