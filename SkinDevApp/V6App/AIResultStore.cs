// ============================================================================
// AIResultStore.cs  -  namespace SkinDevApp.V6
//
// Puts the final model's result of every photograph into the study database (AIAssessments, AIFindings, AIMarkedAreas) and keeps
// the scan-level summary on ScanSessions (AIResult, AITitle, AIConditions, AIModel). The sealed v6_assessment.json next to the
// photograph stays the source of truth; the database rows are its searchable copy, written once per photograph and never edited.
//
// Origin "Scan" = assessed when the photograph was taken; "Re-analysis" = an older photograph (taken with the earlier model)
// assessed later by the final model from its saved image. Both are labelled as such wherever they are shown.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkinDevApp.Data;
using SkinDevApp.Scanning;

namespace SkinDevApp.V6
{
    public static class AIResultStore
    {
        public const string ModelName = "LUMYVUE final model (V6)";
        public const string OriginScan = "Scan", OriginReanalysis = "Re-analysis";

        public static string ModelLabel(string variant) => ModelName + (variant == "full" ? " · full fusion" : " · Global + YOLO fusion");

        /// <summary>Face box of a photograph (normalised x, y, w, h) from its record.json, or null.</summary>
        public static double[] FaceBoxOf(string recordFolder)
        {
            try
            {
                CaptureRecord rec = string.IsNullOrEmpty(recordFolder) ? null : ReviewStore.LoadRecord(recordFolder);
                double[] f = rec != null && rec.Quality != null && rec.Quality.FaceFound ? rec.Quality.FaceBox : null;
                return f != null && f.Length == 4 && f[2] > 0 && f[3] > 0 ? f : null;
            }
            catch { return null; }
        }

