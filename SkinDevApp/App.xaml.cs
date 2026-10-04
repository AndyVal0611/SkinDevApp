using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

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