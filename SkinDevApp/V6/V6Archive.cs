// ============================================================================
// V6Archive.cs  -  namespace SkinDevApp.V6
//
// Saves one UnifiedAssessment next to the scan's record.json (the same folder), so the Results page, like every historical view, only READS
// what was produced at scan time and never re-runs a model.
//
//   v6_assessment.json     {"schema", "created_utc", "ai_content_sha256", "assessment": {...}}   the hash seals the assessment text
//   v6_marked_areas.png    the analysed frame with the V6 supporting boxes (drawn only inside the face region + 25 %, display only)
//
// A file whose seal does not match is reported and is never shown. Nothing here changes record.json, the database or any V5 file.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.V6
{
    public static class V6Archive
    {
        public const string FileName = "v6_assessment.json";
        public const string MarkedFileName = "v6_marked_areas.png";
        public const string Schema = "lumyvue-v6-assessment/1.0";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize(UnifiedAssessment a) => JsonSerializer.Serialize(a, Options);

        public static string Seal(string assessmentJson)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(assessmentJson))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Writes v6_assessment.json (and v6_marked_areas.png when a frame is given) into the capture folder.</summary>
        public static void Write(string folder, UnifiedAssessment a, Mat frame = null, Rect? faceBox = null, string origin = null)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("capture folder is required");
            Directory.CreateDirectory(folder);
            string json = Serialize(a);
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();
                    w.WriteString("schema", Schema);
                    w.WriteString("created_utc", DateTime.UtcNow.ToString("o"));
                    w.WriteString("ai_content_sha256", Seal(json));
                    if (origin != null) w.WriteString("origin", origin);            // "Re-analysis": an older photograph assessed later by the final model
                    w.WritePropertyName("assessment"); w.WriteRawValue(json);
                    w.WriteEndObject();
                }
                File.WriteAllBytes(Path.Combine(folder, FileName), ms.ToArray());
            }
            if (frame != null && !frame.Empty())
            {
                using (Mat marked = DrawMarkedAreas(frame, a, faceBox))
                    File.WriteAllBytes(Path.Combine(folder, MarkedFileName), marked.ImEncode(".png"));
                V6Heatmaps.WriteAll(folder, a, frame, faceBox);              // V6 heat maps (detector evidence), same form as V5's
            }
        }

        /// <summary>"Scan" or "Re-analysis" as written into the file, or null for files written before the origin was recorded.</summary>
        public static string ReadOrigin(string folder)
        {
            try
            {
                string path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, FileName);
                if (path == null || !File.Exists(path)) return null;
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path)))
                    return doc.RootElement.TryGetProperty("origin", out JsonElement o) ? o.GetString() : null;
            }
            catch { return null; }
        }

        /// <summary>Returns the saved assessment, or null with a reason (missing file, wrong schema, hash mismatch, unreadable).</summary>
        public static UnifiedAssessment Read(string folder, out string problem)
        {
            problem = null;
            string path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, FileName);
            if (path == null || !File.Exists(path)) { problem = "no V6 assessment saved for this image"; return null; }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path)))
                {
                    JsonElement r = doc.RootElement;
                    if (r.GetProperty("schema").GetString() != Schema) { problem = "unknown V6 assessment schema"; return null; }
                    JsonElement body = r.GetProperty("assessment");
                    string raw = body.GetRawText();
                    if (!string.Equals(Seal(raw), r.GetProperty("ai_content_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    { problem = "the saved V6 assessment does not match its seal and was not shown"; return null; }
                    UnifiedAssessment a = JsonSerializer.Deserialize<UnifiedAssessment>(raw, Options);
                    if (a == null) { problem = "the saved V6 assessment is empty"; return null; }
                    return a;
                }
            }
            catch (Exception ex) { problem = "the saved V6 assessment could not be read: " + ex.Message; return null; }
        }

        /// <summary>Boxes outside the face region (+25 %) are not drawn (hair, background, clothing). This affects the picture only, never the assessment.</summary>
        public static Mat DrawMarkedAreas(Mat frame, UnifiedAssessment a, Rect? faceBox)
        {
            Mat img = frame.Clone();
            bool labels = a.Boxes.Count(x => x.Deployed) <= 8;           // many boxes: draw the rectangles only, so the labels do not pile up
            foreach (V6Box b in a.Boxes.Where(x => x.Deployed))
            {
                double cx = (b.X0 + b.X1) / 2.0, cy = (b.Y0 + b.Y1) / 2.0;
                if (faceBox.HasValue && faceBox.Value.Width > 0 && faceBox.Value.Height > 0)
                {
                    Rect f = faceBox.Value; double ex = 0.25;
                    if (cx < f.X - ex * f.Width || cx > f.X + f.Width + ex * f.Width || cy < f.Y - ex * f.Height || cy > f.Y + f.Height + ex * f.Height) continue;
                }
                int idx = Math.Max(0, V6Conditions.IndexOf(b.Condition));
                Scalar color = ClassPalette.ColorsBgr[idx];
                Cv2.Rectangle(img, new Point((int)b.X0, (int)b.Y0), new Point((int)b.X1, (int)b.Y1), color, 2);
                if (labels) Cv2.PutText(img, b.Condition + " " + b.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                            new Point((int)b.X0, Math.Max(12, (int)b.Y0 - 4)), HersheyFonts.HersheySimplex, 0.45, color, 1, LineTypes.AntiAlias);
            }
            return img;
        }
    }
}
