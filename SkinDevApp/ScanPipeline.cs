// ============================================================================
// ScanPipeline.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// The FINAL analysis of one captured frame (auto, manual Snap or file upload):
//
//   exact frame  ->  all-class Grad-CAM++ (PNG, never dropped)  ->  archive
//
// Used by LiveGradCamController (auto/manual capture) and by the dashboard's
// "Run Aesthetic Analysis" button (uploaded images).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkinDevApp.AI;
using SkinDevApp.Data;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;

namespace SkinDevApp.Scanning
{
    public static class ScanPipeline
    {
        public const int ServiceHeatmapMaxSide = 320;

        /// <summary>
        /// Runs the all-class Grad-CAM++ on req.Working and archives everything.
        /// Takes ownership of <paramref name="req"/> (it is disposed with the result).
        /// Never throws: failures come back as Ok=false with Error set.
        /// </summary>
        public static async Task<ScanResult> FinalAnalysisAsync(CaptureRequest req, CancellationToken ct = default)
        {
            var result = new ScanResult { Request = req };

            try
            {
                byte[] png = req.Working.ImEncode(".png");   // lossless: identical pixels to what ONNX saw
                string frameId = "final-" + req.CaptureId.Substring(0, 8);

                ClassHeatmapSet set = null;
                string gradcamError = null;
                bool finalModel = SkinDevApp.V6.V6FeatureFlag.IsEnabled;     // the final model (V6) makes its own evidence maps and detector run below

                if (finalModel) gradcamError = "not used: the final model's evidence maps are saved instead";
                else
                using (GradCamAllResult r = await GradCamService
                    .ExplainAllClassesAsync(png, frameId, false, ServiceHeatmapMaxSide, ct, includeRawCam: true)
                    .ConfigureAwait(false))
                {
                    if (r.Ok && r.FrameId == frameId)
                    {
                        Mat thumb = MotionMeter.MakeThumb(req.Working);
                        Rect? faceBox = req.Quality != null ? req.Quality.FaceBox : (Rect?)null;
                        set = r.ToHeatmapSet(req.FrameId, thumb, req.Working.Size(), faceBox);
                    }
                    else
                    {
                        gradcamError = r.Ok ? "frame_id mismatch from service" : r.Error;
                    }
                }

                // ---- lesion localization: separate detector, SAME frozen frame, independent of Grad-CAM++ ----
                // A failure here never fails the scan: it is recorded (status Failed + message) and the scan is saved.
                LesionDetectionSet det = null;
                string detError = null;
                ScanSettings cfg = ScanSettings.Current;
                if (cfg.DetectorEnabled && !finalModel)
                {
                    try
                    {
                        double[] thr = cfg.DetectorThresholds();
                        det = await Task.Run(() => LesionEngine.Detect(req.Working, thr)).ConfigureAwait(false);
                        // boxes on hair / background / clothing are false by definition: keep only boxes whose centre is on the face (+25% margin)
                        if (req.Quality != null && req.Quality.FaceBox.HasValue)
                            LesionEngine.KeepInsideFace(det, req.Quality.FaceBox.Value);
                    }
                    catch (Exception dex)
                    {
                        detError = dex.GetType().Name + ": " + dex.Message;
                    }
                }

                // ---- the final model (V6) assessment: same frozen frame, saved next to record.json ----
                SkinDevApp.V6.UnifiedAssessment v6 = null;
                if (SkinDevApp.V6.V6FeatureFlag.IsEnabled)
                {
                    try { v6 = await Task.Run(() => SkinDevApp.V6.V6Service.Assess(req.Working, req.Quality, req.Trigger == "upload")).ConfigureAwait(false); }
                    catch (Exception v6ex) { v6 = SkinDevApp.V6.UnifiedAssessment.Failed(v6ex.GetType().Name + ": " + v6ex.Message); }
                }

                CaptureRecord record = null;
                ClassHeatmapSet setForSave = set;
                string err = gradcamError;

                string folder = await Task.Run(() =>
                {
                    CaptureRecord rec;
                    string f = CaptureArchive.Save(req, setForSave, err, out rec, det, detError);
                    record = rec;
                    return f;
                }).ConfigureAwait(false);

                if (v6 != null)
                {
                    try { SkinDevApp.V6.V6Archive.Write(folder, v6, req.Working, req.Quality != null ? req.Quality.FaceBox : (Rect?)null, SkinDevApp.V6.AIResultStore.OriginScan); }
                    catch (Exception wex) { v6.Reasons.Add("the V6 assessment could not be saved: " + wex.Message); }
                    result.V6 = v6;
                }

                result.Maps = set;
                result.Detections = det;
                result.Record = record;
                result.Folder = folder;
                result.Ok = true;
                if (set == null) result.Error = gradcamError;   // soft: scores still saved
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Build a CaptureRequest for an UPLOADED image (no live stability data).
        /// Runs ONNX and the quality analyser once. Takes a copy of the image.
        /// </summary>
        public static CaptureRequest BuildUploadRequest(Mat image, int workingWidth)
        {
            var req = new CaptureRequest
            {
                Trigger = "upload",
                Original = image.Clone(),
                Working = LiveGradCamController.ResizeToWidth(image, workingWidth)
            };

            req.Onnx = AiEngine.Predict(req.Working);

            // A throw-away analyser: positioning data for the record only.
            using (var analyzer = new FrameQualityAnalyzer())
                req.Quality = analyzer.Analyze(req.Working);

            return req;
        }
    }
}
