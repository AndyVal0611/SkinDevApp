// ModelInfoPage.xaml.cs - traceable model identity (researcher / admin).
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.AI;
using SkinDevApp.Data;
using SkinDevApp.Explainability;
using SkinDevApp.Imaging;
using SkinDevApp.Scanning;
using SkinDevApp.Views;

namespace SkinDevApp.Views
{
    public partial class ModelInfoPage : Page
    {
        public ModelInfoPage()
        {
            InitializeComponent();
            Ui.StackWhenNarrow(SplitGrid, 950);
            HeaderHost.Content = Ui.Header("Model information");
            if (!AppSession.IsResearcher)
            {
                LeftColumn.Children.Add(Ui.Card(Ui.Text("Model information is only available to signed-in staff.", 14, false, Ui.Bad)));
                return;
            }
            Loaded += async (s, e) => await Render();
        }

        private static string Labels()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "labels.json");
                if (!File.Exists(p)) return "Acne, Hyperpigmentation, Eczema, Normal Skin";
                using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(p)))
                    return string.Join(", ", d.RootElement.GetProperty("classes").EnumerateArray().Select(c => ClinicalText.Name(c.GetString()).Replace("_", " ")));
            }
            catch { return "Acne, Hyperpigmentation, Eczema, Normal Skin"; }
        }

        // ------------------------------------------------------------ lesion detectors --

        private static string Pct(double v) => (v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

        private static int _detectorsReady;

        /// <summary>The three lesion detectors in one compact table: what each finds, whether it is loaded, and its settings.</summary>
        private static UIElement LesionCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Lesion detectors", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Ui.Text("Part of the final model: their evidence feeds the fusion network, and their boxes name the facial regions on every result.",
                11.5, false, Ui.Muted, new Thickness(0, 0, 0, 10)));

            try { LesionEngine.EnsureLoaded(); } catch { }
            var loaded = LesionEngine.LoadedDetectors();
            double[] thresholds = ScanSettings.Current.DetectorThresholds();
            string dir = Path.GetDirectoryName(LesionEngine.ModelPath);
            var tech = new System.Text.StringBuilder();
            _detectorsReady = 0;

            var g = new Grid();
            double[] widths = { 190, 110, 150, 120, 1 };
            for (int k = 0; k < 4; k++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[k]) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] head = { "Detector", "Picture size", "Trained copies", "Box threshold", "Status" };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int k = 0; k < head.Length; k++) { var t = Ui.Text(head[k], 11, true, Ui.Muted, new Thickness(0, 0, 8, 6)); Grid.SetColumn(t, k); g.Children.Add(t); }
            var rule = new Border { Height = 1, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumnSpan(rule, 5); g.Children.Add(rule);

            for (int i = 0; i < 3; i++)
            {
                string cls = Imaging.ClassPalette.Names[i];
                LesionDetector det = loaded.FirstOrDefault(d => d.PaletteClasses.Length == 1 && d.PaletteClasses[0] == i)
                                     ?? loaded.FirstOrDefault(d => d.PaletteClasses.Contains(i));
                JsonElement meta = default(JsonElement);
                bool hasMeta = false;
                try
                {
                    string metaPath = Path.Combine(dir, "lesion_detector_" + cls.ToLowerInvariant() + ".json");
                    if (File.Exists(metaPath)) { using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaPath))) meta = doc.RootElement.Clone(); hasMeta = true; }
                }
                catch { }

                int row = i + 1;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = Ui.Text(cls, 13.5, true, Ui.Brush(Imaging.ClassPalette.Hex[i]), new Thickness(0, 8, 8, 8));
                string size = det == null ? "—" : det.ImageSize + " × " + det.ImageSize;
                string copies = det == null ? "—" : det.EnsembleSize > 1 ? det.EnsembleSize + " (averaged)" : "1";
                string thr = det == null ? "—" : Pct(thresholds[i]);
                var cells = new UIElement[]
                {
                    name,
                    Ui.Text(size, 12.5, false, Ui.Ink, new Thickness(0, 8, 8, 8)),
                    Ui.Text(copies, 12.5, false, Ui.Ink, new Thickness(0, 8, 8, 8)),
                    Ui.Text(thr, 12.5, false, Ui.Ink, new Thickness(0, 8, 8, 8)),
                    Ui.Text(det != null ? "● Ready" : "Not installed", 12.5, true, det != null ? Ui.Good : Ui.Bad, new Thickness(0, 8, 0, 8))
                };
                for (int k = 0; k < cells.Length; k++) { Grid.SetRow(cells[k], row); Grid.SetColumn(cells[k], k); g.Children.Add(cells[k]); }
                if (i < 2) { var r2 = new Border { Height = 1, Background = Ui.Brush("#EFEADC"), VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetRow(r2, row); Grid.SetColumnSpan(r2, 5); g.Children.Add(r2); }

                if (det == null) { tech.AppendLine(cls + " detector: not installed, so no " + cls.ToLowerInvariant() + " boxes are drawn."); tech.AppendLine(); continue; }
                _detectorsReady++;
                string created = hasMeta && meta.TryGetProperty("created", out JsonElement c2) ? c2.GetString()
                               : hasMeta && meta.TryGetProperty("installed", out JsonElement inst) ? inst.GetString() : null;
                tech.AppendLine(cls + " detector");
                tech.AppendLine("  Type:        YOLOv8 object detector");
                if (!string.IsNullOrEmpty(created)) tech.AppendLine("  Created:     " + Fmt.When(created.Replace("T", " ")));
                tech.AppendLine("  File:        " + det.ModelFile + (det.Peers.Count > 0 ? " + " + string.Join(", ", det.Peers.Select(pe => pe.ModelFile)) : ""));
                tech.AppendLine("  SHA-256:     " + det.ModelSha256);
                if (hasMeta && meta.TryGetProperty("note", out JsonElement note)) tech.AppendLine("  Notes:       " + note.GetString());
                tech.AppendLine();
            }
            sp.Children.Add(g);
            sp.Children.Add(Ui.Text("The box threshold is fixed. Boxes below it are not drawn.", 11, false, Ui.Muted, new Thickness(0, 6, 0, 0)));

            foreach (string problem in LesionEngine.LoadProblems) tech.AppendLine("Could not load: " + problem);
            if (!LesionEngine.IsAvailable && LesionEngine.LoadErrorMessage != null) tech.AppendLine("Problem: " + LesionEngine.LoadErrorMessage);
            tech.AppendLine("Runtime:       ONNX Runtime (CPU)");
            sp.Children.Add(Technical(tech.ToString().TrimEnd()));
            return sp;
        }

        /// <summary>The path of a scan through the models, in plain words.</summary>
        private static UIElement PipelineCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("How a scan is read", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Ui.Text("Each image passes through the following steps, in this order.", 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 8)));
            string[][] steps =
            {
                new[] { "Check the image", "The face is located and centered, and the head angle is confirmed. Blurred or poorly lit images are flagged." },
                new[] { "Read the whole face", "A whole-face model (MobileNetV2) looks at the full image." },
                new[] { "Look for spots", "Three lesion detectors (YOLOv8) mark areas that may show acne, hyperpigmentation or eczema." },
                new[] { "Combine", "A trained fusion network combines both readings and evaluates each condition against its own threshold." },
                new[] { "Report", "Each condition is reported as detected, needs review, or not detected, with its certainty and the facial regions marked. Every result needs your review." }
            };
            for (int i = 0; i < steps.Length; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
                var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Brush("#E7E8CF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#5F6048"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);
                var t = new StackPanel();
                t.Children.Add(Ui.Text(steps[i][0], 13.5, true, Ui.Ink, new Thickness(0)));
                t.Children.Add(Ui.Text(steps[i][1], 12, false, Ui.Muted, new Thickness(0, 1, 0, 0)));
                row.Children.Add(t);
                sp.Children.Add(row);
            }
            return sp;
        }

        // --------------------------------------------------------------- face models --

        /// <summary>(name, what it does, loaded, detail, file) for the face finder and the head-angle model. Runs on a background thread.</summary>
        private static System.Collections.Generic.List<Tuple<string, string, bool, string, string>> FaceModels()
        {
            var list = new System.Collections.Generic.List<Tuple<string, string, bool, string, string>>();

            string yunet = ViewPoseEstimator.FindModel();
            bool yOk = false; string yErr = null;
            try { using (var p = new ViewPoseEstimator()) { yOk = p.Available; yErr = p.LoadError; } } catch (Exception ex) { yErr = ex.Message; }
            list.Add(Tuple.Create("YuNet face detector", "Finds the face and confirms the front, left and right head angles.", yOk,
                yOk ? "Ready" : (string.IsNullOrEmpty(yErr) ? "Not found" : yErr), yunet ?? ViewPoseEstimator.ModelFile + " (not found)"));

            string haar = FrameQualityAnalyzer.FindCascade();
            bool hOk = false;
            try { using (var q = new FrameQualityAnalyzer()) hOk = q.CascadeAvailable; } catch { }
            list.Add(Tuple.Create("Haar face finder", "Checks that the face is centered and at the right distance in the oval.", hOk,
                hOk ? "Ready" : "Not found", haar ?? "haarcascade_frontalface_default.xml (not found)"));
            return list;
        }

        private static UIElement ModelRow(string name, string what, bool ok, string status, string file)
        {
            var block = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var badge = Ui.IconBadge("Icon.ScanFace", 30, ok ? "#EAF1E4" : "#F8E6E2", ok ? "#4F7A4E" : "#A93D33");
            badge.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var st = Ui.Text(status, 12, true, ok ? Ui.Good : Ui.Bad, new Thickness(0));
            st.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(st, Dock.Right);
            head.Children.Add(st);
            var nm = Ui.Text(name, 14, true, Ui.Ink, new Thickness(0));
            nm.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(nm);
            block.Children.Add(head);
            block.Children.Add(Ui.Text(what, 12, false, Ui.Muted, new Thickness(40, 0, 0, 0)));
            block.Children.Add(Ui.Text(Path.GetFileName(file), 11, false, Ui.Brush("#9B9B84"), new Thickness(40, 2, 0, 0)));
            return new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = block };
        }

        private static string Mono(string title, params string[] lines)
        {
            return title + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }

        private static UIElement Technical(string text)
        {
            return new Expander
            {
                Header = "Technical details",
                Margin = new Thickness(0, 10, 0, 0),
                Content = new TextBox
                {
                    Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 6, 0, 0)
                }
            };
        }

        private static UIElement ReadinessCard(bool finalOk, int detectors, int faceOk, int faceTotal)
        {
            bool all = finalOk && detectors == 3 && faceOk == faceTotal;
            var row = new DockPanel();
            var badge = Ui.IconBadge(all ? "Icon.CheckCircle" : "Icon.Alert", 44, all ? "#EAF1E4" : "#FBF1DE", all ? "#4F7A4E" : "#8A5A00");
            badge.Margin = new Thickness(0, 0, 16, 0);
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            Action<string, bool> chip = (text, ok) => chips.Children.Add(Ui.Pill((ok ? "✓ " : "✗ ") + text, ok ? Ui.Good : Ui.Bad, ok ? "#EEF6EC" : "#FDECEC"));
            chip("Final model", finalOk);
            chip("Lesion detectors " + detectors + " of 3", detectors == 3);
            chip("Face models " + faceOk + " of " + faceTotal, faceOk == faceTotal);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Ui.Text(all ? "All models are ready for scanning" : "Some models need attention", 17, true, all ? Ui.Good : Ui.Bad, new Thickness(0)));
            t.Children.Add(chips);
            row.Children.Add(t);
            return Ui.Card(row, new Thickness(8, 0, 8, 12));
        }

        private async System.Threading.Tasks.Task Render()
        {
            LeftColumn.Children.Clear();
            RightColumn.Children.Clear();
            ReadyHost.Content = null;
            ModelInfo.Refresh();
            ModelInfo mi = ModelInfo.Current;

            // the final model: the only model whose results are shown and stored
            await System.Threading.Tasks.Task.Run(() => { try { SkinDevApp.V6.V6Service.EnsureLoaded(); } catch { } });
            SkinDevApp.V6.V6Config v6c = SkinDevApp.V6.V6Service.Config;
            string variant = SkinDevApp.V6.V6Service.Variant;
            bool finalOk = SkinDevApp.V6.V6Service.IsReady;
            var cls = new StackPanel();
            cls.Children.Add(Ui.Text("Final AI model", 16, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            cls.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Name", SkinDevApp.V6.AIResultStore.ModelLabel(variant)),
                Ui.KV("Status", finalOk ? "Ready" : "Not ready — " + (SkinDevApp.V6.V6Service.LastError ?? "the model could not be loaded")),
                Ui.KV("Conditions", "Acne, hyperpigmentation and eczema (more than one may be present). Normal skin is reported when none is detected."),
                Ui.KV("Each result shows", "Detected, needs review, or not detected, with the certainty and the facial regions marked."),
                Ui.KV("Reviewed by", "You (the dermatologist), for every result")
            }, 130));
            var v6tech = new System.Collections.Generic.List<string>
            {
                "Training run:    " + (v6c == null ? "—" : v6c.RunId),
                "Variant:         " + (variant ?? "not loaded") + "   (App.config key V6Variant: global_yolo | full)",
                "Preprocessing:   BGR->RGB, bilinear 224 x 224, (pixel / 127.5) - 1",
                "Runtime:         ONNX Runtime (CPU)"
            };
            if (v6c != null)
            {
                SkinDevApp.V6.FusionCalibration cal = variant == "full" && v6c.Full != null ? v6c.Full : v6c.GlobalYolo;
                v6tech.Add("Thresholds:      Acne " + cal.Thresholds[0] + " · Hyperpigmentation " + cal.Thresholds[1] + " · Eczema " + cal.Thresholds[2] + " (calibrated probability), band " + v6c.Band);
                foreach (SkinDevApp.V6.ModelFileSpec m in new[] { v6c.GlobalFeatures, v6c.FusionGlobalYolo, v6c.FusionGlobalOnly, v6c.FusionFull, v6c.RoiFeatures }.Where(x => x != null))
                    v6tech.Add("File:            " + m.File + "  SHA-256 " + m.Sha256.Substring(0, 16) + "… (verified at load)");
            }
            v6tech.Add("Earlier classifier: " + Fmt.ModelName(mi.RunName) + " — used only to tell when the face is steady for an automatic image; its output is not shown or used for findings.");
            cls.Children.Add(Technical(string.Join(Environment.NewLine, v6tech)));
            LeftColumn.Children.Add(Ui.Card(cls));
            LeftColumn.Children.Add(Ui.Card(LesionCard()));
            int detectors = _detectorsReady;

            RightColumn.Children.Add(Ui.Card(PipelineCard()));

            var face = new StackPanel();
            face.Children.Add(Ui.Text("Face and angle models", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            face.Children.Add(Ui.Text("Used while scanning to find the face, check its position and confirm the front, left and right angles.", 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            var faceRows = new StackPanel();
            faceRows.Children.Add(Ui.Spinner(18));
            face.Children.Add(faceRows);
            RightColumn.Children.Add(Ui.Card(face));

            var faceInfo = await System.Threading.Tasks.Task.Run(() => FaceModels());
            faceRows.Children.Clear();
            foreach (Tuple<string, string, bool, string, string> f in faceInfo)
                faceRows.Children.Add(ModelRow(f.Item1, f.Item2, f.Item3, f.Item4, f.Item5));
            ReadyHost.Content = ReadinessCard(finalOk, detectors, faceInfo.Count(f => f.Item3), faceInfo.Count);
        }
    }
}
