using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace SkinDevApp
{
    public class EvaluationRecord
    {
        public int RecordID { get; set; }
        public string PatientName { get; set; }
        public string Timestamp { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public string Confidence { get; set; }
    }

    public static class DatabaseHelper
    {
        public static string CurrentClientName { get; set; } = "Client Walk-In";
        public static string CurrentClientAge { get; set; } = "N/A";
        public static string CurrentClientContact { get; set; } = "N/A";

        private static string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lumyvue_db.sqlite");
        private static string connectionString = $"Data Source={dbPath};Version=3;";

        public static void InitializeDatabase()
        {
            bool isNewDb = !File.Exists(dbPath);

            if (isNewDb)
            {
                SQLiteConnection.CreateFile(dbPath);
            }

            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string tableQuery = @"
                    CREATE TABLE IF NOT EXISTS PatientLogs (
                        RecordID INTEGER PRIMARY KEY AUTOINCREMENT,
                        PatientName TEXT NOT NULL,
                        Timestamp TEXT NOT NULL,
                        PrimaryDiagnosis TEXT NOT NULL,
                        Confidence TEXT NOT NULL
                    );";

                using (var cmd = new SQLiteCommand(tableQuery, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void InsertRecord(string patientName, string diagnosis, string confidence)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string insertQuery = @"
                    INSERT INTO PatientLogs (PatientName, Timestamp, PrimaryDiagnosis, Confidence)
                    VALUES (@name, @time, @diag, @conf);";

                using (var cmd = new SQLiteCommand(insertQuery, conn))
                {
                    string displayName = string.IsNullOrWhiteSpace(patientName) ? "Client Walk-In" : patientName;

                    cmd.Parameters.AddWithValue("@name", displayName);
                    cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@diag", diagnosis);
                    cmd.Parameters.AddWithValue("@conf", confidence);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<EvaluationRecord> GetAllRecords()
        {
            var records = new List<EvaluationRecord>();
            if (!File.Exists(dbPath)) return records;

            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string selectQuery = "SELECT RecordID, PatientName, Timestamp, PrimaryDiagnosis, Confidence FROM PatientLogs ORDER BY RecordID DESC;";

                using (var cmd = new SQLiteCommand(selectQuery, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new EvaluationRecord
                        {
                            RecordID = Convert.ToInt32(reader["RecordID"]),
                            PatientName = reader["PatientName"].ToString(),
                            Timestamp = reader["Timestamp"].ToString(),
                            PrimaryDiagnosis = reader["PrimaryDiagnosis"].ToString(),
                            Confidence = reader["Confidence"].ToString()
                        });
                    }
                }
            }

            return records;
        }

        public static int GetTotalCount()
        {
            if (!File.Exists(dbPath)) return 0;

            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string countQuery = "SELECT COUNT(*) FROM PatientLogs;";
                using (var cmd = new SQLiteCommand(countQuery, conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public static int GetUniquePatientCount()
        {
            if (!File.Exists(dbPath)) return 0;

            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string countQuery = "SELECT COUNT(DISTINCT PatientName) FROM PatientLogs;";
                using (var cmd = new SQLiteCommand(countQuery, conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }
    }
}