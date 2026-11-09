using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SkinDevApp.Views;

namespace SkinDevApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>One body font for every window, dialog and control, including the ones built in code. Returns false if it could not be applied.</summary>
        public static bool ApplyBodyFont()
        {
            try
            {
                var body = (System.Windows.Media.FontFamily)Application.Current.FindResource("BodyFont");
                System.Windows.Documents.TextElement.FontFamilyProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(body));
                System.Windows.Controls.TextBlock.FontFamilyProperty.OverrideMetadata(typeof(System.Windows.Controls.TextBlock), new FrameworkPropertyMetadata(body));
                return true;
            }
            catch { return false; }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplyBodyFont();

            // "SkinDevApp.exe --diagnose <image>": headless, read-only replay of the classifier and detectors (see CnnDiagnostics.cs)
            if (SkinDevApp.AI.CnnDiagnostics.TryRunFromArgs(e.Args)) { Shutdown(0); return; }

            SkinDevApp.Views.ScanInterpreter.Settings = SkinDevApp.Views.InterpretationSettings.From(k => System.Configuration.ConfigurationManager.AppSettings[k]);

            SkinDevApp.V6.V6FeatureFlag.VariantProvider = () => System.Configuration.ConfigurationManager.AppSettings["V6Variant"] ?? Environment.GetEnvironmentVariable(SkinDevApp.V6.V6FeatureFlag.VariantEnvironmentVariable);
            // The final model (V6) is the only model whose results the application shows and stores.
            SkinDevApp.V6.V6FeatureFlag.Provider = () => true;
            SkinDevApp.V6.V6SessionSummary.FaceBoxProvider = SkinDevApp.V6.AIResultStore.FaceBoxOf;       // facial regions are named relative to the saved face box

            // Crisp rendering on every window (including the ones created in code): snap to whole pixels, ClearType text,
            // high-quality image scaling. Combined with the DPI-aware manifest this removes the soft, blurry look.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, args) =>
            {
                var w = s as Window;
                if (w == null) return;
                w.UseLayoutRounding = true;
                w.SnapsToDevicePixels = true;
                TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
                TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
                RenderOptions.SetClearTypeHint(w, ClearTypeHint.Enabled);
                RenderOptions.SetBitmapScalingMode(w, BitmapScalingMode.HighQuality);

                // dialog windows fade in softly (the main window animates its pages instead)
                var content = w.Content as UIElement;
                if (content != null && !(w is MainWindow))
                {
                    content.Opacity = 0;
                    var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
                    fade.Completed += (a, b) => { content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1; };
                    content.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }));

            // touchscreen: LUMYVUE's own keyboard opens when a text field is tapped (never for a mouse click or a physical keyboard)
            OnScreenKeyboard.Install();

            // ---- LUMYVUE start-up: the splash screen reports each real step as it finishes ----------------------------------
            var splash = new SplashWindow();
            splash.Show();
            await Task.Delay(300);                                              // let the wordmark appear before the work starts

            // 1. database and settings
            splash.Step("Initializing system", 0.12, "Opening the patient database");
            string dbProblem = await Task.Run(() =>
            {
                try
                {
                    Data.StudyDatabase.Initialize();     // participant / scan / validation tables (same file)
                    Data.ScanSettings.Current.ApplyGlobal();
                    return null;
                }
                catch (Exception ex) { return ex.Message; }
            });
            splash.Step("Initializing system", 0.26, dbProblem == null ? "Database ready" : "Database problem: " + dbProblem);

            // Safety net: re-index scans that are on disk (and belong to a registered participant) but missing from the database.
            _ = Task.Run(() => { try { Data.SessionImporter.RecoverLinkedSessions(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Recover] " + ex.Message); } })
                .ContinueWith(_ => SkinDevApp.V6.RecordsMaintenance.RunAsync());      // final-model results in the database, folders organised, older scans re-analysed

            // 2. camera: find the webcam the scan screen will use (it is opened later, on the scan screen)
            splash.Step("Connecting camera", 0.34, "Looking for cameras");
            string camera = await Task.Run(() =>
            {
                try
                {
                    var devices = new AForge.Video.DirectShow.FilterInfoCollection(AForge.Video.DirectShow.FilterCategory.VideoInputDevice);
                    if (devices.Count == 0) return null;
                    int i = CameraDiscovery.PickIndex(devices);
                    return devices[i >= 0 && i < devices.Count ? i : 0].Name;
                }
                catch { return null; }
            });
            splash.Step("Connecting camera", 0.46, camera != null ? "Camera found: " + camera : "No camera connected yet. It can be connected later.");

            // 3. the skin model and the lesion detector (if a model file is missing this fails quietly here and is reported later)
            splash.Step("Preparing AI models", 0.52, "Loading the skin model");
            await Task.Run(() => { try { AiEngine.EnsureLoaded(); } catch { } });
            splash.Step("Preparing AI models", 0.68, "Loading the lesion detector");
            await Task.Run(() => { try { AI.LesionEngine.EnsureLoaded(); } catch { } });
            splash.Step("Preparing AI models", 0.78, AiEngine.IsAvailable ? "Skin model ready" : "Skin model not available: " + (AiEngine.LoadErrorMessage ?? "not loaded"));

            // 4. the workspace
            splash.Step("Loading workspace", 0.92, "Opening LUMYVUE");
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            splash.Topmost = true;                                              // keep the splash on top while it fades into the app
            await splash.FinishAsync();
            splash.Close();
            main.Activate();
        }
    }
}
