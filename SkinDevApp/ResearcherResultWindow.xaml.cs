// ResearcherResultWindow.xaml.cs - researcher view of one archived capture + Pending Review.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkinDevApp.Views
{
    public partial class ResearcherResultWindow : Window
    {
        private string _folder;
        private CaptureRecord _record;
        private List<string> _queue = new List<string>();
        private static readonly string[] Labels =
            { "Acne", "Hyperpigmentation", "Eczema", "Normal", "Other / uncertain", "Unusable image" };

        public ResearcherResultWindow(string folder)
        {
            InitializeComponent();
            LabelCombo.ItemsSource = Labels;
            ReviewerBox.Text = Environment.UserName;
            _queue = ReviewStore.ListPending();
            Load(folder);
        }

        private static BitmapSource LoadBitmap(string path)
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private void Load(string folder)
        {
            _folder = folder;
            _record = ReviewStore.LoadRecord(folder);
            if (_record == null) throw new InvalidOperationException("record.json not found in " + folder);

            HeaderTxt.Text = "Capture " + (_record.CaptureId ?? "").Substring(0, Math.Min(8, (_record.CaptureId ?? "").Length))
                + "  |  " + _record.TimestampUtc + "  |  trigger: " + _record.Trigger;

            var review = ReviewStore.LoadReview(folder);
            StatusTxt.Text = review == null
                ? "Status: Pending Review"
                : "Status: Reviewed by " + review.Reviewer + " (" + review.ReviewerLabel + ")";

            string analysed = _record.Files != null && _record.Files.ContainsKey("analysed")
                ? _record.Files["analysed"] : "analysed.png";
            AnalysedImg.Source = LoadBitmap(Path.Combine(folder, analysed));

            // overlays
            OverlayGrid.Children.Clear();
            var maps = _record.ClassMaps ?? new List<MapSection>();
            foreach (var m in maps)
            {
                int idx = ClassPalette.IndexOf(m.Class);
                Color c = Colors.Gray;
                if (idx >= 0)
                    c = (Color)ColorConverter.ConvertFromString(ClassPalette.Hex[idx]);

                var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 240 };
                img.Source = LoadBitmap(Path.Combine(folder, m.OverlayFile ?? ""));

                string zone = m.Diffuse ? "diffuse (no single region)" : (m.TopZone ?? "-");
                var cap = new TextBlock
                {
                    Text = m.Class + "  |  score " + m.ProbabilityPercent.ToString("0.0") + "%  |  relative strength "
                         + m.RelativeStrength.ToString("0.00") + "  |  " + zone,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var sp = new StackPanel();
                sp.Children.Add(img);
                sp.Children.Add(cap);

                OverlayGrid.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(c),
                    BorderThickness = new Thickness(3),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = sp
                });
            }

            ScoresBox.Text = BuildScores();
            DetailsBox.Text = BuildDetails();

            LabelCombo.SelectedItem = review != null ? review.ReviewerLabel : null;
            if (review != null) { ReviewerBox.Text = review.Reviewer; CommentBox.Text = review.Comment; }
            else CommentBox.Text = "";

            UpdateNav();
        }

        private string BuildScores()
        {
            var sb = new StringBuilder();
            var s = _record.Scores;
            if (s?.OnnxPercent != null)
            {
                sb.AppendLine("ONNX (deployed):");
                foreach (var kv in s.OnnxPercent.OrderByDescending(k => k.Value))
                    sb.AppendLine("  " + kv.Key.PadRight(18) + kv.Value.ToString("0.0").PadLeft(6) + " %");
            }
            if (s?.KerasPercent != null)
            {
                sb.AppendLine("Keras (service):");
                foreach (var kv in s.KerasPercent.OrderByDescending(k => k.Value))
                    sb.AppendLine("  " + kv.Key.PadRight(18) + kv.Value.ToString("0.0").PadLeft(6) + " %");
            }
            var p = _record.Primary;
            if (p != null)
            {
                sb.AppendLine();
                sb.AppendLine("Model's top class : " + p.DisplayedClass + " (" + p.DisplayedConfidencePercent.ToString("0.0") + " %)");
                sb.AppendLine("Runner-up         : " + p.RunnerUpClass + "  margin " + p.MarginPercent.ToString("0.0") + " pts");
            }
            return sb.ToString().TrimEnd();
        }

        private string BuildDetails()
        {
            var sb = new StringBuilder();
            var r = _record;
            if (r.Region != null) { sb.AppendLine("Region: " + r.Region.Summary); sb.AppendLine(); }
            if (r.Consistency != null)
            {
                sb.AppendLine("ONNX/Keras same top class: " + (r.Consistency.SameTopClass?.ToString() ?? "n/a"));
                if (r.Consistency.MaxAbsDifference.HasValue)
                    sb.AppendLine("Max |diff|: " + r.Consistency.MaxAbsDifference.Value.ToString("0.0000"));
                if (!string.IsNullOrEmpty(r.Consistency.Warning)) sb.AppendLine("WARNING: " + r.Consistency.Warning);
                sb.AppendLine();
            }
            if (r.Stability != null)
            {
                sb.AppendLine("Stability: held " + r.Stability.FramesHeldStable + "/" + r.Stability.StableFramesRequired
                    + " frames, consistency " + r.Stability.Consistency.ToString("0.00")
                    + ", motion mean/max " + r.Stability.MotionMean.ToString("0.0") + "/" + r.Stability.MotionMax.ToString("0.0")
                    + ", hold " + r.Stability.HoldMs.ToString("0") + " ms");
                sb.AppendLine("(stability = steady prediction, not correctness)");
                sb.AppendLine();
            }
            if (r.Quality != null)
                sb.AppendLine("Quality: sharpness " + r.Quality.Sharpness.ToString("0.0")
                    + ", brightness " + r.Quality.Brightness.ToString("0")
                    + ", face box " + (r.Quality.FaceFound ? "found" : "not found")
                    + (string.IsNullOrEmpty(r.Quality.Guidance) ? "" : ", guidance: " + r.Quality.Guidance));
            if (r.Model != null)
            {
                sb.AppendLine();
                sb.AppendLine("Model run: " + r.Model.RunName);
                sb.AppendLine("ONNX sha256: " + r.Model.OnnxSha256);
                sb.AppendLine("Service: " + r.Model.ServiceVersionReported);
            }
            if (r.Setup != null)
            {
                sb.AppendLine();
                sb.AppendLine("Setup: " + r.Setup.KioskId + ", camera " + r.Setup.CameraModel
                    + " @ " + r.Setup.CameraDistanceCm + " cm / height " + r.Setup.CameraHeightCm
                    + " cm, marker: " + r.Setup.ChinRestMarker + ", lighting: " + r.Setup.Lighting);
            }
            if (r.Notes != null && r.Notes.Count > 0)
            {
                sb.AppendLine();
                foreach (var n in r.Notes) sb.AppendLine("- " + n);
            }
            return sb.ToString().TrimEnd();
        }

        private void UpdateNav()
        {
            int i = _queue.IndexOf(_folder);
            PrevBtn.IsEnabled = _queue.Count > 0 && (i > 0 || i < 0);
            NextBtn.IsEnabled = _queue.Count > 0 && (i < _queue.Count - 1);
        }

        private void Step(int dir)
        {
            _queue = ReviewStore.ListPending();
            if (_queue.Count == 0) { MessageBox.Show("No pending captures.", "LUMYVUE Review"); return; }
            int i = _queue.IndexOf(_folder);
            int next = i < 0 ? 0 : Math.Max(0, Math.Min(_queue.Count - 1, i + dir));
            try { Load(_queue[next]); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "LUMYVUE Review"); }
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e) { Step(-1); }
        private void NextBtn_Click(object sender, RoutedEventArgs e) { Step(+1); }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LabelCombo.SelectedItem == null)
            {
                MessageBox.Show("Choose a label first.", "LUMYVUE Review");
                return;
            }
            string label = (string)LabelCombo.SelectedItem;
            string top = _record.Primary?.DisplayedClass;
            var rv = new ReviewRecord
            {
                CaptureId = _record.CaptureId,
                Reviewer = string.IsNullOrWhiteSpace(ReviewerBox.Text) ? "unknown" : ReviewerBox.Text.Trim(),
                ReviewerLabel = label,
                ModelTopClass = top,
                AgreesWithModel = Labels.Take(4).Contains(label) ? (bool?)string.Equals(label, top, StringComparison.OrdinalIgnoreCase) : null,
                Comment = CommentBox.Text
            };
            try
            {
                ReviewStore.SaveReview(_folder, rv);
                StatusTxt.Text = "Status: Reviewed by " + rv.Reviewer + " (" + rv.ReviewerLabel + ")";
                MessageBox.Show("Review saved. This does not add the image to any training set.", "LUMYVUE Review");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save: " + ex.Message, "LUMYVUE Review");
            }
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", _folder); } catch { }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
