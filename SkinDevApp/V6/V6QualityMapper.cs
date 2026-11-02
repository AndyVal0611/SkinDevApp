// ============================================================================
// V6QualityMapper.cs  -  namespace SkinDevApp.V6
//
// Maps what the application's own frame-quality analysis measured onto the quality flags the V6 decision logic uses
// (BLOCKING_QUALITY in 09_stages_trust.cellsrc: low_resolution, blur, underexposed, overexposed, face_not_found_or_wrong_crop).
//
// It uses the application's OWN verdicts (SharpEnough, brightness limits) instead of the notebook's heuristic numbers, because the two
// analysers measure on different images and scales. The notebook's glare / white-balance / compression / overlay / uneven-lighting checks have
// no equivalent in the application, so they are NOT checked: Coverage says so, and the review contract still applies to every result.
// Pure: no WPF, no OpenCV types.
// ============================================================================

using System;

namespace SkinDevApp.V6
{
    public static class V6QualityMapper
    {
        public const string CoverageText = "resolution, sharpness, exposure and face presence checked; glare, white balance, compression, overlays and uneven lighting are not checked";

        public static QualityInput From(bool cascadeAvailable, bool faceFound, bool sharpEnough, double brightness, double underExposedFraction, double overExposedFraction,
                                        int width, int height, double minBrightness = 45.0, double maxBrightness = 215.0,
                                        double underExposedMax = 0.20, double overExposedMax = 0.08, int minSidePx = 224, bool faceBlocking = true)
        {
            var q = new QualityInput { Assessed = true, Coverage = CoverageText };
            if (Math.Min(width, height) < minSidePx) q.Blocking.Add("low_resolution");
            if (!sharpEnough) q.Blocking.Add("blur");
            if (brightness < minBrightness || underExposedFraction > underExposedMax) q.Blocking.Add("underexposed");
            if (brightness > maxBrightness || overExposedFraction > overExposedMax) q.Blocking.Add("overexposed");
            // A missing face blocks a LIVE capture (the person can simply retake it). For an UPLOADED photograph (faceBlocking = false) it is only a warning: close-up crops
            // never contain a whole face. A warning still raises a review flag, so such a photograph can never be called Normal.
            if (cascadeAvailable && !faceFound) { if (faceBlocking) q.Blocking.Add("face_not_found_or_wrong_crop"); else q.Warnings.Add("face_not_found"); }
            // face_detector_unavailable is information only (as in the notebook): face presence simply could not be checked
            return q;
        }

        /// <summary>Plain-language advice for each blocking flag (same wording as the notebook).</summary>
        public static string Advice(string flag)
        {
            switch (flag)
            {
                case "low_resolution": return "Move closer or use a higher-resolution camera setting.";
                case "blur": return "Hold still or refocus and retake the photograph.";
                case "underexposed": return "Increase the lighting evenly on the face.";
                case "overexposed": return "Reduce direct light and avoid reflections.";
                case "face_not_found_or_wrong_crop": return "Face the camera with the whole face in the frame.";
                default: return null;
            }
        }
    }
}
