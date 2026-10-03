// ============================================================================
// StudyRepository.cs  -  namespace SkinDevApp.Data
//
// All reads and writes of the study database except the AI import
// (SessionImporter.cs). Every method opens its own short connection, so it can
// be called from the UI thread for small queries or from Task.Run.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SkinDevApp.AI;
using SkinDevApp.Scanning;

namespace SkinDevApp.Data
{
    public static class StudyRepository
    {
        public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------ helpers --

        internal static SQLiteCommand Cmd(SQLiteConnection c, string sql, SQLiteTransaction tx = null, params object[] args)
        {
            var cmd = new SQLiteCommand(sql, c, tx);
            for (int i = 0; i + 1 < args.Length; i += 2)
                cmd.Parameters.AddWithValue((string)args[i], args[i + 1] ?? DBNull.Value);
            return cmd;
        }

        internal static string S(SQLiteDataReader r, string col)
        {
            object v = r[col];
            return v == DBNull.Value ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        internal static double? D(SQLiteDataReader r, string col)
        {
            object v = r[col];
            return v == DBNull.Value ? (double?)null : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        internal static long? L(SQLiteDataReader r, string col)
        {
            object v = r[col];
            return v == DBNull.Value ? (long?)null : Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        internal static bool B(SQLiteDataReader r, string col) => (L(r, col) ?? 0) != 0;

        private static int Count(string sql, params object[] args)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, sql, null, args))
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        // -------------------------------------------------------------- audit --

        public static void Audit(string action, string entity, string entityId, string details)
        {
            try
            {
                using (var c = StudyDatabase.Open())
                    Audit(c, null, action, entity, entityId, details);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Audit] " + ex.Message); }
        }

        internal static void Audit(SQLiteConnection c, SQLiteTransaction tx, string action, string entity, string entityId, string details)
        {
            using (var cmd = Cmd(c,
                "INSERT INTO AuditLog(At, Actor, Action, Entity, EntityID, Details) VALUES (@at,@ac,@a,@e,@id,@d);", tx,
                "@at", Now(), "@ac", AppSession.ActorId, "@a", action, "@e", entity, "@id", entityId, "@d", details))
                cmd.ExecuteNonQuery();
        }

        public static List<AuditEntry> RecentAudit(int limit = 200)
        {
            var list = new List<AuditEntry>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM AuditLog ORDER BY AuditID DESC LIMIT @n;", null, "@n", limit))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new AuditEntry
                    {
                        AuditID = L(r, "AuditID") ?? 0, At = S(r, "At"), Actor = S(r, "Actor"), Action = S(r, "Action"),
                        Entity = S(r, "Entity"), EntityID = S(r, "EntityID"), Details = S(r, "Details")
                    });
            return list;
        }

        // ------------------------------------------------------- participants --

        public static string FormatParticipantId(int seq) => "PS-" + seq.ToString("0000", CultureInfo.InvariantCulture);

        /// <summary>The ID the next registration will most likely receive (display only).</summary>
        public static string PeekNextParticipantId()
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT COALESCE(MAX(Seq),0)+1 FROM Participants;"))
                return FormatParticipantId(Convert.ToInt32(cmd.ExecuteScalar()));
        }

        /// <summary>
        /// Register a new participant: generates the ParticipantID inside the write
        /// transaction (never reused, never duplicated), stores the skin profile and,
        /// when collected, the manual Fitzpatrick record.
        /// </summary>
        public static string RegisterParticipant(Participant p, SkinProfile sp)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                int seq;
                using (var cmd = Cmd(c, "SELECT COALESCE(MAX(Seq),0)+1 FROM Participants;", tx))
                    seq = Convert.ToInt32(cmd.ExecuteScalar());

                p.Seq = seq;
                p.ParticipantID = FormatParticipantId(seq);
                p.RegisteredAt = Now();
                p.UpdatedAt = p.RegisteredAt;

                using (var cmd = Cmd(c, @"INSERT INTO Participants
                    (ParticipantID, Seq, RegisteredAt, FirstName, MiddleName, LastName, DateOfBirth, Age, Sex, Contact, OperatorID, Status, UpdatedAt)
                    VALUES (@id,@seq,@reg,@fn,@mn,@ln,@dob,@age,@sex,@con,@op,@st,@up);", tx,
                    "@id", p.ParticipantID, "@seq", seq, "@reg", p.RegisteredAt, "@fn", p.FirstName, "@mn", p.MiddleName,
                    "@ln", p.LastName, "@dob", p.DateOfBirth, "@age", p.Age, "@sex", p.Sex, "@con", p.Contact,
                    "@op", p.OperatorID, "@st", p.Status ?? "Active", "@up", p.UpdatedAt))
                    cmd.ExecuteNonQuery();

                sp.ParticipantID = p.ParticipantID;
                UpsertSkinProfile(c, tx, sp);
                RecordManualFitzpatrick(c, tx, p.ParticipantID, sp);
                Audit(c, tx, "Register", "Participant", p.ParticipantID, "New participant registered");

