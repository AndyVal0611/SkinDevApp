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
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SkinDevApp.Views.ScanInterpreter.Settings = SkinDevApp.Views.InterpretationSettings.From(k => System.Configuration.ConfigurationManager.AppSettings[k]);

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
                    DatabaseHelper.InitializeDatabase(); // Creates lumyvue_db.sqlite table automatically
                    Data.StudyDatabase.Initialize();     // participant / scan / validation tables (same file)
                    Data.ScanSettings.Current.ApplyGlobal();
                    return null;
                }
                catch (Exception ex) { return ex.Message; }
            });
            splash.Step("Initializing system", 0.26, dbProblem == null ? "Database ready" : "Database problem: " + dbProblem);

            // Safety net: re-index scans that are on disk (and belong to a registered participant) but missing from the database.
            Task.Run(() => { try { Data.SessionImporter.RecoverLinkedSessions(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Recover] " + ex.Message); } });

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

            // 4. is the heat-map service already running? (only checked here; it is started from the preparation screen as before)
            splash.Step("Checking heat-map service", 0.84, " ");
            bool heatmaps = false;
            try
            {
                var health = Explainability.GradCamService.GetHealthAsync();
                if (await Task.WhenAny(health, Task.Delay(1500)) == health && health.Result != null) heatmaps = health.Result.Ok;
            }
            catch { }
            splash.Step("Checking heat-map service", 0.88, heatmaps ? "Heat-map service running" : "Heat-map service will start when a scan is prepared");

            // 5. the workspace
            splash.Step("Loading workspace", 0.94, "Opening LUMYVUE");
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
