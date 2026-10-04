// ============================================================================
// StudyDatabase.cs  -  namespace SkinDevApp.Data
//
// Schema for the PrecisionSkin study database. Lives in the same SQLite file as
// the legacy PatientLogs table (DatabaseHelper.cs), which is left untouched.
//
// Tables (UI Architecture v3, section J):
//    1 Participants             8 AnalysisImages
//    2 SkinProfiles             9 AttributionMaps
//    3 Consents                10 ResearcherEvaluations
//    4 ScanSessions            11 DermatologistValidations
//    5 CaptureViews            12 ModelVersions
//    6 AnalysisResults         13 FitzpatrickAssessments  (reserved / future AI)
//    7 ClassScores             14 SystemSettings + AuditLog
//  + AssessmentLabels: lets one human assessment carry several reference labels.
//
// Rules enforced here or in StudyRepository:
//   * ParticipantID is generated (PS-0001...) inside a write transaction; a UNIQUE
//     index on Seq prevents duplicates.
//   * AI rows (sessions, views, scores, maps) are written only by SessionImporter
//     and are frozen once the session is finalized.
//   * Human assessments are separate, append-only tables.
// ============================================================================

using System;
using System.Data.SQLite;
using System.IO;

namespace SkinDevApp.Data
{
    /// <summary>
    /// Where the study database and captured images live. By default they stay on this PC. To share one
    /// database between several PCs, point every PC at the same synced folder (OneDrive, Google Drive,
    /// Dropbox, a network share) with either:
    ///   - the environment variable LUMYVUE_DATA_DIR, or
    ///   - a text file %LOCALAPPDATA%\LUMYVUE\data_folder.txt whose first line is the folder path.
    /// The database file and the Captures folder are then read and written inside that folder.
    /// </summary>
    public static class DataLocation
    {
        private static readonly string LocalDb =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lumyvue_db.sqlite");

        private static readonly string LocalCaptures = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUMYVUE", "Captures");

        /// <summary>The shared data folder, or null when this PC keeps its own data.</summary>
        public static string SharedFolder { get; } = FindSharedFolder();

        public static bool IsShared => SharedFolder != null;

        public static string DbFile { get; } = ResolveDb();

        public static string CapturesDir => SharedFolder != null ? Path.Combine(SharedFolder, "Captures") : LocalCaptures;

        private static string FindSharedFolder()
        {
            try
            {
                string p = Environment.GetEnvironmentVariable("LUMYVUE_DATA_DIR");
                if (string.IsNullOrWhiteSpace(p))
                {
                    string cfg = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUMYVUE", "data_folder.txt");
                    if (File.Exists(cfg))
                        foreach (string line in File.ReadAllLines(cfg))
                            if (!string.IsNullOrWhiteSpace(line)) { p = line; break; }
                }
                if (string.IsNullOrWhiteSpace(p)) return null;
                p = p.Trim().Trim('"');
                Directory.CreateDirectory(p);
                return p;
            }
            catch { return null; }
        }

        private static string ResolveDb()
        {
            if (SharedFolder == null) return LocalDb;
            string shared = Path.Combine(SharedFolder, "lumyvue_db.sqlite");
            try
            {
                // First PC to join: bring its existing local data (accounts, scans) into the shared folder.
                if (!File.Exists(shared) && File.Exists(LocalDb)) File.Copy(LocalDb, shared);
            }
            catch { }
            return shared;
        }

        /// <summary>
        /// Records store absolute paths. If a saved path does not exist on this PC (another PC wrote it),
        /// re-point it at this PC's Captures folder.
        /// </summary>
        public static string Rebase(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                if (File.Exists(path) || Directory.Exists(path)) return path;
                int i = path.IndexOf("\\Captures\\", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return path;
                return Path.Combine(CapturesDir, path.Substring(i + 10));
            }
            catch { return path; }
        }
    }

    public static class StudyDatabase
    {
        public const int SchemaVersion = 1;

        public static string DbPath { get; } = DataLocation.DbFile;

        private static string ConnectionString => "Data Source=" + DbPath + ";Version=3;Foreign Keys=True;";

        public static bool IsReady { get; private set; }
        public static string LastError { get; private set; }

