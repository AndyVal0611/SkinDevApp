// ============================================================================
// GalleryModel.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// Loads a saved scan (a session folder, or a single capture) into plain data
// for the comparison gallery. No WPF here, so it can be tested and reused.
//
//   Per view (Front / Left / Right):  Original | Acne | Hyperpigmentation |
//   Eczema | Normal, each as a card with the class score of THAT view.
//
// The "Original" card is analysed.png: the exact frame the classifier and
// Grad-CAM++ saw, so the overlays line up with it pixel for pixel. The
// full-resolution original.png stays in the same folder.
//
// Class order in the gallery follows the model: Acne, Hyperpigmentation,
// Eczema, Normal. Colours come from ClassPalette (red, blue, yellow/orange, green).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    public sealed class GalleryCard
    {
        public bool IsOriginal { get; set; }
        public int ClassIndex { get; set; } = -1;
        public string Title { get; set; } = "";
        public string ColorHex { get; set; } = "#9CA3AF";

        /// <summary>Image shown on the card (analysed frame, or the class overlay).</summary>
        public string ImagePath { get; set; }

        /// <summary>8-bit per-class normalised heatmap (null for the original card).</summary>
        public string HeatmapPath { get; set; }

        /// <summary>Raw un-normalised CAM (.npy), when saved.</summary>
        public string RawCamPath { get; set; }

        public double? ScorePercent { get; set; }
        public bool IsPredicted { get; set; }
        public bool Diffuse { get; set; }

        /// <summary>Score line shown under the image.</summary>
        public string ScoreText { get; set; } = "";

        /// <summary>Honest one-line description of the attribution (diffuse / strongest zone).</summary>
        public string Note { get; set; } = "";
    }

    public sealed class GalleryView
    {
        public ScanView View { get; set; }
        public string TabTitle { get; set; } = "";
        public string Folder { get; set; }
        public bool Available { get; set; }
        public CaptureRecord Record { get; set; }
        public List<GalleryCard> Cards { get; set; } = new List<GalleryCard>();

        public string SummaryLine { get; set; } = "";
        public string QualityText { get; set; } = "";
        public string StabilityText { get; set; } = "";
        public string PoseText { get; set; } = "";
        public string SimilarityText { get; set; } = "";
        public bool SimilarityHigh { get; set; }
    }

    public sealed class GalleryModel
    {
        public const string AttributionStatement =
            "Grad-CAM++ attribution maps show which image areas influenced the model's score for each class. " +
            "They are NOT lesion segmentation and NOT maps of diseased tissue. A weak or diffuse map is shown as it is.";

        public string Title { get; set; } = "";
        public string SessionFolder { get; set; }
        public SessionIndex Session { get; set; }
        public FusedResult Fusion => Session != null ? Session.Fusion : null;
        public List<GalleryView> Views { get; set; } = new List<GalleryView>();
        public bool IsSession => Session != null;

        /// <summary>Open a session folder, a view folder inside one, or a single capture folder.</summary>
        public static GalleryModel Load(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("Folder not found: " + folder);

            string sessionFolder = null;
            if (File.Exists(Path.Combine(folder, "session.json"))) sessionFolder = folder;
            else
            {
                string parent = Path.GetDirectoryName(folder.TrimEnd('\\', '/'));
                if (parent != null && File.Exists(Path.Combine(parent, "session.json"))) sessionFolder = parent;
            }

            var m = new GalleryModel();

            if (sessionFolder != null)
            {
                m.SessionFolder = sessionFolder;
                m.Session = MultiViewSession.Load(sessionFolder);
                m.Title = "Scan " + (m.Session?.SessionId ?? "").Substring(0, Math.Min(8, (m.Session?.SessionId ?? "").Length))
                          + "  |  patient " + (m.Session?.PatientId ?? "-")
                          + "  |  " + (m.Session?.StartedUtc ?? "");

                foreach (ScanView v in ScanViews.Sequence)
                    m.Views.Add(LoadView(Path.Combine(sessionFolder, ScanViews.Name(v)), v));
            }
            else
            {
                CaptureRecord rec = ReviewStore.LoadRecord(folder);
                if (rec == null) throw new InvalidOperationException("record.json not found in " + folder);
                m.Title = "Capture " + (rec.CaptureId ?? "").Substring(0, Math.Min(8, (rec.CaptureId ?? "").Length))
                          + "  |  " + rec.TimestampUtc + "  |  single view (no angle information)";
                GalleryView gv = LoadView(folder, ScanView.Any);
                gv.TabTitle = "Capture";
                m.Views.Add(gv);
            }

            return m;
        }

        public static GalleryView LoadView(string folder, ScanView view)
        {
            var gv = new GalleryView
            {
                View = view,
                TabTitle = view == ScanView.Any ? "Capture" : ScanViews.Title(view),
                Folder = folder
            };

            CaptureRecord rec = Directory.Exists(folder) ? ReviewStore.LoadRecord(folder) : null;
            if (rec == null)
            {
                gv.Available = false;
                gv.SummaryLine = "This view was not captured.";
                return gv;
            }

            gv.Available = true;
            gv.Record = rec;

            string analysed = rec.Files != null && rec.Files.ContainsKey("analysed") ? rec.Files["analysed"] : "analysed.png";
            string analysedPath = Path.Combine(folder, analysed);

            Dictionary<string, double> scores = rec.Scores != null ? rec.Scores.OnnxPercent : null;

            string topClass = rec.Primary != null ? rec.Primary.OnnxTopClass : null;

            gv.Cards.Add(new GalleryCard
            {
                IsOriginal = true,
                Title = "Original",
                ColorHex = "#374151",
                ImagePath = analysedPath,
                ScoreText = "Exact frame analysed (no heatmap)",
                Note = "Full-resolution capture: original.png"
            });

            for (int k = 0; k < ClassPalette.ClassCount; k++)
            {
                string name = ClassPalette.Names[k];
                MapSection ms = (rec.ClassMaps ?? new List<MapSection>())
                    .FirstOrDefault(x => string.Equals(x.Class, name, StringComparison.OrdinalIgnoreCase));

                var card = new GalleryCard
                {
                    ClassIndex = k,
                    Title = name + " CAM",
                    ColorHex = ClassPalette.Hex[k],
                    IsPredicted = string.Equals(name, topClass, StringComparison.OrdinalIgnoreCase)
                };

                double? score = null;
                if (scores != null && scores.ContainsKey(name)) score = scores[name];
                else if (ms != null) score = ms.ProbabilityPercent;
                card.ScorePercent = score;
                card.ScoreText = (score.HasValue ? "Score " + score.Value.ToString("0.0") + "%" : "Score n/a")
                                 + (card.IsPredicted ? "  (top class)" : "");

                if (ms != null)
                {
                    if (!string.IsNullOrEmpty(ms.OverlayFile)) card.ImagePath = Path.Combine(folder, ms.OverlayFile);
                    if (!string.IsNullOrEmpty(ms.HeatmapFile)) card.HeatmapPath = Path.Combine(folder, ms.HeatmapFile);
                    if (!string.IsNullOrEmpty(ms.RawCamFile)) card.RawCamPath = Path.Combine(folder, ms.RawCamFile);
                    card.Diffuse = ms.Diffuse;

                    if (ms.Diffuse)
                        card.Note = "Diffuse attribution: no single area stands out.";
                    else if (view == ScanView.Front || view == ScanView.Any)
                        card.Note = string.IsNullOrEmpty(ms.TopZone)
                            ? "Attribution is spread over the face."
                            : "Most influence: " + ms.TopZone + " (" + (ms.TopZoneShare * 100.0).ToString("0") + "% of the map). Approximate.";
                    else
                        card.Note = "Focal attribution. Facial-zone names are only given for the front view.";
                }
                else
                {
                    card.Note = "No Grad-CAM++ map was saved for this class" +
                                (rec.Notes != null && rec.Notes.Any(n => n.StartsWith("Grad-CAM++ maps unavailable"))
                                    ? " (service was unavailable)." : ".");
                }

                gv.Cards.Add(card);
            }

            // header texts
            gv.SummaryLine = rec.Primary != null
                ? "Top class: " + rec.Primary.OnnxTopClass + " (" + rec.Primary.OnnxConfidencePercent.ToString("0.0") + "% model score)"
                : "No prediction recorded";

            if (rec.Quality != null)
                gv.QualityText = "Sharpness " + rec.Quality.Sharpness.ToString("0.0")
                    + ", brightness " + rec.Quality.Brightness.ToString("0")
                    + ", under-exposed " + (rec.Quality.UnderExposed * 100).ToString("0") + "%"
                    + ", over-exposed " + (rec.Quality.OverExposed * 100).ToString("0") + "%";

            if (rec.Stability != null)
                gv.StabilityText = "Trigger " + rec.Stability.Trigger
                    + ", held stable " + rec.Stability.FramesHeldStable + "/" + rec.Stability.StableFramesRequired
                    + " frames, consistency " + rec.Stability.Consistency.ToString("0.00")
                    + ", motion mean/max " + rec.Stability.MotionMean.ToString("0.0") + "/" + rec.Stability.MotionMax.ToString("0.0")
                    + ", hold " + rec.Stability.HoldMs.ToString("0") + " ms";

            if (rec.Session != null)
            {
                gv.PoseText = (rec.Session.PoseGateVerified ? "Pose verified" : "Pose NOT verified (manual capture or pose check unavailable)")
                    + (rec.Session.YawRatio.HasValue ? ", yaw ratio " + rec.Session.YawRatio.Value.ToString("0.00") : "")
                    + (string.IsNullOrEmpty(rec.Session.ObservedView) ? "" : ", measured view " + rec.Session.ObservedView)
                    + ", quality weight " + rec.Session.QualityWeight.ToString("0.00");
            }

            ClassMapSimilarityDto sim = rec.ClassMapSimilarity;
            if (sim != null)
            {
                gv.SimilarityHigh = sim.MapsLargelyShared;
                gv.SimilarityText = "Agreement between the four class maps (raw CAMs): mean correlation r = "
                    + sim.MeanPairwisePearson.ToString("0.00") + " (" + sim.Level + ")"
                    + (sim.MapsLargelyShared
                        ? ". The maps share most of one pattern, so differences between them are small: compare them with care."
                        : ".");
            }
            else gv.SimilarityText = "Class-map agreement was not recorded for this capture.";

            return gv;
        }
    }
}
