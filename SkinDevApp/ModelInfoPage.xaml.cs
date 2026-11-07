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

        /// <summary>Every lesion detector in the Model folder: what it finds, whether it is loaded, and its training notes.</summary>
        private static UIElement LesionCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("Lesion detectors", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            sp.Children.Add(Ui.Text("The three lesion detectors are part of the final model: their evidence feeds the fusion network, and their boxes name the facial regions on every result.",
                11.5, false, Ui.Muted, new Thickness(0, 0, 0, 8)));

            try { LesionEngine.EnsureLoaded(); } catch { }
            var loaded = LesionEngine.LoadedDetectors();
            double[] thresholds = ScanSettings.Current.DetectorThresholds();
            string dir = Path.GetDirectoryName(LesionEngine.ModelPath);
            var tech = new System.Text.StringBuilder();

            for (int i = 0; i < 3; i++)
            {
                string cls = Imaging.ClassPalette.Names[i];
                LesionDetector det = loaded.FirstOrDefault(d => d.PaletteClasses.Length == 1 && d.PaletteClasses[0] == i)
                                     ?? loaded.FirstOrDefault(d => d.PaletteClasses.Contains(i));
                JsonElement meta = default(JsonElement);
                string metaPath = Path.Combine(dir, "lesion_detector_" + cls.ToLowerInvariant() + ".json");
                bool hasMeta = false;
                try
                {
                    if (File.Exists(metaPath))
                    {
                        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metaPath))) meta = doc.RootElement.Clone();
                        hasMeta = true;
                    }
                }
                catch { }

                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                var head = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
                var badge = Ui.IconBadge("Icon.Target", 30, det != null ? "#EAF1E4" : "#EFEADC", det != null ? "#4F7A4E" : "#8D8D76");
                badge.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(badge, Dock.Left);
                head.Children.Add(badge);
                var status = Ui.Text(det != null ? "Ready" : "Not installed", 12, true, det != null ? Ui.Good : Ui.Muted, new Thickness(0));
                status.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(status, Dock.Right);
                head.Children.Add(status);
                var name = Ui.Text(cls + " detector", 14, true, Ui.Ink, new Thickness(0));
                name.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(name);
                block.Children.Add(head);

                if (det == null)
                {
                    block.Children.Add(Ui.Text("No detector is installed for " + cls.ToLowerInvariant() + ", so no " + cls.ToLowerInvariant() + " boxes are drawn.",
                        12, false, Ui.Muted, new Thickness(40, 0, 0, 8)));
                    sp.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = block });
                    continue;
                }

                string created = hasMeta && meta.TryGetProperty("created", out JsonElement c) ? c.GetString()
                               : hasMeta && meta.TryGetProperty("installed", out JsonElement inst) ? inst.GetString() : null;
                string baseModel = hasMeta && meta.TryGetProperty("base_model", out JsonElement bm) ? bm.GetString() : null;
                var rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
                {
                    Ui.KV("Type", "YOLOv8 object detector" + (baseModel != null && baseModel.StartsWith("yolov8s") ? " (small)" : "")),
                    Ui.KV("Finds", string.Join(", ", det.PaletteClasses.Select(k => Imaging.ClassPalette.Names[k]))),
                    Ui.KV("Models", det.EnsembleSize > 1 ? det.EnsembleSize + " trained copies, results averaged" : "1"),
                    Ui.KV("Picture size", det.ImageSize + " × " + det.ImageSize),
                    Ui.KV("Box threshold", Pct(thresholds[i]) + " (fixed for this study)")
                };
                if (!string.IsNullOrEmpty(created)) rows.Add(Ui.KV("Created", Fmt.When(created.Replace("T", " "))));
                var info = Ui.InfoRows(rows, 120, 12.5);
                info.Margin = new Thickness(40, 0, 18, 8);
                block.Children.Add(info);
                sp.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = block });

                tech.AppendLine(cls + " detector");
                tech.AppendLine("  File:        " + det.ModelFile + (det.Peers.Count > 0 ? " + " + string.Join(", ", det.Peers.Select(p => p.ModelFile)) : ""));
                tech.AppendLine("  SHA-256:     " + det.ModelSha256);
                if (hasMeta && meta.TryGetProperty("note", out JsonElement note)) tech.AppendLine("  Notes:       " + note.GetString());
                tech.AppendLine();
            }

            foreach (string problem in LesionEngine.LoadProblems)
                tech.AppendLine("Could not load: " + problem);
            if (!LesionEngine.IsAvailable && LesionEngine.LoadErrorMessage != null)
                tech.AppendLine("Problem: " + LesionEngine.LoadErrorMessage);
            tech.AppendLine("Runtime:       ONNX Runtime (CPU)");
            tech.AppendLine("Folder:        " + dir);
            sp.Children.Add(Technical(tech.ToString().TrimEnd()));
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
            list.Add(Tuple.Create("Haar face finder", "Checks that the face is centred and at the right distance in the oval.", hOk,
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

        private async System.Threading.Tasks.Task Render()
        {
            LeftColumn.Children.Clear();
            RightColumn.Children.Clear();
            ModelInfo.Refresh();
            ModelInfo mi = ModelInfo.Current;

            long currentId = 0;
            try { currentId = StudyRepository.EnsureCurrentModelVersion(); } catch { }

            // the final model: the only model whose results are shown and stored
            await System.Threading.Tasks.Task.Run(() => { try { SkinDevApp.V6.V6Service.EnsureLoaded(); } catch { } });
            SkinDevApp.V6.V6Config v6c = SkinDevApp.V6.V6Service.Config;
            string variant = SkinDevApp.V6.V6Service.Variant;
            var cls = new StackPanel();
            cls.Children.Add(Ui.Text("Final AI model", 16, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            cls.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Name", SkinDevApp.V6.AIResultStore.ModelLabel(variant)),
                Ui.KV("How it works", "A MobileNetV2 whole-face reading and three YOLOv8 lesion detectors (acne, hyperpigmentation, eczema), combined by one trained fusion network, then calibrated."),
                Ui.KV("Conditions", "Acne, Hyperpigmentation, Eczema (several can be present). Normal skin is derived when none is detected."),
                Ui.KV("What it reports", "Condition detected / Needs review / No condition detected; per condition: result, certainty and the facial regions marked."),
                Ui.KV("Status", SkinDevApp.V6.V6Service.IsReady ? "Ready" : "Not ready — " + (SkinDevApp.V6.V6Service.LastError ?? "the model could not be loaded")),
                Ui.KV("Training run", v6c == null ? "—" : v6c.RunId)
            }, 150));
            var v6tech = new System.Collections.Generic.List<string>
            {
                "Variant:         " + (variant ?? "not loaded") + "   (App.config key V6Variant: global_yolo | full)",
                "Preprocessing:   BGR->RGB, bilinear 224 x 224, (pixel / 127.5) - 1",
                "Runtime:         ONNX Runtime (CPU)",
                "Model folder:    " + SkinDevApp.V6.V6Service.DeploymentDir
            };
            if (v6c != null)
            {
                SkinDevApp.V6.FusionCalibration cal = variant == "full" && v6c.Full != null ? v6c.Full : v6c.GlobalYolo;
                v6tech.Add("Thresholds:      Acne " + cal.Thresholds[0] + " · Hyperpigmentation " + cal.Thresholds[1] + " · Eczema " + cal.Thresholds[2] + " (calibrated probability), band " + v6c.Band);
                foreach (SkinDevApp.V6.ModelFileSpec m in new[] { v6c.GlobalFeatures, v6c.FusionGlobalYolo, v6c.FusionGlobalOnly, v6c.FusionFull, v6c.RoiFeatures }.Where(x => x != null))
                    v6tech.Add("File:            " + m.File + "  SHA-256 " + m.Sha256.Substring(0, 16) + "… (verified at load)");
            }
            v6tech.Add("Earlier classifier: " + Fmt.ModelName(mi.RunName) + " — used only to tell when the face is steady for an automatic photograph; its output is not shown or used for findings.");
            cls.Children.Add(Technical(string.Join(Environment.NewLine, v6tech)));
            LeftColumn.Children.Add(Ui.Card(cls));
            LeftColumn.Children.Add(Ui.Card(LesionCard()));

            var cam = new StackPanel();
            cam.Children.Add(Ui.Text("Heat-map service", 16, true));
            var checking = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            checking.Children.Add(Ui.Spinner(18));
            checking.Children.Add(Ui.Text("Checking the heat-map service…", 12.5, false, Ui.Muted, new Thickness(10, 0, 0, 0)));
            cam.Children.Add(checking);
            // the earlier heat-map (Grad-CAM++) service is not used by the final model, so its card is no longer shown

            var face = new StackPanel();
            face.Children.Add(Ui.Text("Face and angle models", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            face.Children.Add(Ui.Text("Used while scanning to find the face, check its position and confirm the front, left and right angles.", 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            var faceRows = new StackPanel();
            faceRows.Children.Add(Ui.Spinner(18));
            face.Children.Add(faceRows);
            RightColumn.Children.Add(Ui.Card(face));

            var hist = new StackPanel();
            hist.Children.Add(Ui.Text("Model history", 16, true, Ui.Ink, new Thickness(0, 0, 0, 2)));
            hist.Children.Add(Ui.Text("Every scan keeps the model version that produced it. A newer model never changes older results.", 11.5, false, Ui.Muted, new Thickness(0, 0, 0, 6)));
            try
            {
                var all = StudyRepository.AllModelVersions().ToList();
                var tech = new System.Text.StringBuilder();
                foreach (ModelVersion m in all)
                {
                    bool current = m.ModelVersionID == currentId;
                    hist.Children.Add(new Border
                    {
                        BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6),
                        Child = Ui.InfoRows(new[]
                        {
                            Ui.KV("Version " + m.ModelVersionID, Fmt.ModelName(m.RunName) + (current ? "  (in use now)" : "")),
                            Ui.KV("First used", Fmt.When(m.FirstSeenAt))
                        }, 110)
                    });
                    tech.AppendLine("Version " + m.ModelVersionID + "  ONNX " + ModelVersion.Short(m.OnnxSha256) + "  Keras " + ModelVersion.Short(m.KerasSha256) +
                                    "  service " + (m.ServiceVersion ?? "") + " " + ModelVersion.Short(m.ServiceSha256));
                }
                hist.Children.Add(Technical(tech.ToString().TrimEnd()));
            }
            catch (Exception ex) { hist.Children.Add(Ui.Text("The database could not be read: " + ex.Message, 12, false, Ui.Bad)); }
            RightColumn.Children.Add(Ui.Card(hist));

            var faceInfo = await System.Threading.Tasks.Task.Run(() => FaceModels());
            faceRows.Children.Clear();
            foreach (Tuple<string, string, bool, string, string> f in faceInfo)
                faceRows.Children.Add(ModelRow(f.Item1, f.Item2, f.Item3, f.Item4, f.Item5));

            GradCamHealth h = await GradCamService.GetHealthAsync();
            cam.Children.Clear();
            cam.Children.Add(Ui.Text("Heat-map service", 16, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            bool ok = h != null && h.Ok;
            cam.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Status", ok ? "Running" : "Not running"),
                Ui.KV("What it does", "Creates the heat maps that show which parts of a photograph the computer looked at."),
                Ui.KV("If it is not running", "Scans still work and results are still shown, but no heat maps are created.")
            }, 150));
            cam.Children.Add(Technical(string.Join(Environment.NewLine, new[]
            {
                "Address:         " + GradCamService.BaseUrl,
                "Service version: " + (h?.ServiceVersion ?? mi.ServiceVersion),
                "Model loaded:    " + (h == null ? "-" : (h.ModelLoaded ? "yes" : "no")),
                "Target layer:    " + (h?.TargetLayer ?? "-"),
                "Layer override:  " + (string.IsNullOrEmpty(GradCamServiceConfig.TargetLayer) ? "none (service default)" : GradCamServiceConfig.TargetLayer),
                "Execution mode:  " + (h?.ExecMode ?? "-"),
                "Script SHA-256:  " + mi.ServiceSha256,
                "Method:          Grad-CAM++, one map per category"
            })));
        }
    }
}