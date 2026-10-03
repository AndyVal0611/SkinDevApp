// GradCamLauncher.cs - finds and starts start_gradcam_service.bat (the Grad-CAM++ explanation service).
using System;
using System.Diagnostics;
using System.IO;

namespace SkinDevApp.Views
{
    public static class GradCamLauncher
    {
        public const string ScriptName = "start_gradcam_service.bat";

        /// <summary>The script next to the app, or in the nearest parent folder (the project folder during development).</summary>
        public static string FindScript()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, ScriptName);
                if (File.Exists(candidate)) return candidate;
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent != null ? parent.FullName : null;
            }
            return null;
        }

        /// <summary>Opens the service in its own console window (closing that window stops the service).</summary>
        public static void Start(string script)
        {
            Process.Start(new ProcessStartInfo(script)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(script)
            });
        }
    }
}
