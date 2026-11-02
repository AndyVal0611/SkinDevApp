// ============================================================================
// V6SessionLoader.cs  -  namespace SkinDevApp.V6
//
// Reads the saved V6 assessments of one scan session (v6_assessment.json in each photograph's record folder). Used by the Results page.
// The single gate is V6FeatureFlag: with the flag OFF this returns null and the page behaves exactly as before; a scan saved without V6 files
// also returns null, so older scans keep showing the output of the model that produced them. Pure: no WPF.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SkinDevApp.Data;

namespace SkinDevApp.V6
{
    public static class V6SessionLoader
    {
        /// <summary>Saved V6 assessments by view type (Single / Front / Left / Right), or null when the flag is off or the scan has none.</summary>
        public static Dictionary<string, UnifiedAssessment> Load(SessionDetail d, out List<string> problems)
        {
            problems = new List<string>();
            if (!V6FeatureFlag.IsEnabled || d == null) return null;
            var map = new Dictionary<string, UnifiedAssessment>(StringComparer.OrdinalIgnoreCase);
            foreach (CaptureViewRow v in d.Views)
            {
                string why;
                UnifiedAssessment a = V6Archive.Read(v.RecordFolder, out why);
                if (a != null) map[v.ViewType ?? "Single"] = a;
                else if (!string.IsNullOrEmpty(v.RecordFolder) && File.Exists(Path.Combine(v.RecordFolder, V6Archive.FileName))) problems.Add((v.ViewType ?? "Photograph") + ": " + why);
            }
            return map.Count > 0 ? map : null;
        }

        public static string MarkedAreasPath(CaptureViewRow v)
        {
            string p = v == null || string.IsNullOrEmpty(v.RecordFolder) ? null : Path.Combine(v.RecordFolder, V6Archive.MarkedFileName);
            return p != null && File.Exists(p) ? p : null;
        }
    }
}
