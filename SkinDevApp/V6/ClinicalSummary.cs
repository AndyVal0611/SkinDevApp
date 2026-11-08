// ============================================================================
// ClinicalSummary.cs  -  namespace SkinDevApp.V6
//
// The clinician-facing reading of the final model's result, used by every surface (Results page, patient records, dermatologist review,
// thermal receipt, A4 report, database screen) so they all say the same thing in the same words. Pure: no WPF, no database.
//
// Three clear outcomes for a photograph (or a scan of several photographs):
//   * Condition detected          the model supports a finding and nothing needs a second look
//   * Needs review                something is uncertain, possible, conflicting, unusual, or a finding comes with a review note
//   * No condition detected       every condition was checked with a working detector and nothing was found
// plus two technical outcomes: the photograph must be retaken, or the analysis could not be completed.
//
// Wording rules: never "diagnosis", never raw logits; certainty is given as High / Moderate / Low from the calibrated probability;
// regions are named from where the lesion detectors marked areas on the face (forehead, cheeks, nose, mouth / chin).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkinDevApp.V6
{
    public enum ClinicalCategory { ConditionDetected, NeedsReview, NoConditionDetected, Retake, NotCompleted }

    public sealed class ClinicalFinding
    {
        public string Condition;
        public string Status;            // Detected | Possible | Not detected | Inconclusive
        public string Certainty;         // High | Moderate | Low
        public double Probability;       // calibrated, 0..1 (shown only in technical detail)
        public double Threshold;
        public bool NeedsReview;
        public int BoxCount;             // areas the detector marked on the face for this condition (as drawn on the marked-areas picture)
        public double BestBoxConfidence; // highest confidence among those areas (0 when none)
        public List<string> Regions = new List<string>();
        /// <summary>"model" (the fusion decided) or "lesion detector" (a detector-led condition whose detector marked an area the fusion did not confirm).</summary>
        public string Source = ClinicalSummary.SourceModel;

        public bool IsDetected => Status == ClinicalSummary.Detected;
        public bool ByDetector => Source == ClinicalSummary.SourceDetector;
        public string RegionsText => Regions.Count == 0 ? "" : string.Join(", ", Regions);
    }

    public sealed class ClinicalSummary
    {
        public const string Detected = "Detected", Possible = "Possible", NotDetected = "Not detected", Inconclusive = "Inconclusive";
        public const string SourceModel = "model", SourceDetector = "lesion detector";

        /// <summary>
        /// Conditions where the lesion detector leads: an area its detector marks is reported as Detected even when the fusion did not confirm it.
        /// Hyperpigmentation: the detector finds most hyperpigmentation photographs the whole-face reading misses (about 91% on the fair set),
        /// at the cost of more false alarms on clear skin, so these findings carry at most moderate certainty and say where they came from.
        /// </summary>
        public static readonly string[] DetectorLed = { "Hyperpigmentation" };

        /// <summary>Version of these reading rules; stored results are re-read when it changes (RecordsMaintenance).</summary>
        public const int RulesVersion = 2;

        /// <summary>Areas marked on the face over all photographs of the scan (a count of boxes, never a lesion count).</summary>
        public int MarkedAreas;

        /// <summary>Plain notes about how the result was reached (shown under the findings; not reasons for review).</summary>
        public List<string> Notes = new List<string>();

        public ClinicalCategory Category;
        public string Title;                 // e.g. "Condition detected: Acne"
        public string Explanation;           // one plain sentence
        public List<ClinicalFinding> Findings = new List<ClinicalFinding>();
        public List<string> ReviewReasons = new List<string>();

        public IEnumerable<ClinicalFinding> DetectedFindings => Findings.Where(f => f.IsDetected);
        public IEnumerable<ClinicalFinding> ReviewFindings => Findings.Where(f => f.Status == Possible || f.Status == Inconclusive);

        public string CategoryLabel => Label(Category);

        public static string Label(ClinicalCategory c)
        {
            switch (c)
            {
                case ClinicalCategory.ConditionDetected: return "Condition detected";
                case ClinicalCategory.NeedsReview: return "Needs review";
                case ClinicalCategory.NoConditionDetected: return "No condition detected";
                case ClinicalCategory.Retake: return "Retake photograph";
                default: return "Analysis not completed";
            }
        }

        public static ClinicalCategory Parse(string label)
        {
            foreach (ClinicalCategory c in Enum.GetValues(typeof(ClinicalCategory)))
                if (string.Equals(Label(c), label, StringComparison.OrdinalIgnoreCase)) return c;
            return ClinicalCategory.NotCompleted;
        }

        public const string Disclaimer = "AI-assisted screening result, not a diagnosis. It is not clinically validated and must be confirmed by a dermatologist.";

        // ------------------------------------------------------------------ certainty and regions

        public static string CertaintyFor(string status, double p)
        {
            if (status == Detected) return p >= 0.80 ? "High" : p >= 0.60 ? "Moderate" : "Low";
            if (status == NotDetected) return p <= 0.10 ? "High" : p <= 0.25 ? "Moderate" : "Low";
            return "Low";
        }

        /// <summary>Facial region of a point (normalised to the photograph) relative to the face box (normalised x, y, w, h), using the subject's own left / right.</summary>
        public static string RegionOf(double cx, double cy, double[] faceBox, bool mirrored = false)
        {
            double bx = 0, by = 0, bw = 1, bh = 1;
            if (faceBox != null && faceBox.Length == 4 && faceBox[2] > 0 && faceBox[3] > 0) { bx = faceBox[0]; by = faceBox[1]; bw = faceBox[2]; bh = faceBox[3]; }
            double u = (cx - bx) / bw, v = (cy - by) / bh;
            if (u < -0.25 || u > 1.25 || v < -0.25 || v > 1.25) return null;                 // outside the face region: hair, background, clothing
            if (v < 0.30) return "forehead";
            if (v >= 0.72) return "mouth / chin";
            if (u >= 0.30 && u <= 0.70) return "nose";
            bool imageLeft = u < 0.30;
            bool subjectRight = mirrored ? !imageLeft : imageLeft;                           // un-mirrored camera: the subject's right is on the image left
            return subjectRight ? "right cheek" : "left cheek";
        }

        private static readonly string[] RegionOrder = { "forehead", "right cheek", "left cheek", "nose", "mouth / chin" };

        public static List<string> RegionsFor(UnifiedAssessment a, string condition, double[] faceBox)
        {
            var set = new HashSet<string>();
            if (a == null || a.FrameWidth <= 0 || a.FrameHeight <= 0) return new List<string>();
            foreach (V6Box b in a.Boxes.Where(x => x.Condition == condition))
            {
                string r = RegionOf((b.X0 + b.X1) / 2.0 / a.FrameWidth, (b.Y0 + b.Y1) / 2.0 / a.FrameHeight, faceBox);
                if (r != null) set.Add(r);
            }
            return RegionOrder.Where(set.Contains).ToList();
        }

        // ------------------------------------------------------------------ one photograph

        public static ClinicalSummary From(UnifiedAssessment a, double[] faceBox = null)
        {
            var s = new ClinicalSummary();
            if (a == null) { s.Category = ClinicalCategory.NotCompleted; s.Title = "Analysis not completed"; s.Explanation = "No result from the final model is stored for this photograph."; return s; }

            foreach (ConditionAssessment c in a.Conditions)
            {
                string status = c.Verdict == "present" ? Detected : c.Verdict == "possible" ? Possible : c.Verdict == "absent" ? NotDetected : Inconclusive;
                string source = SourceModel, certainty = CertaintyFor(status, c.CalibratedProbability);
                var onFace = a.FrameWidth > 0 && a.FrameHeight > 0
                    ? a.Boxes.Where(b => b.Condition == c.Condition && b.Deployed &&
                                         RegionOf((b.X0 + b.X1) / 2.0 / a.FrameWidth, (b.Y0 + b.Y1) / 2.0 / a.FrameHeight, faceBox) != null).ToList()
                    : a.Boxes.Where(b => b.Condition == c.Condition && b.Deployed).ToList();
                if (status != Detected && c.DetectorAvailable && c.DetectorFired && DetectorLed.Contains(c.Condition))
                {
                    // detector-led condition: the marked area counts as a finding; certainty from the detector's own confidence on the face, never "high"
                    double conf = onFace.Select(b => (double)b.Confidence).DefaultIfEmpty(0).Max();
                    status = Detected; source = SourceDetector; certainty = conf >= 0.50 ? "Moderate" : "Low";
                    s.Notes.Add(c.Condition + " was found by its lesion detector; the whole-face reading did not confirm it.");
                }
                var f = new ClinicalFinding
                {
                    Condition = c.Condition, Status = status, Probability = c.CalibratedProbability, Threshold = c.Threshold, Source = source,
                    Certainty = certainty, NeedsReview = status == Possible || status == Inconclusive
                };
                if (status == Detected || status == Possible) f.Regions = RegionsFor(a, c.Condition, faceBox);
                f.BoxCount = onFace.Count; f.BestBoxConfidence = onFace.Select(b => (double)b.Confidence).DefaultIfEmpty(0).Max();
                s.Findings.Add(f);
            }
            s.ReviewReasons = ReviewReasonsFor(a, s.Findings);
            s.MarkedAreas = s.Findings.Sum(f => f.BoxCount);
            // an uncertain model state with no specific reason left still needs review, unless the uncertainty was a detector-led finding resolved above
            if (a.State == V6States.UncertainReviewRequired && s.ReviewReasons.Count == 0 && !s.Findings.Any(f => f.ByDetector))
                s.ReviewReasons.Add("The AI marked this result as uncertain.");

            switch (a.State)
            {
                case V6States.RecaptureRequired:
                    // no condition reading is reported for a photograph that has to be retaken: only why
                    s.Category = ClinicalCategory.Retake; s.Title = "Photograph needs to be retaken";
                    s.Explanation = "The photograph quality was not good enough for a reliable reading, so no condition is reported for it.";
                    s.Findings.Clear(); s.ReviewReasons = QualityReasons(a); return s;
                case V6States.InferenceError:
                    s.Category = ClinicalCategory.NotCompleted; s.Title = "Analysis not completed";
                    s.Explanation = "The AI analysis could not be completed for this photograph."; s.Findings.Clear(); s.ReviewReasons.Clear(); return s;
            }

            List<string> detected = s.DetectedFindings.Select(f => f.Condition).ToList();
            // review when there is a concrete reason (uncertain or possible conditions, conflict, unusual photo, quality, missing detector)
            bool review = s.ReviewReasons.Count > 0 || s.Findings.Any(f => f.NeedsReview) || a.State == V6States.OutOfScopeReviewRequired;
            if (!review && detected.Count > 0)
            {
                s.Category = ClinicalCategory.ConditionDetected;
                s.Title = "Condition detected: " + Join(detected);
                s.Explanation = "The AI found signs of " + Join(detected).ToLowerInvariant() + ". A dermatologist must confirm this.";
            }
            else if (!review)
            {
                s.Category = ClinicalCategory.NoConditionDetected;
                s.Title = "No condition detected";
                s.Explanation = "The AI found no signs of acne, hyperpigmentation or eczema in this photograph. This does not rule out a skin condition.";
            }
            else
            {
                s.Category = ClinicalCategory.NeedsReview;
                s.Title = detected.Count > 0 ? Join(detected) + " detected · needs review" : "Needs dermatologist review";
                s.Explanation = detected.Count > 0
                    ? "The AI found signs of " + Join(detected).ToLowerInvariant() + ", and part of the result needs a dermatologist's review."
                    : "The AI result is not clear enough to call. A dermatologist should review this photograph.";
            }
            return s;
        }

        private static List<string> QualityReasons(UnifiedAssessment a)
        {
            var r = new List<string>();
            foreach (string q in a.Flags.Where(x => x.StartsWith("quality_") && !x.StartsWith("quality_warning")))
                r.Add(V6QualityMapper.Advice(q.Substring("quality_".Length)) ?? "Photo quality: " + q.Substring("quality_".Length));
            if (r.Count == 0) r.Add("Please retake the photograph with even lighting, the face in focus and filling the guide.");
            return r;
        }

        private static List<string> ReviewReasonsFor(UnifiedAssessment a, List<ClinicalFinding> findings)
        {
            var r = new List<string>();
            foreach (ClinicalFinding f in findings.Where(x => x.Status == Possible))
                r.Add(f.Condition + ": a lesion detector marked an area, but the overall model did not confirm it.");
            List<string> inconclusive = findings.Where(x => x.Status == Inconclusive).Select(x => x.Condition).ToList();
            if (inconclusive.Count > 0) r.Add(Join(inconclusive) + ": the result was inconclusive.");
            List<string> conflict = a.Flags.Where(x => x.StartsWith("cnn_yolo_conflict_")).Select(x => x.Substring("cnn_yolo_conflict_".Length))
                                     .Where(c => !findings.Any(f => f.Condition == c && (f.Status == Possible || f.ByDetector))).ToList();
            if (conflict.Count > 0) r.Add(Join(conflict) + ": the whole-face reading and the lesion detector disagree.");
            if (a.Flags.Contains("ood") || a.State == V6States.OutOfScopeReviewRequired) r.Add("The photograph looks unlike the photographs the system was trained on.");
            foreach (string q in a.Flags.Where(x => x.StartsWith("quality_") && !x.StartsWith("quality_warning")))
                r.Add(V6QualityMapper.Advice(q.Substring("quality_".Length)) ?? "Photo quality: " + q.Substring("quality_".Length));
            if (a.Flags.Any(x => x.StartsWith("quality_warning:face_not_found"))) r.Add("No face was found, so photo quality could not be fully checked.");
            if (a.Reasons.Any(x => x.StartsWith("detector unavailable"))) r.Add("A lesion detector was not available, so absence cannot be confirmed.");
            if (a.Reasons.Any(x => x.StartsWith("image quality was not assessed"))) r.Add("Photo quality could not be checked, so a clear result cannot be confirmed.");
            return r;
        }

        // ------------------------------------------------------------------ several photographs (one scan)

        /// <summary>Scan-level summary over the photographs of one scan (Front / Left / Right or a single photograph).</summary>
        public static ClinicalSummary ForScan(IList<KeyValuePair<string, ClinicalSummary>> views)
        {
            var s = new ClinicalSummary();
            if (views == null || views.Count == 0) { s.Category = ClinicalCategory.NotCompleted; s.Title = "Analysis not completed"; s.Explanation = "No result from the final model is stored for this scan."; return s; }
            if (views.Count == 1) return views[0].Value;

            foreach (string cond in V6Conditions.Names)
            {
                var per = views.Select(v => new { View = v.Key, F = v.Value.Findings.FirstOrDefault(f => f.Condition == cond) }).Where(x => x.F != null).ToList();
                if (per.Count == 0) continue;
                ClinicalFinding best = per.Select(x => x.F).OrderBy(RankFinding).ThenByDescending(f => f.Probability).First();
                var f2 = new ClinicalFinding
                {
                    Condition = cond, Status = best.Status, Certainty = best.Certainty, Probability = best.Probability, Threshold = best.Threshold, Source = best.Source,
                    BoxCount = per.Sum(x => x.F.BoxCount), BestBoxConfidence = per.Max(x => x.F.BestBoxConfidence),
                    NeedsReview = per.Any(x => x.F.NeedsReview)
                };
                foreach (var x in per.Where(x => x.F.Regions.Count > 0)) f2.Regions.Add(x.View + ": " + x.F.RegionsText);
                s.Findings.Add(f2);
            }
            // the same reason on several photographs is stated once, naming the photographs: "Acne: ... (Front, Left side)"
            foreach (var g in views.SelectMany(v => v.Value.ReviewReasons.Distinct().Select(r => new { View = v.Key, Reason = r })).GroupBy(x => x.Reason))
            {
                var names = g.Select(x => x.View).Distinct().ToList();
                string reason = g.Key.TrimEnd('.');
                s.ReviewReasons.Add(names.Count == views.Count && views.Count > 1 ? reason + " (all photographs)." : reason + " (" + string.Join(", ", names) + ").");
            }

            foreach (var v in views) foreach (string n in v.Value.Notes) if (!s.Notes.Contains(n)) s.Notes.Add(n);
            s.MarkedAreas = views.Sum(v => v.Value.MarkedAreas);
            List<string> detected = s.DetectedFindings.Select(f => f.Condition).ToList();
            bool anyProblem = views.Any(v => v.Value.Category == ClinicalCategory.NeedsReview || v.Value.Category == ClinicalCategory.Retake || v.Value.Category == ClinicalCategory.NotCompleted);
            if (views.All(v => v.Value.Category == ClinicalCategory.NotCompleted)) { s.Category = ClinicalCategory.NotCompleted; s.Title = "Analysis not completed"; s.Explanation = "The AI analysis could not be completed for this scan."; }
            else if (anyProblem)
            {
                s.Category = ClinicalCategory.NeedsReview;
                s.Title = detected.Count > 0 ? Join(detected) + " detected · needs review" : "Needs dermatologist review";
                s.Explanation = "At least one photograph needs a dermatologist's review" + (detected.Count > 0 ? "; signs of " + Join(detected).ToLowerInvariant() + " were found." : ".");
            }
            else if (detected.Count > 0)
            {
                s.Category = ClinicalCategory.ConditionDetected; s.Title = "Condition detected: " + Join(detected);
                s.Explanation = "The AI found signs of " + Join(detected).ToLowerInvariant() + ". A dermatologist must confirm this.";
            }
            else { s.Category = ClinicalCategory.NoConditionDetected; s.Title = "No condition detected"; s.Explanation = "The AI found no signs of acne, hyperpigmentation or eczema in these photographs. This does not rule out a skin condition."; }
            return s;
        }

        private static int RankFinding(ClinicalFinding f) => f.Status == Detected ? (f.ByDetector ? 1 : 0) : 1 + Rank(f.Status);

        private static int Rank(string status) => status == Detected ? 0 : status == Possible ? 1 : status == Inconclusive ? 2 : 3;

        public static string Join(IList<string> names) =>
            names.Count == 0 ? "" : names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        /// <summary>"Detected · High certainty · forehead, right cheek" (no numbers).</summary>
        public static string FindingLine(ClinicalFinding f)
        {
            string line = f.ByDetector ? "Detected by the lesion detector" : f.Status;
            if (f.Status == Detected || f.Status == NotDetected) line += " · " + f.Certainty.ToLowerInvariant() + " certainty";
            else line += " · review needed";
            if (f.Regions.Count > 0) line += " · " + f.RegionsText;
            return line;
        }

        /// <summary>The confidence shown for a finding: the calibrated model probability when the model decided; the detector's own box confidence when the lesion detector found it;
        /// for "not detected" the confidence that the condition is absent (1 - probability); for possible / inconclusive the model probability, which is below the decision limit.</summary>
        public static string ConfidenceText(ClinicalFinding f)
        {
            if (f.ByDetector) return "Detector " + Percent(f.BestBoxConfidence);
            if (f.Status == Detected) return Percent(f.Probability);
            if (f.Status == NotDetected) return Percent(1.0 - f.Probability);
            return Percent(f.Probability) + " (below limit)";
        }

        /// <summary>One concise sentence: what was found, with certainty, and how many areas were marked.</summary>
        public static string SummaryLine(ClinicalSummary s)
        {
            if (s.Category == ClinicalCategory.Retake) return "No condition is reported: the photograph has to be retaken.";
            if (s.Category == ClinicalCategory.NotCompleted) return "The AI analysis could not be completed.";
            var parts = new List<string>();
            foreach (ClinicalFinding f in s.Findings.Where(x => x.Status == Detected))
                parts.Add(f.Condition + " detected" + (f.ByDetector ? " by the lesion detector" : "") + " (" + f.Certainty.ToLowerInvariant() + " certainty)");
            foreach (ClinicalFinding f in s.Findings.Where(x => x.Status == Possible || x.Status == Inconclusive))
                parts.Add(f.Condition + " " + f.Status.ToLowerInvariant() + " (review needed)");
            string line = parts.Count == 0 ? "No condition detected" : string.Join("; ", parts);
            line += s.MarkedAreas == 0 ? ". No areas were marked on the face." : ". " + s.MarkedAreas + (s.MarkedAreas == 1 ? " area was" : " areas were") + " marked on the face.";
            return line;
        }

        public static string Percent(double p) => (p * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
