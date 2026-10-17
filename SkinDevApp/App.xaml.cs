using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SkinDevApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            DatabaseHelper.InitializeDatabase(); // Creates lumyvue_db.sqlite table automatically
            Data.StudyDatabase.Initialize();     // participant / scan / validation tables (same file)
            Data.ScanSettings.Current.ApplyGlobal();

            // Safety net: re-index scans that are on disk (and belong to a registered participant) but missing from the database.
            Task.Run(() => { try { Data.SessionImporter.RecoverLinkedSessions(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Recover] " + ex.Message); } });

            // Warm up the ONNX model in the background so it's already loaded
            // by the time the user reaches the dashboard and clicks Analyze.
            // If precisionskin.onnx hasn't been added to the project yet,
            // this fails quietly here - AiEngine.IsAvailable/LoadErrorMessage
            // report it later instead of crashing the app at startup.
            Task.Run(() => AiEngine.EnsureLoaded());

            // Lesion localization detector (separate model). Same quiet-failure behaviour.
            Task.Run(() => AI.LesionEngine.EnsureLoaded());

        }
    }
}