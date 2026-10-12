// ClinicalText.cs - turns the classifier's numbers into wording a dermatologist can read, without any WPF code.
//
// The model returns four percentages that add up to 100% (a softmax over Acne, Hyperpigmentation, Eczema, Normal).
// Dermatologists think in terms of "which condition does this look like, how clearly, and do the photographs agree",
// so this class says that in plain sentences. It never claims a diagnosis and never invents numbers: every sentence
// is built from the stored scores.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    /// <summary>The scores of one photograph, independent of where they were loaded from.</summary>
    public sealed class ViewReading
    {
        public string View;                 // Front / Left / Right / Single
        public double[] Percent;            // Acne, Hyperpigmentation, Eczema, Normal (0-100)
        public string Quality;              // "Good" or "Check: ..." (may be null)

        public int TopIndex { get { int t = 0; for (int i = 1; i < Percent.Length; i++) if (Percent[i] > Percent[t]) t = i; return t; } }
        public string TopClass => StudyText.Classes[TopIndex];
        public double TopPercent => Percent[TopIndex];
    }

    public static class ClinicalText
    {
        public const string HowToReadScores =
            "How to read these percentages: each bar shows how closely the photograph resembles one skin category, compared with the other three. " +
            "The four bars always add up to 100%. A longer bar means a stronger visual match. It is a decision aid, not a diagnosis and not the chance that the patient has the condition.";

        public static string Name(string cls) => cls == "Normal" ? "Normal Skin" : cls;

        public static List<ViewReading> Readings(SessionDetail d)
        {
            return d.Views.Where(v => v.Scores != null)
                .Select(v => new ViewReading { View = v.ViewType, Percent = v.Scores.AsArray(), Quality = v.QualityStatus })
                .ToList();
        }

        /// <summary>How clear the match is.</summary>
        public static string Strength(double percent)
        {
            if (percent >= 85) return "strong";
            if (percent >= 60) return "moderate";
            if (percent >= 40) return "weak";
            return "very weak";
        }

        private static string P(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";

        /// <summary>"Acne - moderate match (60%)"</summary>
        public static string Top(ViewReading v) => Name(v.TopClass) + " — " + Strength(v.TopPercent) + " match (" + P(v.TopPercent) + ")";

        /// <summary>The second-highest category, only when it is large enough to matter (10% or more).</summary>
        public static string NextClosest(ViewReading v)
        {
            int top = v.TopIndex;
            int second = -1;
            for (int i = 0; i < v.Percent.Length; i++)
                if (i != top && (second < 0 || v.Percent[i] > v.Percent[second])) second = i;
            if (second < 0 || v.Percent[second] < 10) return "No other category scored above 10%.";
            return "Next closest: " + Name(StudyText.Classes[second]) + " (" + P(v.Percent[second]) + ").";
        }

        /// <summary>One sentence explaining what the top category means for this photograph.</summary>
        public static string Meaning(ViewReading v)
        {
            string c = v.TopClass;
            string strength = Strength(v.TopPercent);
            if (v.TopPercent < 40) return "No category stands out clearly in this photograph. Please judge it directly.";
            if (c == "Normal")
                return "The photograph looks most like healthy skin; the model did not find a " + (v.TopPercent >= 85 ? "notable" : "clear") + " match to acne, eczema or hyperpigmentation.";
            return "The photograph resembles " + Name(c).ToLowerInvariant() + " more than the other categories (" + strength + " match). " +
                   (v.TopPercent < 60 ? "Treat this as a tentative impression." : "Please confirm on the photograph.");
        }

        /// <summary>The main message about whether the photographs point the same way.</summary>
        public static void Agreement(IList<ViewReading> views, bool single, out string headline, out string detail, out int level)
        {
            // level: 0 = fine, 1 = look closer, 2 = disagree
            headline = ""; detail = ""; level = 0;
            if (single || views.Count < 2) { headline = "Single photograph"; detail = "Only one photograph was analysed, so there is nothing to compare."; return; }

            var tops = views.Select(v => v.TopClass).Distinct().ToList();
            if (tops.Count > 1)
            {
                level = 2;
                headline = "The photographs do not agree";
                detail = string.Join(";  ", views.Select(v => v.View + " looks most like " + Name(v.TopClass))) + ". Please compare each photograph.";
                return;
            }

            string cls = tops[0];
            int ci = Array.IndexOf(StudyText.Classes, cls);
            double hi = views.Max(v => v.Percent[ci]), lo = views.Min(v => v.Percent[ci]);
            if (hi - lo >= 30)
            {
                level = 1;
                ViewReading strongest = views.First(v => v.Percent[ci] == hi), weakest = views.First(v => v.Percent[ci] == lo);
                headline = "Same category, different strength";
                detail = "All views point to " + Name(cls) + ", but the match is " + P(hi) + " in the " + strongest.View + " view and only " + P(lo) +
                         " in the " + weakest.View + " view. Compare the two photographs.";
                return;
            }
            headline = "The photographs agree";
            detail = "Every photograph points to " + Name(cls) + ".";
        }

        /// <summary>Short plain-language findings for the whole scan (for summaries). Built from the readings only.</summary>
        public static List<string> Findings(IList<ViewReading> views, string overallClass)
        {
            var lines = new List<string>();
            if (views.Count == 0) { lines.Add("No photograph produced a usable result."); return lines; }

            string cls = overallClass ?? views.GroupBy(v => v.TopClass).OrderByDescending(g => g.Count()).First().Key;
            int ci = Array.IndexOf(StudyText.Classes, cls);
            ViewReading best = views.OrderByDescending(v => v.Percent[ci]).First();

            lines.Add(cls == "Normal"
                ? "Overall, the photographs look most like healthy skin. The model found no clear match to acne, eczema or hyperpigmentation."
                : "Overall, the photographs resemble " + Name(cls).ToLowerInvariant() + " more than the other categories. The clearest view is " + best.View + " (" + Strength(best.Percent[ci]) + " match, " + P(best.Percent[ci]) + ").");

            var others = new List<string>();
            for (int i = 0; i < StudyText.Classes.Length; i++)
            {
                if (i == ci) continue;
                ViewReading hv = views.OrderByDescending(v => v.Percent[i]).First();
                if (hv.Percent[i] >= 10) others.Add(Name(StudyText.Classes[i]) + " up to " + P(hv.Percent[i]) + " (" + hv.View + ")");
            }
            lines.Add(others.Count == 0 ? "No other category reached 10% in any view."
                                        : "Other categories worth a look: " + string.Join(", ", others) + ".");

            var poor = views.Where(v => !string.IsNullOrEmpty(v.Quality) && v.Quality != "Good").ToList();
            lines.Add(poor.Count == 0 ? "All photographs were taken in suitable conditions."
                                      : "Photo quality to keep in mind: " + string.Join("; ", poor.Select(v => v.View + " (" + PlainQuality(v.Quality) + ")")) + ".");
            return lines;
        }

        /// <summary>"Check: blur, pose not verified, manual capture" -> "slightly blurred, angle not confirmed, taken manually"</summary>
        public static string PlainQuality(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q == "Good") return "good";
            string t = q.Replace("Check: ", "");
            t = t.Replace("blur", "slightly blurred").Replace("too dark", "a little dark").Replace("too bright", "a little bright")
                 .Replace("poor exposure", "uneven lighting").Replace("face not found", "face not clearly detected")
                 .Replace("pose not verified", "angle not confirmed").Replace("manual capture", "taken manually");
            return t;
        }
    }
}