        public static SQLiteConnection Open()
        {
            var c = new SQLiteConnection(ConnectionString);
            c.Open();
            using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;", c))
                cmd.ExecuteNonQuery();
            return c;
        }

        public static void Initialize()
        {
            try
            {
                if (!File.Exists(DbPath)) SQLiteConnection.CreateFile(DbPath);

                using (var c = Open())
                using (var tx = c.BeginTransaction())
                {
                    Exec(c, tx, Schema);
                    Exec(c, tx, "INSERT OR IGNORE INTO SystemSettings(Key, Value, UpdatedAt, UpdatedBy) VALUES ('schema_version', '" +
                                SchemaVersion + "', datetime('now','localtime'), 'system');");
                    tx.Commit();
                }

                IsReady = true;
                LastError = null;
            }
            catch (Exception ex)
            {
                IsReady = false;
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine("[StudyDatabase] " + ex);
            }
        }

        private static void Exec(SQLiteConnection c, SQLiteTransaction tx, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c, tx)) cmd.ExecuteNonQuery();
        }

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS Participants (
    ParticipantID   TEXT PRIMARY KEY,
    Seq             INTEGER NOT NULL UNIQUE,
    RegisteredAt    TEXT NOT NULL,
    FirstName       TEXT,
    MiddleName      TEXT,
    LastName        TEXT,
    DateOfBirth     TEXT,
    Age             INTEGER,
    Sex             TEXT,
    Contact         TEXT,
    OperatorID      TEXT,
    Status          TEXT NOT NULL DEFAULT 'Active',
    UpdatedAt       TEXT
);

CREATE TABLE IF NOT EXISTS SkinProfiles (
    ProfileID             INTEGER PRIMARY KEY AUTOINCREMENT,
    ParticipantID         TEXT NOT NULL UNIQUE REFERENCES Participants(ParticipantID),
    GeneralSkinType       TEXT,
    FitzpatrickManual     TEXT,
    FitzpatrickSource     TEXT,
    Sensitivity           TEXT,
    Concerns              TEXT,
    ConcernOther          TEXT,
    Regions               TEXT,
    RegionOther           TEXT,
    ConcernDuration       TEXT,
    Cleanser              TEXT,
    Moisturizer           TEXT,
    Sunscreen             TEXT,
    AcneTreatment         TEXT,
    EczemaTreatment       TEXT,
    PigmentationTreatment TEXT,
    OtherProducts         TEXT,
    RecentProcedures      TEXT,
    OtherResponses        TEXT,
    UpdatedAt             TEXT
);

CREATE TABLE IF NOT EXISTS Consents (
    ConsentID              INTEGER PRIMARY KEY AUTOINCREMENT,
    ParticipantID          TEXT NOT NULL REFERENCES Participants(ParticipantID),
    ResearchInfoUnderstood INTEGER NOT NULL,
    VoluntaryParticipation INTEGER NOT NULL,
    ImageCaptureConsent    INTEGER NOT NULL,
    ImageProcessingConsent INTEGER NOT NULL,
    FutureModelUseConsent  INTEGER NOT NULL,
    Decision               TEXT NOT NULL,
    RecordedAt             TEXT NOT NULL,
    OperatorID             TEXT
);
CREATE INDEX IF NOT EXISTS IX_Consents_Participant ON Consents(ParticipantID);

CREATE TABLE IF NOT EXISTS ModelVersions (
    ModelVersionID  INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelName       TEXT NOT NULL,
    Architecture    TEXT,
    RunName         TEXT,
    OnnxSha256      TEXT,
    KerasSha256     TEXT,
    ServiceSha256   TEXT,
    ServiceVersion  TEXT,
    CreatedUtc      TEXT,
    Classes         TEXT,
    FirstSeenAt     TEXT NOT NULL,
    UNIQUE (OnnxSha256, KerasSha256, ServiceSha256)
);

