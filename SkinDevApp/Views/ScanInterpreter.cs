// ScanInterpreter.cs - decides how the two independent AI outputs are PRESENTED together. No WPF or database code.
//
// Two separate models look at the same photograph:
//   * the MobileNetV2 classifier gives one score per category (Acne, Hyperpigmentation, Eczema, Normal) that adds up to 100%;
//   * the YOLO lesion detectors give candidate boxes, each with its own detector confidence.
// They answer different questions. Neither is ever used to change the other, and nothing here is written back to the scan record:
// the interpretation is recomputed from the stored scores and boxes every time it is shown, so a dermatologist's review
// can never overwrite or alter the original AI prediction.
//
// The result has two independent parts:
//   * a CATEGORY: Consistent, Mixed, ClassificationOnly or Unavailable, and
//   * UNCERTAINTY FLAGS: low confidence, weak match, close margin. A result can be Mixed AND uncertain.
//
// Detector evidence is kept per condition in three states, because "no boxes" can mean two very different things:
//   NotAvailable            no detector covering this condition ran on the photograph(s), so nothing is known;
//   NoQualifyingDetections  a covering detector ran and kept no box above its (unchanged) thresholds;
//   Detected                a covering detector kept at least one box.
// A box is a candidate location, never a diagnosis; how many boxes there are says nothing about severity or prevalence,
// so counts are not used for any decision and are not put into the interpretation text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkinDevApp.Views
{
    /// <summary>
    /// The cut-offs the interpretation wording uses. They are PROVISIONAL RESEARCH PARAMETERS: they were chosen to match the
    /// wording bands already shown to users, and have NOT been validated against dermatologist-annotated data. Until that
    /// evaluation is done they must not be read as clinical decision thresholds. They affect only what is displayed; they never
    /// touch the models, the detection thresholds or the stored scores. Override in App.config (appSettings) without a rebuild.
    /// </summary>
    public sealed class InterpretationSettings
    {
        public const string KeyLowConfidence = "Interpretation.LowConfidencePercent";
        public const string KeyAgreement = "Interpretation.AgreementPercent";
        public const string KeyCloseMargin = "Interpretation.CloseMarginPoints";

        /// <summary>Top classifier score below this: no category stands out (flag LowConfidence).</summary>
        public double LowConfidencePercent { get; set; } = 40;
        /// <summary>Top classifier score below this: the match is weaker than "moderate" (flag WeakMatch).</summary>
        public double AgreementPercent { get; set; } = 60;
        /// <summary>Top two categories closer than this many percentage points are not clearly separated (flag CloseMargin).</summary>
        public double CloseMarginPoints { get; set; } = 10;

        public const string ProvisionalNote =
            "Interpretation cut-offs are provisional research parameters and have not been validated against dermatologist-annotated data.";

        public static InterpretationSettings Default => new InterpretationSettings();

        /// <summary>Reads overrides through <paramref name="get"/> (e.g. ConfigurationManager.AppSettings). Missing or invalid values keep the defaults.</summary>
        public static InterpretationSettings From(Func<string, string> get)
        {
            var s = new InterpretationSettings();
            double v;
            if (Try(get, KeyLowConfidence, out v)) s.LowConfidencePercent = v;
            if (Try(get, KeyAgreement, out v)) s.AgreementPercent = v;
            if (Try(get, KeyCloseMargin, out v)) s.CloseMarginPoints = v;
            // an inconsistent set (agreement below low-confidence) would make the bands meaningless: fall back to the defaults
            if (s.AgreementPercent < s.LowConfidencePercent) return Default;
            return s;
        }

        private static bool Try(Func<string, string> get, string key, out double v)
        {
            v = 0;
            string raw = get == null ? null : get(key);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 100;
        }
    }

    public enum InterpretationCategory
    {
        /// <summary>No classifier scores to interpret.</summary>
        Unavailable,
        /// <summary>The classifier's top category is not confirmed or contradicted by any detector finding (see the per-condition detector states).</summary>
        ClassificationOnly,
        /// <summary>A covering detector kept boxes for the classifier's top condition, and for no other condition.</summary>
        Consistent,
        /// <summary>The outputs point at different things, or boxes were kept for more than one condition.</summary>
        Mixed
    }

    [Flags]
    public enum UncertaintyFlags
    {
        None = 0,
        /// <summary>Top classifier score is below the low-confidence cut-off.</summary>
        LowConfidence = 1,
        /// <summary>Top score is at least the low-confidence cut-off but below the agreement cut-off.</summary>
        WeakMatch = 2,
        /// <summary>The top two categories are within the margin of each other.</summary>
        CloseMargin = 4
    }

    public enum DetectorState
    {
        /// <summary>No detector covering this condition ran on the photograph(s).</summary>
        NotAvailable,
        /// <summary>A covering detector ran and kept no box.</summary>
        NoQualifyingDetections,
        /// <summary>A covering detector kept at least one box.</summary>
        Detected
    }

    /// <summary>One stored detector box, reduced to what the interpretation needs.</summary>
    public sealed class LesionFinding
    {
        public string ClassName;            // Acne / Hyperpigmentation / Eczema
        public double Confidence;           // detector confidence of this box (0-1), NOT a classifier percentage
    }

    /// <summary>What the detector side knows about ONE photograph.</summary>
    public sealed class ViewEvidence
    {
        public IList<LesionFinding> Lesions = new List<LesionFinding>();
        /// <summary>Acne, Hyperpigmentation, Eczema: did a detector covering this condition run successfully on this photograph?</summary>
        public bool[] Covered = new bool[3];
    }

    /// <summary>Detector evidence for one condition over all analysed photographs. Box counts are deliberately absent.</summary>
    public sealed class ConditionEvidence
    {
        public string ClassName;
        public DetectorState State;
        public int ViewsAssessed;           // photographs where a covering detector ran (or produced a box)
        public int ViewsWithBoxes;
        public int ViewsTotal;
        /// <summary>Highest per-box detector confidence (0-1) for this condition; null when nothing was detected.</summary>
        public double? MaxConfidence;
    }

    public sealed class ScanInterpretation
    {
        public InterpretationCategory Category;
        public UncertaintyFlags Flags;
        public string Headline;
        public string Detail;
        /// <summary>Why the result is uncertain (empty when no flag is set).</summary>
        public string UncertaintyLine = "";
        /// <summary>Image-level scores, labelled as such.</summary>
        public string ClassifierLine = "";
        /// <summary>Acne, Hyperpigmentation, Eczema.</summary>
        public List<ConditionEvidence> Conditions = new List<ConditionEvidence>();
        public List<string> DetectedConditions = new List<string>();
        public string Caveat = ScanInterpreter.BoxCaveat;
        public string ProvisionalNote = InterpretationSettings.ProvisionalNote;
        public bool ReviewRecommended;
        /// <summary>0 = fine, 1 = look closer (same scale as ClinicalText.Agreement).</summary>
        public int Level;
        public string TopClass;
        public double TopPercent;

        public bool IsUncertain => Flags != UncertaintyFlags.None;
    }

    public static class ScanInterpreter
    {
        public const string MixedHeadline = "Mixed AI Findings — Review Recommended";
        public const string BoxCaveat =
            "Boxes are candidate locations from separate detectors. They do not confirm a diagnosis, how many there are says nothing about severity or how common a condition is, " +
            "and the box confidence is a different measure from the classifier percentages.";

        public static readonly string[] Classes = { "Acne", "Hyperpigmentation", "Eczema", "Normal" };

        /// <summary>The cut-offs in use. Replaced once at start-up from App.config; tests may pass their own.</summary>
        public static InterpretationSettings Settings { get; set; } = InterpretationSettings.Default;

        private static string Name(string cls) => cls == "Normal" ? "Normal Skin" : cls;
        private static string Pct(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";

        /// <summary>One photograph.</summary>
        public static ScanInterpretation Interpret(double[] percent, ViewEvidence evidence, InterpretationSettings settings = null) =>
            Interpret(percent, evidence == null ? null : new[] { evidence }, settings);

        /// <summary>One photograph, boxes only (every condition that has a box counts as covered).</summary>
        public static ScanInterpretation Interpret(double[] percent, IList<LesionFinding> lesions, bool[] covered, InterpretationSettings settings = null) =>
            Interpret(percent, new[] { new ViewEvidence { Lesions = lesions ?? new List<LesionFinding>(), Covered = covered ?? new bool[3] } }, settings);

        /// <summary>
        /// Several photographs (e.g. the three views): <paramref name="percent"/> is the pooled score; the detector evidence of every photograph is combined per condition.
        /// </summary>
        public static ScanInterpretation Interpret(double[] percent, IList<ViewEvidence> views, InterpretationSettings settings = null)
        {
            settings = settings ?? Settings;
            var r = new ScanInterpretation();
            if (percent == null || percent.Length != 4 || percent.Any(p => double.IsNaN(p) || p < 0))
            {
                r.Category = InterpretationCategory.Unavailable;
                r.Headline = "No AI result";
                r.Detail = "This photograph was not scored by the classifier.";
                r.Caveat = null;
                r.ProvisionalNote = null;
                return r;
            }

            views = views ?? new List<ViewEvidence>();
            int top = 0;
            for (int i = 1; i < 4; i++) if (percent[i] > percent[top]) top = i;
            double second = 0;
            for (int i = 0; i < 4; i++) if (i != top && percent[i] > second) second = percent[i];
            r.TopClass = Classes[top];
            r.TopPercent = percent[top];

            // ---- detector evidence per condition ------------------------------------------
            for (int c = 0; c < 3; c++)
            {
                string cls = Classes[c];
                var e = new ConditionEvidence { ClassName = cls, ViewsTotal = views.Count };
                foreach (ViewEvidence v in views)
                {
                    List<LesionFinding> boxes = (v?.Lesions ?? new List<LesionFinding>()).Where(l => l != null && l.ClassName == cls).ToList();
                    bool covered = v != null && v.Covered != null && v.Covered.Length > c && v.Covered[c];
                    if (boxes.Count > 0)
                    {
                        e.ViewsAssessed++; e.ViewsWithBoxes++;
                        double m = boxes.Max(b => b.Confidence);
                        if (!e.MaxConfidence.HasValue || m > e.MaxConfidence.Value) e.MaxConfidence = m;
                    }
                    else if (covered) e.ViewsAssessed++;
                }
                e.State = e.ViewsWithBoxes > 0 ? DetectorState.Detected : e.ViewsAssessed > 0 ? DetectorState.NoQualifyingDetections : DetectorState.NotAvailable;
                r.Conditions.Add(e);
            }
            r.DetectedConditions = r.Conditions.Where(e => e.State == DetectorState.Detected).Select(e => e.ClassName).ToList();
            string found = string.Join(", ", r.DetectedConditions).ToLowerInvariant();

            r.ClassifierLine = "Image-level classification (how closely the whole photograph resembles each category; the four add up to 100%): " +
                string.Join(", ", Enumerable.Range(0, 4).OrderByDescending(i => percent[i]).Select(i => Name(Classes[i]) + " " + Pct(percent[i]))) + ".";

            // ---- uncertainty flags: independent of the category ----------------------------
            if (r.TopPercent < settings.LowConfidencePercent) r.Flags |= UncertaintyFlags.LowConfidence;
            else if (r.TopPercent < settings.AgreementPercent) r.Flags |= UncertaintyFlags.WeakMatch;
            if (r.TopPercent - second < settings.CloseMarginPoints) r.Flags |= UncertaintyFlags.CloseMargin;
            r.UncertaintyLine = UncertaintyText(r, second, settings);

            // ---- category -----------------------------------------------------------------
            string topClass = r.TopClass;
            ConditionEvidence topEv = r.Conditions.FirstOrDefault(e => e.ClassName == topClass);   // null when Normal
            bool conflict = r.DetectedConditions.Count > 0 &&
                            (topClass == "Normal" || r.DetectedConditions.Count > 1 || !r.DetectedConditions.Contains(topClass));

            if (conflict)
            {
                r.Category = InterpretationCategory.Mixed;
                r.Headline = MixedHeadline;
                if (topClass == "Normal")
                    r.Detail = "The classifier's closest category is Normal Skin (" + Pct(r.TopPercent) + "), but the separate detectors marked possible " + found +
                               ". The two outputs disagree, so this is not shown as healthy skin and is not resolved either way.";
                else if (!r.DetectedConditions.Contains(topClass))
                    r.Detail = "The classifier's closest category is " + Name(topClass) + " (" + Pct(r.TopPercent) + "), but the detectors marked possible " + found +
                               " instead. The two outputs point at different things.";
                else
                    r.Detail = "The detectors marked possible areas for more than one condition (" + found + "); the classifier's closest category is " + Name(topClass) +
                               " (" + Pct(r.TopPercent) + "). More than one finding may be present; the marked areas do not confirm any of them.";
            }
            else if (topEv != null && topEv.State == DetectorState.Detected)
            {
                r.Category = InterpretationCategory.Consistent;
                r.Headline = "Consistent AI Findings";
                r.Detail = "The classifier's closest category is " + Name(topClass) + " (" + Pct(r.TopPercent) + ") and the " + Name(topClass).ToLowerInvariant() +
                           " detector marked possible areas as well. The two outputs support each other; this is not a confirmed diagnosis.";
            }
            else
            {
                r.Category = InterpretationCategory.ClassificationOnly;
                r.Headline = "Classification Only";
                r.Detail = ClassificationOnlyDetail(r, topEv);
            }

            r.ReviewRecommended = r.Category == InterpretationCategory.Mixed || r.IsUncertain;
            r.Level = r.ReviewRecommended ? 1 : 0;
            return r;
        }

        private static string ClassificationOnlyDetail(ScanInterpretation r, ConditionEvidence topEv)
        {
            string head = "The classifier's closest category is " + Name(r.TopClass) + " (" + Pct(r.TopPercent) + "). ";
            if (topEv != null)
            {
                if (topEv.State == DetectorState.NotAvailable)
                    return head + "No " + topEv.ClassName.ToLowerInvariant() + " detector was available or run for this photograph, so nothing supports or contradicts this from the detectors.";
                return head + "The " + topEv.ClassName.ToLowerInvariant() + " detector ran but kept no qualifying detections. That neither confirms nor rules out " +
                       topEv.ClassName.ToLowerInvariant() + "; it may not mark every presentation.";
            }
            // Normal with no boxes anywhere
            if (r.Conditions.All(e => e.State == DetectorState.NotAvailable))
                return head + "No lesion detector was available or run, so there is no detector evidence either way.";
            var missing = r.Conditions.Where(e => e.State == DetectorState.NotAvailable).Select(e => e.ClassName.ToLowerInvariant()).ToList();
            return head + "The detectors that ran kept no qualifying detections. That does not confirm healthy skin" +
                   (missing.Count > 0 ? ", and there was no detector for " + string.Join(" or ", missing) + "." : ".");
        }

        private static string UncertaintyText(ScanInterpretation r, double second, InterpretationSettings s)
        {
            var parts = new List<string>();
            if ((r.Flags & UncertaintyFlags.LowConfidence) != 0)
                parts.Add("low classifier confidence (best match " + Pct(r.TopPercent) + ", below " + s.LowConfidencePercent.ToString("0", CultureInfo.InvariantCulture) + "%)");
            if ((r.Flags & UncertaintyFlags.WeakMatch) != 0)
                parts.Add("weak match (" + Pct(r.TopPercent) + ", below " + s.AgreementPercent.ToString("0", CultureInfo.InvariantCulture) + "%)");
            if ((r.Flags & UncertaintyFlags.CloseMargin) != 0)
                parts.Add("the top two categories are close (" + Pct(r.TopPercent) + " vs " + Pct(second) + ")");
            return parts.Count == 0 ? "" : "Uncertainty: " + string.Join("; ", parts) + ".";
        }
    }

    /// <summary>
    /// The wording every screen, the receipt and the report share, so they cannot drift apart. All of it is derived from one
    /// <see cref="ScanInterpretation"/>; none of it changes or replaces the stored scores or boxes.
    /// </summary>
    public static class InterpretationText
    {
        /// <summary>"Acne: possible areas marked in 2 of 3 photos (highest detector confidence 0.47)"</summary>
        public static string ConditionLine(ConditionEvidence e)
        {
            string of = e.ViewsTotal > 1 ? " of " + e.ViewsTotal + " photos" : "";
            switch (e.State)
            {
                case DetectorState.Detected:
                    return e.ClassName + ": possible areas marked" + (e.ViewsTotal > 1 ? " in " + e.ViewsWithBoxes + of : "") +
                           " (highest detector confidence " + (e.MaxConfidence ?? 0).ToString("0.00", CultureInfo.InvariantCulture) + ")";
                case DetectorState.NoQualifyingDetections:
                    return e.ClassName + ": detector ran, no qualifying detections" + (e.ViewsTotal > 1 && e.ViewsAssessed < e.ViewsTotal ? " (" + e.ViewsAssessed + of + " checked)" : "");
                default:
                    return e.ClassName + ": no detector available or run";
            }
        }

        /// <summary>Headline, explanation and uncertainty, in this order. Empty entries are left out.</summary>
        public static List<string> Summary(ScanInterpretation i)
        {
            var l = new List<string> { i.Headline, i.Detail };
            if (!string.IsNullOrEmpty(i.UncertaintyLine)) l.Add(i.UncertaintyLine);
            return l;
        }

        /// <summary>The per-condition detector lines (Acne, Hyperpigmentation, Eczema).</summary>
        public static List<string> DetectorLines(ScanInterpretation i) => i.Conditions.Select(ConditionLine).ToList();

        /// <summary>Everything, for screens and the A4 report.</summary>
        public static List<string> Full(ScanInterpretation i)
        {
            var l = Summary(i);
            if (i.Category == InterpretationCategory.Unavailable) return l;
            l.Add(i.ClassifierLine);
            l.Add("Lesion detectors (separate models; confidence is per box, not a percentage of the photograph):");
            l.AddRange(DetectorLines(i).Select(x => "  " + x));
            l.Add(i.Caveat);
            l.Add(i.ProvisionalNote);
            return l;
        }
    }
}
