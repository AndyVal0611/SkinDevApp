// ============================================================================
// SessionImporter.cs  -  namespace SkinDevApp.Data
//
// Copies what the scan pipeline saved on disk (session.json + one record.json
// per view, written by MultiViewSession / CaptureArchive) into the study
// database. The files stay the primary evidence; the database indexes them.
//
//   * A session is re-imported while it is in progress (retakes replace a view).
//   * Once Finalized = 1 its AI rows are never changed again.
//   * Model identity comes from each record.json's own hashes, so a historical
//     scan keeps the model that actually produced it.
//   * Human assessments live in other tables and are never touched here.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkinDevApp.Scanning;

namespace SkinDevApp.Data
{
    public static class SessionImporter
    {
        private static readonly object Gate = new object();

        // ------------------------------------------------------------- public --

        /// <summary>Import (or refresh) a Front/Left/Right session folder. Returns the ScanSessionID.</summary>
        public static string ImportSession(string sessionFolder, string participantId, bool finalize)
        {
            SessionIndex idx = MultiViewSession.Load(sessionFolder);
            if (idx == null) throw new InvalidOperationException("session.json not found in " + sessionFolder);

            var views = new List<Tuple<string, string>>();          // view name, absolute folder
            foreach (ViewOutcome o in idx.Views)
            {
                string f = string.IsNullOrEmpty(o.Folder) ? Path.Combine(sessionFolder, o.View) : Path.Combine(sessionFolder, o.Folder);
                if (File.Exists(Path.Combine(f, "record.json"))) views.Add(Tuple.Create(o.View, f));
            }

            string status = idx.Status ?? "in_progress";
            if (finalize && status == "in_progress") status = "incomplete";

            var head = new ScanSessionRow
            {
                ScanSessionID = idx.SessionId,
                ParticipantID = participantId,
                StartedAt = Local(idx.StartedUtc),
                CompletedAt = status == "in_progress" ? null : Local(idx.UpdatedUtc),
                OverallPredictedClass = idx.Fusion != null ? idx.Fusion.TopClass : null,
                OverallScore = idx.Fusion != null && idx.Fusion.ViewsUsed > 0 ? idx.Fusion.TopClassPercent : (double?)null,
                FusionMethod = idx.Fusion != null ? idx.Fusion.Method : null,
                ViewDisagreement = idx.Fusion != null && idx.Fusion.Disagreement,
                ViewsCompleted = idx.Views.Count(v => v.Ok),
                ViewsExpected = ScanViews.Sequence.Length,
                ScanStatus = status,
                ScanMode = "MultiView",
                SessionFolder = sessionFolder,
                OperatorID = AppSession.ActorId,
                FusionJson = idx.Fusion != null ? JsonSerializer.Serialize(idx.Fusion) : null,
                LegacyPatientLabel = participantId == null ? idx.PatientId : null
            };

            Write(head, views, finalize);
            return head.ScanSessionID;
        }

        /// <summary>Import a single capture (3-view scan switched off, or an uploaded image) as a one-view session.</summary>
        public static string ImportSingleCapture(string captureFolder, string participantId, bool finalize)
        {
            CaptureRecord rec = ReviewStore.LoadRecord(captureFolder);
            if (rec == null) throw new InvalidOperationException("record.json not found in " + captureFolder);

            string top = rec.Primary != null ? rec.Primary.OnnxTopClass : null;
            double? score = rec.Primary != null ? rec.Primary.OnnxConfidencePercent : (double?)null;

            var head = new ScanSessionRow
            {
                ScanSessionID = "single-" + rec.CaptureId,
                ParticipantID = participantId,
                StartedAt = Local(rec.TimestampUtc),
                CompletedAt = Local(rec.TimestampUtc),
                OverallPredictedClass = top,
                OverallScore = score,
                FusionMethod = "Single capture: the classifier's scores for this one image (no multi-view fusion).",
                ViewDisagreement = false,
                ViewsCompleted = top != null ? 1 : 0,
                ViewsExpected = 1,
                ScanStatus = top != null ? "complete" : "incomplete",
                ScanMode = "Single",
                SessionFolder = captureFolder,
                OperatorID = AppSession.ActorId
            };

            Write(head, new List<Tuple<string, string>> { Tuple.Create("Single", captureFolder) }, finalize);
            return head.ScanSessionID;
        }