CREATE TABLE IF NOT EXISTS ScanSessions (
    ScanSessionID         TEXT PRIMARY KEY,
    SessionLabel          TEXT,
    ParticipantID         TEXT REFERENCES Participants(ParticipantID),
    LegacyPatientLabel    TEXT,
    StartedAt             TEXT NOT NULL,
    CompletedAt           TEXT,
    OverallPredictedClass TEXT,
    OverallScore          REAL,
    FusionMethod          TEXT,
    ViewDisagreement      INTEGER NOT NULL DEFAULT 0,
    ViewsCompleted        INTEGER NOT NULL DEFAULT 0,
    ViewsExpected         INTEGER NOT NULL DEFAULT 3,
    ScanStatus            TEXT NOT NULL,
    ScanMode              TEXT NOT NULL DEFAULT 'MultiView',
    Finalized             INTEGER NOT NULL DEFAULT 0,
    ModelVersionID        INTEGER REFERENCES ModelVersions(ModelVersionID),
    SessionFolder         TEXT,
    OperatorID            TEXT,
    FusionJson            TEXT,
    ImportedAt            TEXT
);
CREATE INDEX IF NOT EXISTS IX_ScanSessions_Participant ON ScanSessions(ParticipantID);

CREATE TABLE IF NOT EXISTS CaptureViews (
    CaptureID          TEXT PRIMARY KEY,
    ScanSessionID      TEXT NOT NULL REFERENCES ScanSessions(ScanSessionID),
    ViewType           TEXT NOT NULL,
    FrameID            INTEGER,
    CapturedAt         TEXT,
    Trigger            TEXT,
    OriginalImagePath  TEXT,
    AnalysedImagePath  TEXT,
    ImageWidth         INTEGER,
    ImageHeight        INTEGER,
    Sharpness          REAL,
    Brightness         REAL,
    UnderExposed       REAL,
    OverExposed        REAL,
    Motion             REAL,
    PoseYaw            REAL,
    PosePitch          REAL,
    PoseRoll           REAL,
    PoseVerified       INTEGER NOT NULL DEFAULT 0,
    FaceFound          INTEGER NOT NULL DEFAULT 0,
    StabilityScore     REAL,
    FramesHeldStable   INTEGER,
    QualityWeight      REAL,
    QualityStatus      TEXT,
    RecordFolder       TEXT,
    UNIQUE (ScanSessionID, ViewType)
);

