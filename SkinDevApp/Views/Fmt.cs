// Fmt.cs - one place for how dates, times and model names are shown to people.
//   stored text "2026-10-04 10:19:24"  ->  "Oct 4, 2026 · 10:19 AM"
//   run "facial_only"                  ->  "PrecisionSkinV1"
using System;
using System.Globalization;

namespace SkinDevApp.Views
{
    public static class Fmt
    {
        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        private static readonly string[] StoredFormats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };

        public static bool TryParse(string stored, out DateTime t)
        {
            t = default(DateTime);
            if (string.IsNullOrWhiteSpace(stored)) return false;
            return DateTime.TryParseExact(stored.Trim(), StoredFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)
                || DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
        }

        /// <summary>"Oct 4, 2026"</summary>
        public static string Date(string stored)
        {
            DateTime t;
            return TryParse(stored, out t) ? t.ToString("MMM d, yyyy", En) : (stored ?? "—");
        }

        /// <summary>"10:19 AM"</summary>
        public static string Time(string stored)
        {
            DateTime t;
            return TryParse(stored, out t) ? t.ToString("h:mm tt", En) : "";
        }

        /// <summary>"Oct 4, 2026 · 10:19 AM"</summary>
        public static string When(string stored)
        {
            DateTime t;
            if (!TryParse(stored, out t)) return string.IsNullOrWhiteSpace(stored) ? "—" : stored;
            return t.ToString("MMM d, yyyy", En) + " · " + t.ToString("h:mm tt", En);
        }

        /// <summary>"Oct 4, 2026 · 10:19 AM (took 20 s)". The end time is only used for the duration.</summary>
        public static string ScanWhen(string started, string completed)
        {
            string s = When(started);
            DateTime a, b;
            if (!TryParse(started, out a) || !TryParse(completed, out b) || b <= a) return s;
            TimeSpan d = b - a;
            if (d.TotalHours >= 1) return s;                     // a scan left open for hours: do not show a misleading duration
            string took = d.TotalMinutes >= 1 ? (int)d.TotalMinutes + " min " + d.Seconds + " s" : Math.Max(1, (int)Math.Round(d.TotalSeconds)) + " s";
            return s + "  (took " + took + ")";
        }

        /// <summary>The name people see for a model run. The training run "facial_only" is PrecisionSkinV1.</summary>
        public static string ModelName(string runName)
        {
            if (string.IsNullOrWhiteSpace(runName) || runName == "unknown") return "PrecisionSkin (unknown version)";
            if (string.Equals(runName, "facial_only", StringComparison.OrdinalIgnoreCase)) return "PrecisionSkinV1";
            return "PrecisionSkin (" + runName + ")";
        }
    }
}