        /// <summary>
        /// Backward compatibility: index every saved scan folder that is not in the database yet.
        /// Folders whose patient id is a registered PS-xxxx are linked; the rest stay unlinked
        /// (Participant Records can link them later). Returns how many were imported.
        /// </summary>
        public static int ImportArchive(out int failed, bool linkedOnly = false)
        {
            failed = 0;
            int imported = 0;
            string root = CaptureArchive.RootDirectory;
            if (!Directory.Exists(root)) return 0;

            HashSet<string> known = KnownSessionIds();
            var pid = new Regex(@"^PS-\d{4,}$");

            foreach (string day in Directory.GetDirectories(root))
                foreach (string folder in Directory.GetDirectories(day))
                {
                    try
                    {
                        if (File.Exists(Path.Combine(folder, "session.json")))
                        {
                            SessionIndex idx = MultiViewSession.Load(folder);
                            if (idx == null || known.Contains(idx.SessionId)) continue;
                            if (idx.Views == null || idx.Views.Count == 0) continue;     // camera opened, nothing captured
                            string link = idx.PatientId != null && pid.IsMatch(idx.PatientId) && StudyRepository.GetParticipant(idx.PatientId) != null
                                ? idx.PatientId : null;
                            if (linkedOnly && link == null) continue;       // legacy scans are only imported by the researcher, on request
                            ImportSession(folder, link, true);
                            imported++;
                        }
                        else if (File.Exists(Path.Combine(folder, "record.json")))
                        {
                            CaptureRecord rec = ReviewStore.LoadRecord(folder);
                            if (rec == null || known.Contains("single-" + rec.CaptureId)) continue;
                            if (linkedOnly) continue;                       // a single capture does not record which participant it belongs to
                            ImportSingleCapture(folder, null, true);
                            imported++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        System.Diagnostics.Debug.WriteLine("[ImportArchive] " + folder + ": " + ex.Message);
                    }
                }

            if (imported > 0)
                StudyRepository.Audit("Import archive", "ScanSessions", null, imported + " saved scans indexed, " + failed + " failed");
            return imported;
        }

        /// <summary>
        /// Start-up safety net: scans that are on disk and belong to a registered participant but are missing
        /// from the database (a save failed, or the app was closed mid-scan) are indexed again, finalised as
        /// "incomplete" if they never finished. Returns how many were recovered.
        /// </summary>
        public static int RecoverLinkedSessions()
        {
            int failed;
            int n = ImportArchive(out failed, linkedOnly: true);
            if (n > 0 || failed > 0)
                StudyRepository.Audit("Recovered scans", "ScanSessions", null, n + " scan(s) re-indexed at start-up, " + failed + " could not be read");
            return n;
        }

        private static HashSet<string> KnownSessionIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT ScanSessionID FROM ScanSessions;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) set.Add(r.GetString(0));
            return set;
        }

        // -------------------------------------------------------------- write --

