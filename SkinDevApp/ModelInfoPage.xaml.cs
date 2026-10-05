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
            HeaderHost.Content = Ui.Header("Model Information");
            if (!AppSession.IsResearcher)
            {
                LeftColumn.Children.Add(Ui.Card(Ui.Text("Model information is available to signed-in researchers / administrators only.", 14, false, Ui.Bad)));
                return;
            }
            Loaded += async (s, e) => await Render();
        }

        private static string Labels()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "labels.json");
                if (!File.Exists(p)) return "Acne, Hyperpigmentation, Eczema, Normal_Skin";
                using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(p)))
                    return string.Join(", ", d.RootElement.GetProperty("classes").EnumerateArray()
                        .Select((c, i) => i + " = " + c.GetString()));
            }
            catch { return "Acne, Hyperpigmentation, Eczema, Normal_Skin"; }
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
            cls.Children.Add(Ui.Text("Skin-condition classifier", 16, true));
            cls.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("Model name", "PrecisionSkin"),
                Ui.KV("Architecture", "MobileNetV2"),
                Ui.KV("Input size", "224 × 224 × 3"),
                Ui.KV("Input colour order", "RGB (camera frames are converted from BGR)"),
                Ui.KV("Normalisation", "(pixel / 127.5) − 1"),
                Ui.KV("Classes", Labels()),
                Ui.KV("Output", "Four-class softmax (Model Class Scores, not independent probabilities)"),
                Ui.KV("Runtime", "ONNX Runtime (deployment inference)"),
                Ui.KV("ONNX model file", AiEngine.ModelPath + (File.Exists(AiEngine.ModelPath) ? "" : "  (NOT FOUND)")),
                Ui.KV("Loaded", AiEngine.IsAvailable ? "Yes" : "No — " + (AiEngine.LoadErrorMessage ?? "not loaded")),
                Ui.KV("Model version (run)", mi.RunName),
                Ui.KV("Model version ID (database)", currentId > 0 ? currentId.ToString() : "—"),
                Ui.KV("Created (UTC)", mi.CreatedUtc),
                Ui.KV("ONNX SHA-256", mi.OnnxSha256),
                Ui.KV("Keras SHA-256", mi.KerasSha256),
                Ui.KV("Version source", mi.Source)
            }, 190));
            LeftColumn.Children.Add(Ui.Card(cls));

            var cam = new StackPanel();
            cam.Children.Add(Ui.Text("Grad-CAM++ service", 16, true));
            cam.Children.Add(Ui.Text("Checking service…", 12, false, Ui.Muted));
            RightColumn.Children.Add(Ui.Card(cam));

            var hist = new StackPanel();
            hist.Children.Add(Ui.Text("Model versions recorded in the database", 16, true));
            try
            {
                foreach (ModelVersion m in StudyRepository.AllModelVersions())
                    hist.Children.Add(new Border
                    {
                        BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 6),
                        Child = Ui.KeyValues(new[]
                        {
                            Ui.KV("ID " + m.ModelVersionID, m.RunName + (m.OnnxSha256 == mi.OnnxSha256 ? "   (current)" : "")),
                            Ui.KV("ONNX", m.OnnxSha256), Ui.KV("Keras", m.KerasSha256),
                            Ui.KV("Service", (m.ServiceVersion ?? "") + " · " + ModelVersion.Short(m.ServiceSha256)),
                            Ui.KV("First seen", m.FirstSeenAt)
                        }, 90)
                    });
            }
            catch (Exception ex) { hist.Children.Add(Ui.Text("Database error: " + ex.Message, 12, false, Ui.Bad)); }
            hist.Children.Add(Ui.Text("Historical scans keep the model version that produced them; a new model never relabels old results.", 10.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
            RightColumn.Children.Add(Ui.Card(hist));

            GradCamHealth h = await GradCamService.GetHealthAsync();
            cam.Children.Clear();
            cam.Children.Add(Ui.Text("Grad-CAM++ service", 16, true));
            cam.Children.Add(Ui.KeyValues(new[]
            {
                Ui.KV("Status", h != null && h.Ok ? "Available" : "Unavailable (classification still works; heatmaps missing)"),
                Ui.KV("Endpoint", GradCamService.BaseUrl),
                Ui.KV("Service version", h?.ServiceVersion ?? mi.ServiceVersion),
                Ui.KV("Keras model loaded", h == null ? "—" : (h.ModelLoaded ? "Yes" : "No")),
                Ui.KV("Current target layer", h?.TargetLayer ?? "—"),
                Ui.KV("Layer override (Settings)", string.IsNullOrEmpty(GradCamServiceConfig.TargetLayer) ? "none (service default)" : GradCamServiceConfig.TargetLayer),
                Ui.KV("Execution mode", h?.ExecMode ?? "—"),
                Ui.KV("Service script SHA-256", mi.ServiceSha256),
                Ui.KV("Method", "Grad-CAM++, one independent map per target class index (0 Acne, 1 Hyperpigmentation, 2 Eczema, 3 Normal)")
            }, 190));
            cam.Children.Add(Ui.Text(StudyText.AttributionNote, 10.5, false, Ui.Muted, new Thickness(0, 6, 0, 0)));
        }
    }
}