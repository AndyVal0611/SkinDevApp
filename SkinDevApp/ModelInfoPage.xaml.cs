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
using SkinDevApp.Views;

namespace SkinDevApp.Views
{
    public partial class ModelInfoPage : Page
    {
        public ModelInfoPage()
        {
            InitializeComponent();
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
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 6, 0, 0)
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

            var cls = new StackPanel();
            cls.Children.Add(Ui.Text("Skin-condition model", 16, true, Ui.Ink, new Thickness(0, 0, 0, 4)));
            cls.Children.Add(Ui.InfoRows(new[]
            {
                Ui.KV("Name", Fmt.ModelName(mi.RunName)),
                Ui.KV("Type", "MobileNetV2 image classifier"),
                Ui.KV("Categories", Labels()),
                Ui.KV("What it reports", "How closely a photograph resembles each of the four categories. The four figures add up to 100%."),
                Ui.KV("Status", AiEngine.IsAvailable ? "Ready" : "Not ready — " + (AiEngine.LoadErrorMessage ?? "the model could not be loaded")),
                Ui.KV("Version number", currentId > 0 ? currentId.ToString() : "—"),
                Ui.KV("Created", Fmt.When(mi.CreatedUtc))
            }, 150));
            cls.Children.Add(Technical(string.Join(Environment.NewLine, new[]
            {
                "Input size:      224 x 224 x 3",
                "Colour order:    RGB (camera frames are converted from BGR)",
                "Normalisation:   (pixel / 127.5) - 1",
                "Runtime:         ONNX Runtime",
                "Model file:      " + AiEngine.ModelPath + (File.Exists(AiEngine.ModelPath) ? "" : "  (NOT FOUND)"),
                "Version source:  " + mi.Source,
                "ONNX SHA-256:    " + mi.OnnxSha256,
                "Keras SHA-256:   " + mi.KerasSha256
            })));
            LeftColumn.Children.Add(Ui.Card(cls));

            var cam = new StackPanel();
            cam.Children.Add(Ui.Text("Heat-map service", 16, true));
            cam.Children.Add(Ui.Text("Checking…", 12, false, Ui.Muted));
            RightColumn.Children.Add(Ui.Card(cam));

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