        private static void Write(ScanSessionRow head, List<Tuple<string, string>> views, bool finalize)
        {
            lock (Gate)
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                bool exists = false, frozen = false;
                string label = null;
                using (var cmd = StudyRepository.Cmd(c, "SELECT Finalized, SessionLabel FROM ScanSessions WHERE ScanSessionID=@id;", tx, "@id", head.ScanSessionID))
                using (var r = cmd.ExecuteReader())
                    if (r.Read())
                    {
                        exists = true;
                        frozen = StudyRepository.B(r, "Finalized");
                        label = StudyRepository.S(r, "SessionLabel");
                    }

                if (frozen) { tx.Commit(); return; }                         // historical output is never rewritten

                var records = new List<Tuple<string, string, CaptureRecord>>();
                foreach (Tuple<string, string> v in views)
                {
                    CaptureRecord rec = ReviewStore.LoadRecord(v.Item2);
                    if (rec != null) records.Add(Tuple.Create(v.Item1, v.Item2, rec));
                }

                // The model that produced the views (all views of one scan come from the same deployment).
                CaptureRecord first = records.Select(x => x.Item3).FirstOrDefault(x => x.Model != null);
                long modelId = first != null ? EnsureModel(c, tx, first.Model) : StudyRepository.EnsureCurrentModelVersion(c, tx);

                if (label == null) label = NewLabel(c, tx, head);

                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO ScanSessions
                    (ScanSessionID, SessionLabel, ParticipantID, LegacyPatientLabel, StartedAt, CompletedAt, OverallPredictedClass, OverallScore,
                     FusionMethod, ViewDisagreement, ViewsCompleted, ViewsExpected, ScanStatus, ScanMode, Finalized, ModelVersionID,
                     SessionFolder, OperatorID, FusionJson, ImportedAt)
                    VALUES (@id,@lbl,@p,@leg,@st,@ct,@cls,@sc,@fm,@dis,@vc,@ve,@ss,@mode,@fin,@mv,@fold,@op,@fj,@imp)
                    ON CONFLICT(ScanSessionID) DO UPDATE SET
                     ParticipantID=COALESCE(ScanSessions.ParticipantID, excluded.ParticipantID), CompletedAt=excluded.CompletedAt,
                     OverallPredictedClass=excluded.OverallPredictedClass, OverallScore=excluded.OverallScore, FusionMethod=excluded.FusionMethod,
                     ViewDisagreement=excluded.ViewDisagreement, ViewsCompleted=excluded.ViewsCompleted, ScanStatus=excluded.ScanStatus,
                     Finalized=excluded.Finalized, ModelVersionID=excluded.ModelVersionID, FusionJson=excluded.FusionJson,
                     ImportedAt=excluded.ImportedAt;", tx,
                    "@id", head.ScanSessionID, "@lbl", label, "@p", head.ParticipantID, "@leg", head.LegacyPatientLabel,
                    "@st", head.StartedAt, "@ct", head.CompletedAt, "@cls", head.OverallPredictedClass, "@sc", head.OverallScore,
                    "@fm", head.FusionMethod, "@dis", head.ViewDisagreement ? 1 : 0, "@vc", head.ViewsCompleted, "@ve", head.ViewsExpected,
                    "@ss", head.ScanStatus, "@mode", head.ScanMode, "@fin", finalize ? 1 : 0, "@mv", modelId, "@fold", head.SessionFolder,
                    "@op", head.OperatorID, "@fj", head.FusionJson, "@imp", StudyRepository.Now()))
                    cmd.ExecuteNonQuery();

                DeleteViews(c, tx, head.ScanSessionID);
                foreach (Tuple<string, string, CaptureRecord> v in records)
                    InsertView(c, tx, head.ScanSessionID, v.Item1, v.Item2, v.Item3, modelId);

                if (!exists)
                    StudyRepository.Audit(c, tx, "Scan saved", "ScanSession", head.ScanSessionID,
                        label + " | " + head.ScanMode + " | " + head.ViewsCompleted + "/" + head.ViewsExpected + " views");
                if (finalize)
                    StudyRepository.Audit(c, tx, "Scan finalized", "ScanSession", head.ScanSessionID, head.ScanStatus);

                tx.Commit();
            }
        }

        private static string NewLabel(SQLiteConnection c, SQLiteTransaction tx, ScanSessionRow head)
        {
            if (string.IsNullOrEmpty(head.ParticipantID))
                return "UNLINKED-" + head.ScanSessionID.Replace("single-", "").Substring(0, 8).ToUpperInvariant();

            using (var cmd = StudyRepository.Cmd(c, "SELECT COUNT(*) FROM ScanSessions WHERE ParticipantID=@p;", tx, "@p", head.ParticipantID))
            {
                int n = Convert.ToInt32(cmd.ExecuteScalar()) + 1;
                return head.ParticipantID + "-S" + n.ToString("00", CultureInfo.InvariantCulture);
            }
        }

        private static long EnsureModel(SQLiteConnection c, SQLiteTransaction tx, ModelSection m)
        {
            AI.ModelInfo mi = AI.ModelInfo.Current;
            bool isCurrent = m.OnnxSha256 == mi.OnnxSha256;

            using (var cmd = StudyRepository.Cmd(c, @"INSERT OR IGNORE INTO ModelVersions
                (ModelName, Architecture, RunName, OnnxSha256, KerasSha256, ServiceSha256, ServiceVersion, CreatedUtc, Classes, FirstSeenAt)
                VALUES ('PrecisionSkin','MobileNetV2',@rn,@o,@k,@s,@sv,@cu,@cl,@at);", tx,
                "@rn", m.RunName, "@o", m.OnnxSha256, "@k", m.KerasSha256, "@s", m.ServiceSha256,
                "@sv", m.ServiceVersionReported, "@cu", isCurrent ? mi.CreatedUtc : null,
                "@cl", m.Classes != null ? string.Join(",", m.Classes) : string.Join(",", StudyText.Classes), "@at", StudyRepository.Now()))
                cmd.ExecuteNonQuery();

            using (var cmd = StudyRepository.Cmd(c, @"SELECT ModelVersionID FROM ModelVersions
                WHERE OnnxSha256 IS @o AND KerasSha256 IS @k AND ServiceSha256 IS @s;", tx,
                "@o", m.OnnxSha256, "@k", m.KerasSha256, "@s", m.ServiceSha256))
                return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private static void DeleteViews(SQLiteConnection c, SQLiteTransaction tx, string sessionId)
        {
            const string sub = "(SELECT CaptureID FROM CaptureViews WHERE ScanSessionID=@s)";
            foreach (string t in new[] { "AttributionMaps", "AnalysisImages", "ClassScores", "AnalysisResults" })
                using (var cmd = StudyRepository.Cmd(c, "DELETE FROM " + t + " WHERE CaptureID IN " + sub + ";", tx, "@s", sessionId))
                    cmd.ExecuteNonQuery();
            using (var cmd = StudyRepository.Cmd(c, "DELETE FROM CaptureViews WHERE ScanSessionID=@s;", tx, "@s", sessionId))
                cmd.ExecuteNonQuery();
        }

        private static void InsertView(SQLiteConnection c, SQLiteTransaction tx, string sessionId, string viewType,
                                       string folder, CaptureRecord rec, long modelId)
        {
            string P(string key)
            {
                string f;
                return rec.Files != null && rec.Files.TryGetValue(key, out f) && !string.IsNullOrEmpty(f) ? Path.Combine(folder, f) : null;
            }

            QualitySection q = rec.Quality;
            StabilitySection st = rec.Stability;
            SessionSection ss = rec.Session;

            using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO CaptureViews
                (CaptureID, ScanSessionID, ViewType, FrameID, CapturedAt, Trigger, OriginalImagePath, AnalysedImagePath, ImageWidth, ImageHeight,
                 Sharpness, Brightness, UnderExposed, OverExposed, Motion, PoseYaw, PosePitch, PoseRoll, PoseVerified, FaceFound,
                 StabilityScore, FramesHeldStable, QualityWeight, QualityStatus, RecordFolder)
                VALUES (@id,@s,@v,@f,@at,@tr,@orig,@an,@w,@h,@sh,@br,@ue,@oe,@mo,@yaw,NULL,@roll,@pv,@ff,@stab,@held,@qw,@qs,@fold);", tx,
                "@id", rec.CaptureId, "@s", sessionId, "@v", viewType, "@f", rec.FrameId > 0 ? (object)rec.FrameId : null,
                "@at", Local(rec.TimestampUtc), "@tr", rec.Trigger, "@orig", P("original"), "@an", P("analysed"),
                "@w", rec.Input != null ? rec.Input.OriginalWidth : 0, "@h", rec.Input != null ? rec.Input.OriginalHeight : 0,
                "@sh", q != null ? (object)q.Sharpness : null, "@br", q != null ? (object)q.Brightness : null,
                "@ue", q != null ? (object)q.UnderExposed : null, "@oe", q != null ? (object)q.OverExposed : null,
                "@mo", st != null ? (object)st.MotionMean : null,
                "@yaw", ss != null ? ss.YawRatio : null, "@roll", ss != null ? ss.RollDegrees : null,
                "@pv", ss != null && ss.PoseGateVerified ? 1 : 0, "@ff", q != null && q.FaceFound ? 1 : 0,
                "@stab", st != null ? (object)st.Consistency : null, "@held", st != null ? (object)st.FramesHeldStable : null,
                "@qw", ss != null ? (object)ss.QualityWeight : null, "@qs", QualityStatus(rec, viewType), "@fold", folder))
                cmd.ExecuteNonQuery();

            Dictionary<string, double> sc = rec.Scores != null ? rec.Scores.OnnxPercent : null;
            double Sc(string k) { double v; return sc != null && sc.TryGetValue(k, out v) ? v : 0.0; }

            if (sc != null)
            {
                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO ClassScores
                    (CaptureID, AcneScore, HyperpigmentationScore, EczemaScore, NormalScore, PredictedClass, Confidence)
                    VALUES (@id,@a,@h,@e,@n,@p,@c);", tx,
                    "@id", rec.CaptureId, "@a", Sc("Acne"), "@h", Sc("Hyperpigmentation"), "@e", Sc("Eczema"), "@n", Sc("Normal"),
                    "@p", rec.Primary != null ? rec.Primary.OnnxTopClass : null,
                    "@c", rec.Primary != null ? (object)rec.Primary.OnnxConfidencePercent : null))
                    cmd.ExecuteNonQuery();
            }

            if (rec.Primary != null)
            {
                ConsistencySection k = rec.Consistency;
                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO AnalysisResults
                    (CaptureID, Runtime, ModelVersionID, PredictedClass, Confidence, RunnerUpClass, MarginPercent, StabilisedClass,
                     KerasSameTop, KerasMaxAbsDiff, ConsistencyWarning, CreatedAt)
                    VALUES (@id,'ONNX Runtime',@mv,@p,@c,@ru,@m,@sc,@ks,@kd,@w,@at);", tx,
                    "@id", rec.CaptureId, "@mv", modelId, "@p", rec.Primary.OnnxTopClass, "@c", rec.Primary.OnnxConfidencePercent,
                    "@ru", rec.Primary.RunnerUpClass, "@m", rec.Primary.MarginPercent, "@sc", rec.Primary.DisplayedClass,
                    "@ks", k != null && k.SameTopClass.HasValue ? (object)(k.SameTopClass.Value ? 1 : 0) : null,
                    "@kd", k != null ? k.MaxAbsDifference : null, "@w", k != null ? k.Warning : null, "@at", Local(rec.TimestampUtc)))
                    cmd.ExecuteNonQuery();
            }

            foreach (var img in new[] { Tuple.Create("Original", "original"), Tuple.Create("Analysed", "analysed"),
                                        Tuple.Create("ModelInput224", "model_input_224"), Tuple.Create("OverlayAll", "overlay_all") })
            {
                string p = P(img.Item2);
                if (p == null) continue;
                using (var cmd = StudyRepository.Cmd(c, "INSERT INTO AnalysisImages(CaptureID, ImageType, Path) VALUES (@id,@t,@p);", tx,
                    "@id", rec.CaptureId, "@t", img.Item1, "@p", p))
                    cmd.ExecuteNonQuery();
            }

            string G(string key) { string v; return rec.GradCam != null && rec.GradCam.TryGetValue(key, out v) ? v : null; }
            string similarity = rec.ClassMapSimilarity != null ? JsonSerializer.Serialize(rec.ClassMapSimilarity) : null;

            foreach (MapSection m in rec.ClassMaps ?? new List<MapSection>())
            {
                int index = Array.IndexOf(StudyText.Classes, m.Class);
                using (var cmd = StudyRepository.Cmd(c, @"INSERT OR REPLACE INTO AttributionMaps
                    (CaptureID, TargetClass, TargetClassIndex, TargetLayer, Method, RawMapPath, RenderedMapPath, OverlayPath,
                     RelativeStrength, Diffuse, TopZone, SimilarityDebugMetadata, GradCAMVersion)
                    VALUES (@id,@c,@i,@l,@m,@raw,@ren,@ov,@rs,@d,@z,@sim,@ver);", tx,
                    "@id", rec.CaptureId, "@c", m.Class, "@i", index, "@l", G("target_layer") ?? "service default (not recorded)",
                    "@m", G("method"), "@raw", m.RawCamFile != null ? Path.Combine(folder, m.RawCamFile) : null,
                    "@ren", m.HeatmapFile != null ? Path.Combine(folder, m.HeatmapFile) : null,
                    "@ov", m.OverlayFile != null ? Path.Combine(folder, m.OverlayFile) : null,
                    "@rs", m.RelativeStrength, "@d", m.Diffuse ? 1 : 0, "@z", m.TopZone, "@sim", similarity, "@ver", G("service_version")))
                    cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Plain-language capture quality for the Image Review step and the records.</summary>
        public static string QualityStatus(CaptureRecord rec, string viewType)
        {
            var issues = new List<string>();
            ScanSettings s = ScanSettings.Current;
            QualitySection q = rec.Quality;

            if (q == null) issues.Add("quality not measured");
            else
            {
                if (q.Sharpness < s.MinSharpness) issues.Add("blur");
                if (q.Brightness < s.MinBrightness) issues.Add("too dark");
                else if (q.Brightness > s.MaxBrightness) issues.Add("too bright");
                if (q.UnderExposed > 0.35 || q.OverExposed > 0.15) issues.Add("poor exposure");
                if (!q.FaceFound) issues.Add("face not found");
            }

            if (viewType != "Single" && (rec.Session == null || !rec.Session.PoseGateVerified))
                issues.Add("pose not verified");
            if (rec.Trigger == "manual") issues.Add("manual capture");

            return issues.Count == 0 ? "Good" : "Check: " + string.Join(", ", issues);
        }

        private static string Local(string isoUtc)
        {
            DateTime t;
            if (string.IsNullOrEmpty(isoUtc) ||
                !DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                return StudyRepository.Now();
            return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
