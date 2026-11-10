// ============================================================================
// DeviceShutdown.cs  -  namespace SkinDevApp.Views
//
// The "Shut down" button: asks for confirmation, lets LUMYVUE close the normal way (the last scan is saved, a receipt that is being
// printed finishes), and then asks Windows to shut the whole device down. Windows is not forced: it closes everything itself, so the
// shutdown stays safe. The shutdown starts 5 seconds after the request, which also gives time to cancel it with "shutdown /a".
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class DeviceShutdown
    {
        /// <summary>True from the moment "Shut down" was confirmed: no new image capture may start.</summary>
        public static bool InProgress { get; set; }

        /// <summary>Asks "Are you sure?" and, on Yes, closes LUMYVUE and shuts the device down.</summary>
        public static void Ask()
        {
            if (LumyDialog.Show("Are you sure you want to shut down the device?\n\nAny scan that is still being saved will finish first. Then LUMYVUE closes and the computer turns off.",
                                "Shut down", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var main = Application.Current.MainWindow as MainWindow;
            if (main == null)
            {
                string problem = Start();
                if (problem != null) LumyDialog.Show("The device could not be shut down.\n\n" + problem, "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Warning);
                else Application.Current.Shutdown(0);
                return;
            }
            main.BeginDeviceShutdown();
        }

        /// <summary>Starts the Windows shutdown. Returns null when it was started, otherwise a plain-language reason.</summary>
        public static string Start()
        {
            try
            {
                try { StudyRepository.Audit("Device shutdown", "System", AppSession.ActorId ?? "", "Shutdown requested from LUMYVUE"); } catch { }
                var psi = new ProcessStartInfo("shutdown.exe", "/s /t 5 /c \"LUMYVUE is shutting down this device.\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return "Windows did not start the shutdown.";
                    if (!p.WaitForExit(4000)) return null;                       // still running: it is counting down
                    if (p.ExitCode == 0 || p.ExitCode == 1190) return null;      // 1190: a shutdown is already scheduled
                    return "Windows refused the shutdown (error " + p.ExitCode + ").";
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Writes an unexpected error to crash.log next to the data (never throws).</summary>
        public static void LogCrash(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUMYVUE");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (ex == null ? "unknown error" : ex.ToString()) + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