CREATE TABLE IF NOT EXISTS AnalysisResults (
    PredictionID     INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID        TEXT NOT NULL UNIQUE REFERENCES CaptureViews(CaptureID),
    Runtime          TEXT NOT NULL,
    ModelVersionID   INTEGER REFERENCES ModelVersions(ModelVersionID),
    PredictedClass   TEXT,
    Confidence       REAL,
    RunnerUpClass    TEXT,
    MarginPercent    REAL,
    StabilisedClass  TEXT,
    KerasSameTop     INTEGER,
    KerasMaxAbsDiff  REAL,
    ConsistencyWarning TEXT,
    CreatedAt        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ClassScores (
    ScoreID                 INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID               TEXT NOT NULL UNIQUE REFERENCES CaptureViews(CaptureID),
    AcneScore               REAL NOT NULL,
    HyperpigmentationScore  REAL NOT NULL,
    EczemaScore             REAL NOT NULL,
    NormalScore             REAL NOT NULL,
    PredictedClass          TEXT,
    Confidence              REAL
);

CREATE TABLE IF NOT EXISTS AnalysisImages (
    ImageID     INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID   TEXT NOT NULL REFERENCES CaptureViews(CaptureID),
    ImageType   TEXT NOT NULL,
    Path        TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AnalysisImages_Capture ON AnalysisImages(CaptureID);

CREATE TABLE IF NOT EXISTS AttributionMaps (
    MapID                    INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID                TEXT NOT NULL REFERENCES CaptureViews(CaptureID),
    TargetClass              TEXT NOT NULL,
    TargetClassIndex         INTEGER NOT NULL,
    TargetLayer              TEXT,
    Method                   TEXT,
    RawMapPath               TEXT,
    RenderedMapPath          TEXT,
    OverlayPath              TEXT,
    RelativeStrength         REAL,
    Diffuse                  INTEGER,
    TopZone                  TEXT,
    SimilarityDebugMetadata  TEXT,
    GradCAMVersion           TEXT,
    UNIQUE (CaptureID, TargetClassIndex)
);

CREATE TABLE IF NOT EXISTS LocalizationRuns (
    RunID           INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID       TEXT NOT NULL UNIQUE REFERENCES CaptureViews(CaptureID),
    Status          TEXT NOT NULL,
    ErrorText       TEXT,
    ModelFile       TEXT,
    ModelSha256     TEXT,
    ModelTag        TEXT,
    ImageSize       INTEGER,
    ThresholdsJson  TEXT,
    LatencyMs       REAL,
    BoxCount        INTEGER,
    OverlayPath     TEXT,
    CombinedPath    TEXT
);

CREATE TABLE IF NOT EXISTS LesionDetections (
    DetectionID  INTEGER PRIMARY KEY AUTOINCREMENT,
    CaptureID    TEXT NOT NULL REFERENCES CaptureViews(CaptureID),
    ClassName    TEXT NOT NULL,
    ClassIndex   INTEGER NOT NULL,
    Confidence   REAL NOT NULL,
    X0 REAL, Y0 REAL, X1 REAL, Y1 REAL,
    NX0 REAL, NY0 REAL, NX1 REAL, NY1 REAL
);
CREATE INDEX IF NOT EXISTS IX_LesionDetections_Capture ON LesionDetections(CaptureID);

CREATE TABLE IF NOT EXISTS ResearcherEvaluations (
    EvaluationID             INTEGER PRIMARY KEY AUTOINCREMENT,
    ScanSessionID            TEXT NOT NULL REFERENCES ScanSessions(ScanSessionID),
    ResearcherID             TEXT NOT NULL,
    ResearcherClassification TEXT NOT NULL,
    AgreementWithAI          TEXT,
    Notes                    TEXT,
    FrontComment             TEXT,
    LeftComment              TEXT,
    RightComment             TEXT,
    EvaluationDate           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ResearcherEvaluations_Session ON ResearcherEvaluations(ScanSessionID);

CREATE TABLE IF NOT EXISTS DermatologistValidations (
    ValidationID             INTEGER PRIMARY KEY AUTOINCREMENT,
    ScanSessionID            TEXT NOT NULL REFERENCES ScanSessions(ScanSessionID),
    DermatologistID          TEXT NOT NULL,
    ValidatorName            TEXT,
    ProfessionalRole         TEXT NOT NULL,
    CredentialReference      TEXT,
    DermatologistAssessment  TEXT,
    AgreementWithAI          TEXT,
    AgreementWithResearcher  TEXT,
    Notes                    TEXT,
    ValidationDate           TEXT NOT NULL,
    ValidationStatus         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_DermatologistValidations_Session ON DermatologistValidations(ScanSessionID);

CREATE TABLE IF NOT EXISTS AssessmentLabels (
    LabelID      INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerType    TEXT NOT NULL,
    OwnerID      INTEGER NOT NULL,
    Label        TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AssessmentLabels_Owner ON AssessmentLabels(OwnerType, OwnerID);

CREATE TABLE IF NOT EXISTS FitzpatrickAssessments (
    FitzpatrickAssessmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    ParticipantID           TEXT REFERENCES Participants(ParticipantID),
    ScanSessionID           TEXT REFERENCES ScanSessions(ScanSessionID),
    SourceType              TEXT NOT NULL,
    ManualType              TEXT,
    PredictedType           TEXT,
    ModelVersionID          INTEGER REFERENCES ModelVersions(ModelVersionID),
    ScoreData               TEXT,
    AssessmentDate          TEXT NOT NULL,
    Status                  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS SystemSettings (
    Key        TEXT PRIMARY KEY,
    Value      TEXT,
    UpdatedAt  TEXT,
    UpdatedBy  TEXT
);

CREATE TABLE IF NOT EXISTS ResearcherAccounts (
    Username           TEXT PRIMARY KEY COLLATE NOCASE,
    Salt               BLOB NOT NULL,
    Hash               BLOB NOT NULL,
    Iterations         INTEGER NOT NULL,
    CreatedAt          TEXT NOT NULL,
    CreatedBy          TEXT,
    PasswordChangedAt  TEXT
);

CREATE TABLE IF NOT EXISTS AuditLog (
    AuditID   INTEGER PRIMARY KEY AUTOINCREMENT,
    At        TEXT NOT NULL,
    Actor     TEXT,
    Action    TEXT NOT NULL,
    Entity    TEXT,
    EntityID  TEXT,
    Details   TEXT
);
";
    }
}