                tx.Commit();
                return p.ParticipantID;
            }
        }

        /// <summary>Explicit edit of registration data. Never touches any scan result.</summary>
        public static void UpdateParticipant(Participant p, SkinProfile sp, string reason)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                SkinProfile before = GetSkinProfile(c, tx, p.ParticipantID);

                p.UpdatedAt = Now();
                using (var cmd = Cmd(c, @"UPDATE Participants SET FirstName=@fn, MiddleName=@mn, LastName=@ln, DateOfBirth=@dob,
                    Age=@age, Sex=@sex, Contact=@con, Status=@st, UpdatedAt=@up WHERE ParticipantID=@id;", tx,
                    "@id", p.ParticipantID, "@fn", p.FirstName, "@mn", p.MiddleName, "@ln", p.LastName, "@dob", p.DateOfBirth,
                    "@age", p.Age, "@sex", p.Sex, "@con", p.Contact, "@st", p.Status ?? "Active", "@up", p.UpdatedAt))
                    cmd.ExecuteNonQuery();

                sp.ParticipantID = p.ParticipantID;
                UpsertSkinProfile(c, tx, sp);
                if (before == null || before.FitzpatrickManual != sp.FitzpatrickManual || before.FitzpatrickSource != sp.FitzpatrickSource)
                    RecordManualFitzpatrick(c, tx, p.ParticipantID, sp);

                Audit(c, tx, "Edit", "Participant", p.ParticipantID, string.IsNullOrWhiteSpace(reason) ? "Registration edited" : reason);
                tx.Commit();
            }
        }

        private static void UpsertSkinProfile(SQLiteConnection c, SQLiteTransaction tx, SkinProfile sp)
        {
            sp.UpdatedAt = Now();
            using (var cmd = Cmd(c, @"INSERT INTO SkinProfiles
                (ParticipantID, GeneralSkinType, FitzpatrickManual, FitzpatrickSource, Sensitivity, Concerns, ConcernOther, Regions, RegionOther,
                 ConcernDuration, Cleanser, Moisturizer, Sunscreen, AcneTreatment, EczemaTreatment, PigmentationTreatment, OtherProducts,
                 RecentProcedures, OtherResponses, UpdatedAt)
                VALUES (@id,@gst,@fz,@fzs,@sen,@con,@cono,@reg,@rego,@dur,@cl,@mo,@su,@at,@et,@pt,@op,@rp,@or,@up)
                ON CONFLICT(ParticipantID) DO UPDATE SET
                 GeneralSkinType=excluded.GeneralSkinType, FitzpatrickManual=excluded.FitzpatrickManual, FitzpatrickSource=excluded.FitzpatrickSource,
                 Sensitivity=excluded.Sensitivity, Concerns=excluded.Concerns, ConcernOther=excluded.ConcernOther, Regions=excluded.Regions,
                 RegionOther=excluded.RegionOther, ConcernDuration=excluded.ConcernDuration, Cleanser=excluded.Cleanser,
                 Moisturizer=excluded.Moisturizer, Sunscreen=excluded.Sunscreen, AcneTreatment=excluded.AcneTreatment,
                 EczemaTreatment=excluded.EczemaTreatment, PigmentationTreatment=excluded.PigmentationTreatment,
                 OtherProducts=excluded.OtherProducts, RecentProcedures=excluded.RecentProcedures,
                 OtherResponses=excluded.OtherResponses, UpdatedAt=excluded.UpdatedAt;", tx,
                "@id", sp.ParticipantID, "@gst", sp.GeneralSkinType, "@fz", sp.FitzpatrickManual, "@fzs", sp.FitzpatrickSource,
                "@sen", sp.Sensitivity, "@con", sp.Concerns, "@cono", sp.ConcernOther, "@reg", sp.Regions, "@rego", sp.RegionOther,
                "@dur", sp.ConcernDuration, "@cl", sp.Cleanser, "@mo", sp.Moisturizer, "@su", sp.Sunscreen, "@at", sp.AcneTreatment,
                "@et", sp.EczemaTreatment, "@pt", sp.PigmentationTreatment, "@op", sp.OtherProducts, "@rp", sp.RecentProcedures,
                "@or", sp.OtherResponses, "@up", sp.UpdatedAt))
                cmd.ExecuteNonQuery();
        }

        private static void RecordManualFitzpatrick(SQLiteConnection c, SQLiteTransaction tx, string participantId, SkinProfile sp)
        {
            if (string.IsNullOrWhiteSpace(sp.FitzpatrickManual) || sp.FitzpatrickManual == "Not collected") return;

            using (var cmd = Cmd(c, @"INSERT INTO FitzpatrickAssessments
                (ParticipantID, ScanSessionID, SourceType, ManualType, PredictedType, ModelVersionID, ScoreData, AssessmentDate, Status)
                VALUES (@id, NULL, 'Manual', @m, NULL, NULL, NULL, @at, @st);", tx,
                "@id", participantId, "@m", sp.FitzpatrickManual, "@at", Now(),
                "@st", (sp.FitzpatrickSource ?? "Self-reported") + "; AI assessment not available"))
                cmd.ExecuteNonQuery();
        }

        private static Participant ReadParticipant(SQLiteDataReader r) => new Participant
        {
            ParticipantID = S(r, "ParticipantID"),
            Seq = (int)(L(r, "Seq") ?? 0),
            RegisteredAt = S(r, "RegisteredAt"),
            FirstName = S(r, "FirstName"),
            MiddleName = S(r, "MiddleName"),
            LastName = S(r, "LastName"),
            DateOfBirth = S(r, "DateOfBirth"),
            Age = (int?)L(r, "Age"),
            Sex = S(r, "Sex"),
            Contact = S(r, "Contact"),
            OperatorID = S(r, "OperatorID"),
            Status = S(r, "Status"),
            UpdatedAt = S(r, "UpdatedAt")
        };

        public static string NormaliseParticipantId(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string t = text.Trim().ToUpperInvariant().Replace(" ", "");
            int n;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0) return FormatParticipantId(n);
            if (t.StartsWith("PS") && !t.StartsWith("PS-")) t = "PS-" + t.Substring(2);
            if (t.StartsWith("PS-") && int.TryParse(t.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                return FormatParticipantId(n);
            return t;
        }

        public static Participant GetParticipant(string participantId)
        {
            string id = NormaliseParticipantId(participantId);
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM Participants WHERE ParticipantID=@id;", null, "@id", id))
            using (var r = cmd.ExecuteReader())
                return r.Read() ? ReadParticipant(r) : null;
        }

        /// <summary>Filter by ID / name text, registration date (yyyy-MM-dd) and status. Empty = no filter.</summary>
        public static List<Participant> SearchParticipants(string text, string date, string status)
        {
            var list = new List<Participant>();
            string t = (text ?? "").Trim();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, @"SELECT * FROM Participants
                WHERE (@t = '' OR ParticipantID LIKE @like OR FirstName LIKE @like OR LastName LIKE @like OR MiddleName LIKE @like
                       OR (COALESCE(FirstName,'') || ' ' || COALESCE(LastName,'')) LIKE @like)
                  AND (@d = '' OR substr(RegisteredAt,1,10) = @d)
                  AND (@s = '' OR Status = @s)
                ORDER BY Seq DESC;", null,
                "@t", t, "@like", "%" + t + "%", "@d", (date ?? "").Trim(), "@s", (status ?? "").Trim()))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadParticipant(r));
            return list;
        }

        public static SkinProfile GetSkinProfile(string participantId)
        {
            using (var c = StudyDatabase.Open())
                return GetSkinProfile(c, null, participantId);
        }

        private static SkinProfile GetSkinProfile(SQLiteConnection c, SQLiteTransaction tx, string participantId)
        {
            using (var cmd = Cmd(c, "SELECT * FROM SkinProfiles WHERE ParticipantID=@id;", tx, "@id", participantId))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;
                return new SkinProfile
                {
                    ParticipantID = S(r, "ParticipantID"),
                    GeneralSkinType = S(r, "GeneralSkinType"),
                    FitzpatrickManual = S(r, "FitzpatrickManual"),
                    FitzpatrickSource = S(r, "FitzpatrickSource"),
                    Sensitivity = S(r, "Sensitivity"),
                    Concerns = S(r, "Concerns"),
                    ConcernOther = S(r, "ConcernOther"),
                    Regions = S(r, "Regions"),
                    RegionOther = S(r, "RegionOther"),
                    ConcernDuration = S(r, "ConcernDuration"),
                    Cleanser = S(r, "Cleanser"),
                    Moisturizer = S(r, "Moisturizer"),
                    Sunscreen = S(r, "Sunscreen"),
                    AcneTreatment = S(r, "AcneTreatment"),
                    EczemaTreatment = S(r, "EczemaTreatment"),
                    PigmentationTreatment = S(r, "PigmentationTreatment"),
                    OtherProducts = S(r, "OtherProducts"),
                    RecentProcedures = S(r, "RecentProcedures"),
                    OtherResponses = S(r, "OtherResponses"),
                    UpdatedAt = S(r, "UpdatedAt")
                };
            }
        }

        // ----------------------------------------------------------- consent --

        public static long SaveConsent(Consent k)
        {
            k.RecordedAt = Now();
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                long id;
                using (var cmd = Cmd(c, @"INSERT INTO Consents
                    (ParticipantID, ResearchInfoUnderstood, VoluntaryParticipation, ImageCaptureConsent, ImageProcessingConsent,
                     FutureModelUseConsent, Decision, RecordedAt, OperatorID)
                    VALUES (@id,@a,@b,@c,@d,@e,@dec,@at,@op); SELECT last_insert_rowid();", tx,
                    "@id", k.ParticipantID, "@a", k.ResearchInfoUnderstood ? 1 : 0, "@b", k.VoluntaryParticipation ? 1 : 0,
                    "@c", k.ImageCaptureConsent ? 1 : 0, "@d", k.ImageProcessingConsent ? 1 : 0, "@e", k.FutureModelUseConsent ? 1 : 0,
                    "@dec", k.Decision, "@at", k.RecordedAt, "@op", k.OperatorID))
                    id = Convert.ToInt64(cmd.ExecuteScalar());
                Audit(c, tx, "Consent " + k.Decision, "Participant", k.ParticipantID,
                      "future model use: " + (k.FutureModelUseConsent ? "yes" : "no"));
                tx.Commit();
                return id;
            }
        }

        public static Consent GetLatestConsent(string participantId)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM Consents WHERE ParticipantID=@id ORDER BY ConsentID DESC LIMIT 1;", null, "@id", participantId))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;
                return new Consent
                {
                    ConsentID = L(r, "ConsentID") ?? 0,
                    ParticipantID = S(r, "ParticipantID"),
                    ResearchInfoUnderstood = B(r, "ResearchInfoUnderstood"),
                    VoluntaryParticipation = B(r, "VoluntaryParticipation"),
                    ImageCaptureConsent = B(r, "ImageCaptureConsent"),
                    ImageProcessingConsent = B(r, "ImageProcessingConsent"),
                    FutureModelUseConsent = B(r, "FutureModelUseConsent"),
                    Decision = S(r, "Decision"),
                    RecordedAt = S(r, "RecordedAt"),
                    OperatorID = S(r, "OperatorID")
                };
            }
        }

        // ------------------------------------------------------------ models --

        /// <summary>The ModelVersions row for the model that is deployed right now (created on first use).</summary>
        public static long EnsureCurrentModelVersion(SQLiteConnection c, SQLiteTransaction tx)
        {
            ModelInfo mi = ModelInfo.Current;
            using (var cmd = Cmd(c, @"INSERT OR IGNORE INTO ModelVersions
                (ModelName, Architecture, RunName, OnnxSha256, KerasSha256, ServiceSha256, ServiceVersion, CreatedUtc, Classes, FirstSeenAt)
                VALUES ('PrecisionSkin','MobileNetV2',@rn,@o,@k,@s,@sv,@cu,@cl,@at);", tx,
                "@rn", mi.RunName, "@o", mi.OnnxSha256, "@k", mi.KerasSha256, "@s", mi.ServiceSha256, "@sv", mi.ServiceVersion,
                "@cu", mi.CreatedUtc, "@cl", string.Join(",", StudyText.Classes), "@at", Now()))
                cmd.ExecuteNonQuery();

            using (var cmd = Cmd(c, @"SELECT ModelVersionID FROM ModelVersions
                WHERE OnnxSha256 IS @o AND KerasSha256 IS @k AND ServiceSha256 IS @s;", tx,
                "@o", mi.OnnxSha256, "@k", mi.KerasSha256, "@s", mi.ServiceSha256))
                return Convert.ToInt64(cmd.ExecuteScalar());
        }

        public static long EnsureCurrentModelVersion()
        {
            using (var c = StudyDatabase.Open())
                return EnsureCurrentModelVersion(c, null);
        }

        private static ModelVersion ReadModel(SQLiteDataReader r) => new ModelVersion
        {
            ModelVersionID = L(r, "ModelVersionID") ?? 0,
            ModelName = S(r, "ModelName"),
            Architecture = S(r, "Architecture"),
            RunName = S(r, "RunName"),
            OnnxSha256 = S(r, "OnnxSha256"),
            KerasSha256 = S(r, "KerasSha256"),
            ServiceSha256 = S(r, "ServiceSha256"),
            ServiceVersion = S(r, "ServiceVersion"),
            CreatedUtc = S(r, "CreatedUtc"),
            FirstSeenAt = S(r, "FirstSeenAt")
        };

        public static ModelVersion GetModelVersion(long? id)
        {
            if (!id.HasValue) return null;
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM ModelVersions WHERE ModelVersionID=@id;", null, "@id", id.Value))
            using (var r = cmd.ExecuteReader())
                return r.Read() ? ReadModel(r) : null;
        }

        public static List<ModelVersion> AllModelVersions()
        {
            var list = new List<ModelVersion>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM ModelVersions ORDER BY ModelVersionID DESC;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadModel(r));
            return list;
        }

        // ---------------------------------------------------------- sessions --

        private const string SessionSelect = @"
            SELECT s.*,
                   (COALESCE(m.RunName,'unknown') || ' / ' || substr(COALESCE(m.OnnxSha256,'unknown'),1,12)) AS ModelLabel,
                   CASE WHEN EXISTS (SELECT 1 FROM DermatologistValidations d WHERE d.ScanSessionID=s.ScanSessionID AND d.ValidationStatus='Completed')
                             THEN 'Dermatologist validated'
                        WHEN EXISTS (SELECT 1 FROM ResearcherEvaluations e WHERE e.ScanSessionID=s.ScanSessionID)
                             THEN 'Researcher assessed'
                        ELSE 'Pending' END AS ValidationStatus
            FROM ScanSessions s LEFT JOIN ModelVersions m ON m.ModelVersionID = s.ModelVersionID ";

        private static ScanSessionRow ReadSession(SQLiteDataReader r) => new ScanSessionRow
        {
            ScanSessionID = S(r, "ScanSessionID"),
            SessionLabel = S(r, "SessionLabel"),
            ParticipantID = S(r, "ParticipantID"),
            LegacyPatientLabel = S(r, "LegacyPatientLabel"),
            StartedAt = S(r, "StartedAt"),
            CompletedAt = S(r, "CompletedAt"),
            OverallPredictedClass = S(r, "OverallPredictedClass"),
            OverallScore = D(r, "OverallScore"),
            FusionMethod = S(r, "FusionMethod"),
            ViewDisagreement = B(r, "ViewDisagreement"),
            ViewsCompleted = (int)(L(r, "ViewsCompleted") ?? 0),
            ViewsExpected = (int)(L(r, "ViewsExpected") ?? 3),
            ScanStatus = S(r, "ScanStatus"),
            ScanMode = S(r, "ScanMode"),
            Finalized = B(r, "Finalized"),
            ModelVersionID = L(r, "ModelVersionID"),
            SessionFolder = S(r, "SessionFolder"),
            OperatorID = S(r, "OperatorID"),
            FusionJson = S(r, "FusionJson"),
            ModelLabel = S(r, "ModelLabel"),
            ValidationStatus = S(r, "ValidationStatus")
        };

        public static List<ScanSessionRow> SessionsForParticipant(string participantId)
        {
            var list = new List<ScanSessionRow>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, SessionSelect + " WHERE s.ParticipantID=@id ORDER BY s.StartedAt DESC;", null, "@id", participantId))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadSession(r));
            return list;
        }

        public static List<ScanSessionRow> UnlinkedSessions()
        {
            var list = new List<ScanSessionRow>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, SessionSelect + " WHERE s.ParticipantID IS NULL ORDER BY s.StartedAt DESC;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadSession(r));
            return list;
        }

        public static List<ScanSessionRow> PendingVerification()
        {
            var list = new List<ScanSessionRow>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, SessionSelect + @" WHERE s.ViewsCompleted > 0 AND " + ActiveSession + @"
                AND NOT EXISTS (SELECT 1 FROM ResearcherEvaluations e WHERE e.ScanSessionID = s.ScanSessionID)
                ORDER BY s.StartedAt DESC;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(ReadSession(r));
            return list;
        }

        public static ScanSessionRow GetSession(string scanSessionId)
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, SessionSelect + " WHERE s.ScanSessionID=@id;", null, "@id", scanSessionId))
            using (var r = cmd.ExecuteReader())
                return r.Read() ? ReadSession(r) : null;
        }

        /// <summary>Loads the exact saved data of one scan. Nothing is re-run.</summary>
        public static SessionDetail GetSessionDetail(string scanSessionId)
        {
            ScanSessionRow s = GetSession(scanSessionId);
            if (s == null) return null;

            var d = new SessionDetail
            {
                Session = s,
                Participant = string.IsNullOrEmpty(s.ParticipantID) ? null : GetParticipant(s.ParticipantID),
                Model = GetModelVersion(s.ModelVersionID),
                Evaluations = EvaluationsFor(scanSessionId),
                Validations = ValidationsFor(scanSessionId)
            };

            if (!string.IsNullOrEmpty(s.FusionJson))
            {
                try { d.Fusion = JsonSerializer.Deserialize<FusedResult>(s.FusionJson); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[GetSessionDetail] fusion: " + ex.Message); }
            }

            using (var c = StudyDatabase.Open())
            {
                using (var cmd = Cmd(c, "SELECT * FROM CaptureViews WHERE ScanSessionID=@id;", null, "@id", scanSessionId))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        d.Views.Add(new CaptureViewRow
                        {
                            CaptureID = S(r, "CaptureID"),
                            ScanSessionID = S(r, "ScanSessionID"),
                            ViewType = S(r, "ViewType"),
                            FrameID = L(r, "FrameID"),
                            CapturedAt = S(r, "CapturedAt"),
                            Trigger = S(r, "Trigger"),
                            OriginalImagePath = S(r, "OriginalImagePath"),
                            AnalysedImagePath = S(r, "AnalysedImagePath"),
                            ImageWidth = (int)(L(r, "ImageWidth") ?? 0),
                            ImageHeight = (int)(L(r, "ImageHeight") ?? 0),
                            Sharpness = D(r, "Sharpness"),
                            Brightness = D(r, "Brightness"),
                            UnderExposed = D(r, "UnderExposed"),
                            OverExposed = D(r, "OverExposed"),
                            Motion = D(r, "Motion"),
                            PoseYaw = D(r, "PoseYaw"),
                            PosePitch = D(r, "PosePitch"),
                            PoseRoll = D(r, "PoseRoll"),
                            PoseVerified = B(r, "PoseVerified"),
                            FaceFound = B(r, "FaceFound"),
                            StabilityScore = D(r, "StabilityScore"),
                            QualityWeight = D(r, "QualityWeight"),
                            QualityStatus = S(r, "QualityStatus"),
                            RecordFolder = S(r, "RecordFolder")
                        });

                foreach (CaptureViewRow v in d.Views)
                {
                    using (var cmd = Cmd(c, "SELECT * FROM ClassScores WHERE CaptureID=@id;", null, "@id", v.CaptureID))
                    using (var r = cmd.ExecuteReader())
                        if (r.Read())
                            v.Scores = new ClassScoreRow
                            {
                                CaptureID = v.CaptureID,
                                Acne = D(r, "AcneScore") ?? 0,
                                Hyperpigmentation = D(r, "HyperpigmentationScore") ?? 0,
                                Eczema = D(r, "EczemaScore") ?? 0,
                                Normal = D(r, "NormalScore") ?? 0,
                                PredictedClass = S(r, "PredictedClass"),
                                Confidence = D(r, "Confidence") ?? 0
                            };

                    using (var cmd = Cmd(c, "SELECT * FROM AttributionMaps WHERE CaptureID=@id ORDER BY TargetClassIndex;", null, "@id", v.CaptureID))
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            v.Maps.Add(new AttributionMapRow
                            {
                                CaptureID = v.CaptureID,
                                TargetClass = S(r, "TargetClass"),
                                TargetClassIndex = (int)(L(r, "TargetClassIndex") ?? 0),
                                TargetLayer = S(r, "TargetLayer"),
                                RawMapPath = S(r, "RawMapPath"),
                                RenderedMapPath = S(r, "RenderedMapPath"),
                                OverlayPath = S(r, "OverlayPath"),
                                RelativeStrength = D(r, "RelativeStrength") ?? 0,
                                Diffuse = B(r, "Diffuse"),
                                TopZone = S(r, "TopZone"),
                                GradCAMVersion = S(r, "GradCAMVersion"),
                                SimilarityDebugMetadata = S(r, "SimilarityDebugMetadata")
                            });
                }
            }

            d.Views = d.Views.OrderBy(v => Array.IndexOf(new[] { "Front", "Left", "Right", "Single" }, v.ViewType)).ToList();
            return d;
        }

        public static void LinkSessionToParticipant(string scanSessionId, string participantId)
        {
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var cmd = Cmd(c, "UPDATE ScanSessions SET ParticipantID=@p WHERE ScanSessionID=@s AND ParticipantID IS NULL;", tx,
                    "@p", participantId, "@s", scanSessionId))
                    cmd.ExecuteNonQuery();
                Audit(c, tx, "Link legacy scan", "ScanSession", scanSessionId, "linked to " + participantId);
                tx.Commit();
            }
        }

        // ------------------------------------------------- human assessments --

        public static long SaveResearcherEvaluation(ResearcherEvaluation e, IEnumerable<string> labels)
        {
            e.EvaluationDate = Now();
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                long id;
                using (var cmd = Cmd(c, @"INSERT INTO ResearcherEvaluations
                    (ScanSessionID, ResearcherID, ResearcherClassification, AgreementWithAI, Notes, FrontComment, LeftComment, RightComment, EvaluationDate)
                    VALUES (@s,@r,@cls,@ag,@n,@f,@l,@ri,@d); SELECT last_insert_rowid();", tx,
                    "@s", e.ScanSessionID, "@r", e.ResearcherID, "@cls", e.ResearcherClassification, "@ag", e.AgreementWithAI,
                    "@n", e.Notes, "@f", e.FrontComment, "@l", e.LeftComment, "@ri", e.RightComment, "@d", e.EvaluationDate))
                    id = Convert.ToInt64(cmd.ExecuteScalar());

                InsertLabels(c, tx, "Researcher", id, labels);
                Audit(c, tx, "Researcher evaluation", "ScanSession", e.ScanSessionID, e.ResearcherClassification + " / " + e.AgreementWithAI);
                tx.Commit();
                return id;
            }
        }

        public static long SaveDermatologistValidation(DermatologistValidation v, IEnumerable<string> labels)
        {
            v.ValidationDate = Now();
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                long id;
                using (var cmd = Cmd(c, @"INSERT INTO DermatologistValidations
                    (ScanSessionID, DermatologistID, ValidatorName, ProfessionalRole, CredentialReference, DermatologistAssessment,
                     AgreementWithAI, AgreementWithResearcher, Notes, ValidationDate, ValidationStatus)
                    VALUES (@s,@id,@nm,@role,@cr,@as,@ai,@ar,@n,@d,@st); SELECT last_insert_rowid();", tx,
                    "@s", v.ScanSessionID, "@id", v.DermatologistID, "@nm", v.ValidatorName, "@role", v.ProfessionalRole,
                    "@cr", v.CredentialReference, "@as", v.DermatologistAssessment, "@ai", v.AgreementWithAI,
                    "@ar", v.AgreementWithResearcher, "@n", v.Notes, "@d", v.ValidationDate, "@st", v.ValidationStatus))
                    id = Convert.ToInt64(cmd.ExecuteScalar());

                InsertLabels(c, tx, "Dermatologist", id, labels);
                Audit(c, tx, "Dermatologist validation", "ScanSession", v.ScanSessionID, v.DermatologistAssessment + " / " + v.ValidationStatus);
                tx.Commit();
                return id;
            }
        }

        private static void InsertLabels(SQLiteConnection c, SQLiteTransaction tx, string owner, long id, IEnumerable<string> labels)
        {
            if (labels == null) return;
            foreach (string l in labels.Where(x => !string.IsNullOrWhiteSpace(x)))
                using (var cmd = Cmd(c, "INSERT INTO AssessmentLabels(OwnerType, OwnerID, Label) VALUES (@o,@i,@l);", tx,
                    "@o", owner, "@i", id, "@l", l))
                    cmd.ExecuteNonQuery();
        }

        public static List<ResearcherEvaluation> EvaluationsFor(string scanSessionId)
        {
            var list = new List<ResearcherEvaluation>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM ResearcherEvaluations WHERE ScanSessionID=@s ORDER BY EvaluationID DESC;", null, "@s", scanSessionId))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new ResearcherEvaluation
                    {
                        EvaluationID = L(r, "EvaluationID") ?? 0,
                        ScanSessionID = S(r, "ScanSessionID"),
                        ResearcherID = S(r, "ResearcherID"),
                        ResearcherClassification = S(r, "ResearcherClassification"),
                        AgreementWithAI = S(r, "AgreementWithAI"),
                        Notes = S(r, "Notes"),
                        FrontComment = S(r, "FrontComment"),
                        LeftComment = S(r, "LeftComment"),
                        RightComment = S(r, "RightComment"),
                        EvaluationDate = S(r, "EvaluationDate")
                    });
            return list;
        }

        public static List<DermatologistValidation> ValidationsFor(string scanSessionId)
        {
            var list = new List<DermatologistValidation>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM DermatologistValidations WHERE ScanSessionID=@s ORDER BY ValidationID DESC;", null, "@s", scanSessionId))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new DermatologistValidation
                    {
                        ValidationID = L(r, "ValidationID") ?? 0,
                        ScanSessionID = S(r, "ScanSessionID"),
                        DermatologistID = S(r, "DermatologistID"),
                        ValidatorName = S(r, "ValidatorName"),
                        ProfessionalRole = S(r, "ProfessionalRole"),
                        CredentialReference = S(r, "CredentialReference"),
                        DermatologistAssessment = S(r, "DermatologistAssessment"),
                        AgreementWithAI = S(r, "AgreementWithAI"),
                        AgreementWithResearcher = S(r, "AgreementWithResearcher"),
                        Notes = S(r, "Notes"),
                        ValidationDate = S(r, "ValidationDate"),
                        ValidationStatus = S(r, "ValidationStatus")
                    });
            return list;
        }

        // --------------------------------------------------------- fitzpatrick --

        public static List<FitzpatrickAssessment> FitzpatrickFor(string participantId)
        {
            var list = new List<FitzpatrickAssessment>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT * FROM FitzpatrickAssessments WHERE ParticipantID=@p ORDER BY FitzpatrickAssessmentID DESC;", null, "@p", participantId))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new FitzpatrickAssessment
                    {
                        FitzpatrickAssessmentID = L(r, "FitzpatrickAssessmentID") ?? 0,
                        ParticipantID = S(r, "ParticipantID"),
                        ScanSessionID = S(r, "ScanSessionID"),
                        SourceType = S(r, "SourceType"),
                        ManualType = S(r, "ManualType"),
                        PredictedType = S(r, "PredictedType"),
                        ModelVersionID = L(r, "ModelVersionID"),
                        ScoreData = S(r, "ScoreData"),
                        AssessmentDate = S(r, "AssessmentDate"),
                        Status = S(r, "Status")
                    });
            return list;
        }

        // ---------------------------------------------------------- settings --

        public static Dictionary<string, string> LoadSettings()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, "SELECT Key, Value FROM SystemSettings;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) d[S(r, "Key")] = S(r, "Value");
            return d;
        }

        /// <summary>
        /// Save changed settings; every change is written to the audit log with old and new value.
        /// <paramref name="effective"/> = the values in force before (stored or default), so an
        /// unchanged default is not logged as a change.
        /// </summary>
        public static int SaveSettings(IDictionary<string, string> values, IDictionary<string, string> effective = null)
        {
            Dictionary<string, string> old = LoadSettings();
            if (effective != null)
                foreach (KeyValuePair<string, string> kv in effective)
                    if (!old.ContainsKey(kv.Key)) old[kv.Key] = kv.Value;
            int changed = 0;
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                foreach (KeyValuePair<string, string> kv in values)
                {
                    string prev;
                    old.TryGetValue(kv.Key, out prev);
                    if (prev == kv.Value) continue;

                    using (var cmd = Cmd(c, @"INSERT INTO SystemSettings(Key, Value, UpdatedAt, UpdatedBy) VALUES (@k,@v,@at,@by)
                        ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value, UpdatedAt=excluded.UpdatedAt, UpdatedBy=excluded.UpdatedBy;", tx,
                        "@k", kv.Key, "@v", kv.Value, "@at", Now(), "@by", AppSession.ActorId))
                        cmd.ExecuteNonQuery();
                    Audit(c, tx, "Setting changed", "SystemSettings", kv.Key,
                          (prev ?? "(default)") + " -> " + kv.Value + " | model " + ModelInfo.Current.RunName);
                    changed++;
                }
                tx.Commit();
            }
            return changed;
        }

        // ---------------------------------------------------- research stats --
        //
        // A participant whose record status is "Withdrawn" is excluded from every figure
        // below (their data stays stored but is not counted or analysed).

        /// <summary>SQL condition on a ScanSessions row aliased "s": not a withdrawn participant's scan.</summary>
        private const string ActiveSession =
            "(s.ParticipantID IS NULL OR s.ParticipantID NOT IN (SELECT ParticipantID FROM Participants WHERE Status = 'Withdrawn'))";

        public static int TotalParticipants() => Count("SELECT COUNT(*) FROM Participants WHERE Status <> 'Withdrawn';");
        public static int WithdrawnParticipants() => Count("SELECT COUNT(*) FROM Participants WHERE Status = 'Withdrawn';");
        public static int TotalSessions() => Count("SELECT COUNT(*) FROM ScanSessions s WHERE " + ActiveSession + ";");
        public static int TotalCapturedImages() => Count(
            "SELECT COUNT(*) FROM CaptureViews v JOIN ScanSessions s ON s.ScanSessionID = v.ScanSessionID WHERE " + ActiveSession + ";");
        public static int TotalAttributionMaps() => Count(
            "SELECT COUNT(*) FROM AttributionMaps m JOIN CaptureViews v ON v.CaptureID = m.CaptureID JOIN ScanSessions s ON s.ScanSessionID = v.ScanSessionID WHERE " + ActiveSession + ";");
        public static int CompletedAnalyses() => Count("SELECT COUNT(*) FROM ScanSessions s WHERE s.ScanStatus='complete' AND " + ActiveSession + ";");
        public static int PendingVerificationCount() => Count(@"SELECT COUNT(*) FROM ScanSessions s WHERE s.ViewsCompleted > 0 AND " + ActiveSession + @"
            AND NOT EXISTS (SELECT 1 FROM ResearcherEvaluations e WHERE e.ScanSessionID=s.ScanSessionID);");
        public static int PendingDermatologistCount() => Count(@"SELECT COUNT(*) FROM ScanSessions s WHERE s.ViewsCompleted > 0 AND " + ActiveSession + @"
            AND NOT EXISTS (SELECT 1 FROM DermatologistValidations d WHERE d.ScanSessionID=s.ScanSessionID AND d.ValidationStatus='Completed');");

        /// <summary>
        /// Scans of participants whose LATEST consent is "Agreed" with the optional future-model-use
        /// permission ticked, and who have not withdrawn. Any future export of images for model
        /// improvement must be limited to these.
        /// </summary>
        public static int SessionsWithFutureUseConsent() => Count(@"SELECT COUNT(*) FROM ScanSessions s WHERE " + ActiveSession + @"
            AND s.ParticipantID IS NOT NULL
            AND EXISTS (SELECT 1 FROM Consents k WHERE k.ParticipantID = s.ParticipantID AND k.Decision = 'Agreed' AND k.FutureModelUseConsent = 1
                        AND k.ConsentID = (SELECT MAX(ConsentID) FROM Consents x WHERE x.ParticipantID = s.ParticipantID));");

        private static List<CountRow> Group(string sql)
        {
            var list = new List<CountRow>();
            using (var c = StudyDatabase.Open())
            using (var cmd = Cmd(c, sql))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    list.Add(new CountRow { Label = S(r, "Label") ?? "(not recorded)", Count = (int)(L(r, "N") ?? 0) });
            return list;
        }

        public static List<CountRow> AiClassDistribution() => Group(
            "SELECT s.OverallPredictedClass AS Label, COUNT(*) AS N FROM ScanSessions s WHERE s.OverallPredictedClass IS NOT NULL AND " + ActiveSession + " GROUP BY 1 ORDER BY 2 DESC;");

        /// <summary>Latest researcher label per session (a later evaluation supersedes an earlier one for statistics).</summary>
        public static List<CountRow> ResearcherDistribution() => Group(@"
            SELECT e.ResearcherClassification AS Label, COUNT(*) AS N
            FROM ResearcherEvaluations e JOIN ScanSessions s ON s.ScanSessionID = e.ScanSessionID
            WHERE " + ActiveSession + @"
              AND e.EvaluationID = (SELECT MAX(EvaluationID) FROM ResearcherEvaluations x WHERE x.ScanSessionID = e.ScanSessionID)
            GROUP BY 1 ORDER BY 2 DESC;");

        public static List<CountRow> DermatologistDistribution() => Group(@"
            SELECT d.DermatologistAssessment AS Label, COUNT(*) AS N
            FROM DermatologistValidations d JOIN ScanSessions s ON s.ScanSessionID = d.ScanSessionID
            WHERE " + ActiveSession + @" AND d.ValidationStatus='Completed'
              AND d.ValidationID = (SELECT MAX(ValidationID) FROM DermatologistValidations x WHERE x.ScanSessionID = d.ScanSessionID AND x.ValidationStatus='Completed')
            GROUP BY 1 ORDER BY 2 DESC;");

        public static List<CountRow> SexDistribution() => Group(
            "SELECT COALESCE(NULLIF(Sex,''),'(not recorded)') AS Label, COUNT(*) AS N FROM Participants WHERE Status <> 'Withdrawn' GROUP BY 1 ORDER BY 2 DESC;");

        public static List<CountRow> AgeDistribution() => Group(@"
            SELECT CASE WHEN Age IS NULL THEN '(not recorded)'
                        WHEN Age < 18 THEN 'under 18'
                        WHEN Age < 25 THEN '18-24'
                        WHEN Age < 35 THEN '25-34'
                        WHEN Age < 45 THEN '35-44'
                        WHEN Age < 55 THEN '45-54'
                        ELSE '55+' END AS Label, COUNT(*) AS N
            FROM Participants WHERE Status <> 'Withdrawn' GROUP BY 1 ORDER BY 1;");

        public static List<CountRow> FitzpatrickManualDistribution() => Group(@"
            SELECT COALESCE(NULLIF(k.FitzpatrickManual,''),'Not collected') AS Label, COUNT(*) AS N
            FROM SkinProfiles k JOIN Participants p ON p.ParticipantID = k.ParticipantID
            WHERE p.Status <> 'Withdrawn' GROUP BY 1 ORDER BY 1;");

        public static List<CountRow> ViewCompletion() => Group(@"
            SELECT CASE WHEN s.ScanMode='Single' THEN 'Single capture'
                        ELSE s.ViewsCompleted || ' of ' || s.ViewsExpected || ' views' END AS Label, COUNT(*) AS N
            FROM ScanSessions s WHERE " + ActiveSession + " GROUP BY 1 ORDER BY 1 DESC;");

        public static List<CountRow> ModelVersionDistribution() => Group(@"
            SELECT COALESCE(m.RunName,'unknown') || ' / ' || substr(COALESCE(m.OnnxSha256,'unknown'),1,12) AS Label, COUNT(*) AS N
            FROM ScanSessions s LEFT JOIN ModelVersions m ON m.ModelVersionID = s.ModelVersionID
            WHERE " + ActiveSession + " GROUP BY 1 ORDER BY 2 DESC;");

        /// <summary>AI overall class vs the latest researcher label (single-label evaluations only).</summary>
        public static List<CountRow> AiVsResearcherAgreement() => Group(@"
            SELECT CASE WHEN e.ResearcherClassification = s.OverallPredictedClass THEN 'Agree'
                        WHEN instr(e.ResearcherClassification, ',') > 0 THEN 'Multiple reference labels'
                        ELSE 'Disagree' END AS Label, COUNT(*) AS N
            FROM ScanSessions s JOIN ResearcherEvaluations e ON e.ScanSessionID = s.ScanSessionID
            WHERE " + ActiveSession + @"
              AND e.EvaluationID = (SELECT MAX(EvaluationID) FROM ResearcherEvaluations x WHERE x.ScanSessionID = s.ScanSessionID)
              AND s.OverallPredictedClass IS NOT NULL
            GROUP BY 1 ORDER BY 2 DESC;");

        public static List<CountRow> AiVsDermatologistAgreement() => Group(@"
            SELECT CASE WHEN d.DermatologistAssessment = s.OverallPredictedClass THEN 'Agree'
                        WHEN instr(d.DermatologistAssessment, ',') > 0 THEN 'Multiple reference labels'
                        ELSE 'Disagree' END AS Label, COUNT(*) AS N
            FROM ScanSessions s JOIN DermatologistValidations d ON d.ScanSessionID = s.ScanSessionID
            WHERE " + ActiveSession + @" AND d.ValidationStatus='Completed'
              AND d.ValidationID = (SELECT MAX(ValidationID) FROM DermatologistValidations x WHERE x.ScanSessionID = s.ScanSessionID AND x.ValidationStatus='Completed')
              AND s.OverallPredictedClass IS NOT NULL
            GROUP BY 1 ORDER BY 2 DESC;");
    }
}