        /// <summary>Stores one photograph's assessment (replacing nothing: an existing row for the photograph is kept).</summary>
        public static void Store(string scanSessionId, string captureId, string recordFolder, UnifiedAssessment a, string origin)
        {
            if (a == null || string.IsNullOrEmpty(captureId)) return;
            double[] face = FaceBoxOf(recordFolder);
            ClinicalSummary s = ClinicalSummary.From(a, face);
            string file = string.IsNullOrEmpty(recordFolder) ? null : Path.Combine(recordFolder, V6Archive.FileName);
            string sha = null;
            try { if (file != null && File.Exists(file)) using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(file))) sha = d.RootElement.TryGetProperty("ai_content_sha256", out JsonElement e) ? e.ToString() : null; } catch { }

            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var chk = StudyRepository.Cmd(c, "SELECT COUNT(*) FROM AIAssessments WHERE CaptureID=@c;", tx, "@c", captureId))
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) { tx.Commit(); return; }
                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO AIAssessments(CaptureID, ScanSessionID, ModelName, Variant, RunId, State, Category, Title, Explanation, ReviewReasons,
                        Origin, AssessedAt, AssessmentFile, ContentSha256, ModelHashes, FaceBox)
                        VALUES (@c,@s,@m,@v,@run,@st,@cat,@t,@e,@rr,@o,@at,@f,@sha,@mh,@fb);", tx,
                    "@c", captureId, "@s", scanSessionId, "@m", ModelName, "@v", a.Variant, "@run", a.RunId, "@st", a.State, "@cat", s.CategoryLabel,
                    "@t", s.Title, "@e", s.Explanation, "@rr", JsonSerializer.Serialize(s.ReviewReasons), "@o", origin, "@at", StudyRepository.Now(),
                    "@f", file, "@sha", sha, "@mh", JsonSerializer.Serialize(a.ModelSha256), "@fb", face == null ? null : JsonSerializer.Serialize(face)))
                    cmd.ExecuteNonQuery();
                foreach (ClinicalFinding f in s.Findings)
                {
                    ConditionAssessment ca = a.Conditions.FirstOrDefault(x => x.Condition == f.Condition);
                    using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO AIFindings(CaptureID, Condition, Status, Certainty, Probability, Threshold, DetectorMarked, Regions)
                            VALUES (@c,@cond,@st,@ce,@p,@t,@d,@r);", tx,
                        "@c", captureId, "@cond", f.Condition, "@st", f.Status, "@ce", f.Certainty, "@p", f.Probability, "@t", f.Threshold,
                        "@d", ca != null && ca.DetectorFired ? 1 : 0, "@r", f.RegionsText))
                        cmd.ExecuteNonQuery();
                }
                if (a.FrameWidth > 0 && a.FrameHeight > 0)
                    foreach (V6Box b in a.Boxes)
                    {
                        double cx = (b.X0 + b.X1) / 2.0 / a.FrameWidth, cy = (b.Y0 + b.Y1) / 2.0 / a.FrameHeight;
                        using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO AIMarkedAreas(CaptureID, Condition, Confidence, NX0, NY0, NX1, NY1, FacialRegion)
                                VALUES (@c,@cond,@conf,@x0,@y0,@x1,@y1,@reg);", tx,
                            "@c", captureId, "@cond", b.Condition, "@conf", (double)b.Confidence, "@x0", b.X0 / a.FrameWidth, "@y0", b.Y0 / a.FrameHeight,
                            "@x1", b.X1 / a.FrameWidth, "@y1", b.Y1 / a.FrameHeight, "@reg", ClinicalSummary.RegionOf(cx, cy, face)))
                            cmd.ExecuteNonQuery();
                    }
                StudyRepository.Audit(c, tx, origin == OriginReanalysis ? "AI re-analysis stored" : "AI result stored", "CaptureView", captureId, s.CategoryLabel + " | " + s.Title);
                tx.Commit();
            }
        }

        /// <summary>Reads every photograph of a scan that has a v6_assessment.json but no database row, then refreshes the scan summary.</summary>
        public static void SyncSession(string scanSessionId)
        {
            foreach (var v in ViewsOf(scanSessionId))
            {
                if (v.HasRow || string.IsNullOrEmpty(v.Folder)) continue;
                UnifiedAssessment a = V6Archive.Read(v.Folder, out string _);
                if (a != null) Store(scanSessionId, v.CaptureId, v.Folder, a, OriginScan);
            }
            RefreshSession(scanSessionId);
        }

        internal sealed class ViewRef { public string CaptureId, ViewType, Folder, Trigger, Analysed, Original; public bool HasRow; }

        internal static List<ViewRef> ViewsOf(string scanSessionId)
        {
            var list = new List<ViewRef>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, @"SELECT v.CaptureID, v.ViewType, v.RecordFolder, v.Trigger, v.AnalysedImagePath, v.OriginalImagePath,
                    EXISTS(SELECT 1 FROM AIAssessments a WHERE a.CaptureID=v.CaptureID) AS HasRow
                    FROM CaptureViews v WHERE v.ScanSessionID=@s ORDER BY CASE v.ViewType WHEN 'Front' THEN 0 WHEN 'Left' THEN 1 WHEN 'Right' THEN 2 ELSE 3 END;", null, "@s", scanSessionId))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new ViewRef
                    {
                        CaptureId = StudyRepository.S(r, "CaptureID"), ViewType = StudyRepository.S(r, "ViewType"), Folder = DataLocation.Rebase(StudyRepository.S(r, "RecordFolder")),
                        Trigger = StudyRepository.S(r, "Trigger"), Analysed = DataLocation.Rebase(StudyRepository.S(r, "AnalysedImagePath")),
                        Original = DataLocation.Rebase(StudyRepository.S(r, "OriginalImagePath")), HasRow = StudyRepository.B(r, "HasRow")
                    });
            return list;
        }

        // ------------------------------------------------------------------ reading back

        public sealed class StoredView
        {
            public string CaptureId, ViewType, Origin, AssessedAt, Variant, Folder;
            public ClinicalSummary Summary;
        }

        /// <summary>The stored final-model results of a scan, one per photograph (empty when the scan has not been assessed yet).</summary>
        public static List<StoredView> Load(string scanSessionId)
        {
            var list = new List<StoredView>();
            using (var c = StudyDatabase.Open())
            {
                using (var cmd = StudyRepository.Cmd(c, @"SELECT a.*, v.ViewType, v.RecordFolder FROM AIAssessments a JOIN CaptureViews v ON v.CaptureID=a.CaptureID
                        WHERE a.ScanSessionID=@s ORDER BY CASE v.ViewType WHEN 'Front' THEN 0 WHEN 'Left' THEN 1 WHEN 'Right' THEN 2 ELSE 3 END;", null, "@s", scanSessionId))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        var s = new ClinicalSummary
                        {
                            Category = ClinicalSummary.Parse(StudyRepository.S(r, "Category")), Title = StudyRepository.S(r, "Title"), Explanation = StudyRepository.S(r, "Explanation")
                        };
                        try { s.ReviewReasons = JsonSerializer.Deserialize<List<string>>(StudyRepository.S(r, "ReviewReasons") ?? "[]") ?? new List<string>(); } catch { }
                        list.Add(new StoredView
                        {
                            CaptureId = StudyRepository.S(r, "CaptureID"), ViewType = StudyRepository.S(r, "ViewType"), Origin = StudyRepository.S(r, "Origin"),
                            AssessedAt = StudyRepository.S(r, "AssessedAt"), Variant = StudyRepository.S(r, "Variant"), Folder = DataLocation.Rebase(StudyRepository.S(r, "RecordFolder")), Summary = s
                        });
                    }
                foreach (StoredView v in list)
                    using (var cmd = StudyRepository.Cmd(c, "SELECT * FROM AIFindings WHERE CaptureID=@c;", null, "@c", v.CaptureId))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            var f = new ClinicalFinding
                            {
                                Condition = StudyRepository.S(r, "Condition"), Status = StudyRepository.S(r, "Status"), Certainty = StudyRepository.S(r, "Certainty"),
                                Probability = StudyRepository.D(r, "Probability") ?? 0, Threshold = StudyRepository.D(r, "Threshold") ?? 0
                            };
                            f.NeedsReview = f.Status == ClinicalSummary.Possible || f.Status == ClinicalSummary.Inconclusive;
                            string regions = StudyRepository.S(r, "Regions");
                            if (!string.IsNullOrEmpty(regions)) f.Regions = regions.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            v.Summary.Findings.Add(f);
                        }
                foreach (StoredView v in list)
                    v.Summary.Findings = v.Summary.Findings.OrderBy(f => V6Conditions.IndexOf(f.Condition)).ToList();
            }
            return list;
        }

        public static string ViewLabel(string viewType) =>
            string.IsNullOrEmpty(viewType) || viewType == "Single" ? "Photograph" : viewType == "Left" ? "Left side" : viewType == "Right" ? "Right side" : viewType;

        /// <summary>The scan-level reading (one photograph: its own; several: combined).</summary>
        public static ClinicalSummary ScanSummary(List<StoredView> views) =>
            ClinicalSummary.ForScan(views.Select(v => new KeyValuePair<string, ClinicalSummary>(ViewLabel(v.ViewType), v.Summary)).ToList());

        /// <summary>Recomputes ScanSessions.AIResult / AITitle / AIConditions / AIModel from the stored rows.</summary>
        public static void RefreshSession(string scanSessionId)
        {
            List<StoredView> views = Load(scanSessionId);
            ClinicalSummary s = views.Count == 0 ? null : ScanSummary(views);
            string variant = views.Select(v => v.Variant).FirstOrDefault(x => x != null);
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "UPDATE ScanSessions SET AIResult=@r, AITitle=@t, AIConditions=@c, AIModel=@m, AIUpdatedAt=@at WHERE ScanSessionID=@id;", null,
                "@r", s?.CategoryLabel, "@t", s?.Title, "@c", s == null ? null : string.Join(", ", s.DetectedFindings.Select(f => f.Condition)),
                "@m", s == null ? null : ModelLabel(variant) + (views.Any(v => v.Origin == OriginReanalysis) ? " (re-analysed)" : ""), "@at", StudyRepository.Now(), "@id", scanSessionId))
                cmd.ExecuteNonQuery();
        }

        /// <summary>Scans that have at least one photograph without a final-model result.</summary>
        public static List<string> SessionsMissingResults()
        {
            var list = new List<string>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, @"SELECT DISTINCT v.ScanSessionID FROM CaptureViews v JOIN ScanSessions s ON s.ScanSessionID=v.ScanSessionID
                    WHERE s.Finalized=1 AND NOT EXISTS (SELECT 1 FROM AIAssessments a WHERE a.CaptureID=v.CaptureID);"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(r.GetString(0));
            return list;
        }
    }
}
