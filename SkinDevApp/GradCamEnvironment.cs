// GradCamEnvironment.cs - makes the Grad-CAM++ service portable between Windows PCs.
//
// It inspects THIS computer (Windows version, CPU architecture, logical processors, RAM, GPU names, free port 8765),
// locates the project files (gradcam_service.py, precisionskin_best.keras, labels.json), picks the first Python that can
// import TensorFlow, and starts the service with those real paths - no hand-written terminal command, no per-laptop edits.
// It never changes the model or its preprocessing; it only starts / connects to the existing local service.
//
//   * never starts a second copy: a healthy service on the port is simply reported Ready
//   * never blocks the UI thread: everything runs in Task.Run / async
//   * stdout + stderr of the service are written to logs\gradcam_service.log next to the app
//   * a service started by the app is stopped when the app exits (a service you started yourself is left alone)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using SkinDevApp.Explainability;

namespace SkinDevApp
{
    public enum GradCamState
    {
        Ready, Starting, PythonNotFound, DependencyMissing, ModelMissing, ScriptMissing, PortUnavailable, StartFailed, Unavailable
    }

    public sealed class PythonCandidate
    {
        public string File { get; set; }
        public string PrefixArgs { get; set; } = "";
        public string Label => string.IsNullOrEmpty(PrefixArgs) ? File : File + " " + PrefixArgs;
        public bool Exists { get; set; }
        public bool HasTensorFlow { get; set; }
        public string TensorFlowVersion { get; set; }
        public string Error { get; set; }
    }

    public sealed class GradCamEnvInfo
    {
        public string WindowsVersion { get; set; }
        public string Architecture { get; set; }
        public int LogicalProcessors { get; set; }
        public double RamGb { get; set; }
        public List<string> Gpus { get; set; } = new List<string>();
        public string ProjectDir { get; set; }
        public string ServiceScript { get; set; }
        public string ModelPath { get; set; }
        public string LabelsPath { get; set; }
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8765;
        public bool PortFree { get; set; }
        public bool ServiceHealthy { get; set; }
        public List<PythonCandidate> Candidates { get; set; } = new List<PythonCandidate>();
        public PythonCandidate Python { get; set; }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows " + WindowsVersion + " (" + Architecture + "), " + LogicalProcessors + " logical processors, " + RamGb.ToString("0.0") + " GB RAM");
            sb.AppendLine("GPU: " + (Gpus.Count == 0 ? "none detected" : string.Join("; ", Gpus)) + "  (the service runs on CPU)");
            sb.AppendLine("Project folder: " + (ProjectDir ?? "NOT FOUND"));
            sb.AppendLine("Port " + Port + ": " + (ServiceHealthy ? "service already running" : PortFree ? "free" : "occupied by another program"));
            sb.AppendLine("Python: " + (Python != null ? Python.Label + " (TensorFlow " + Python.TensorFlowVersion + ")" : "none with TensorFlow found"));
            foreach (PythonCandidate c in Candidates)
                sb.AppendLine("   tried " + c.Label + ": " + (!c.Exists ? "not installed" : c.HasTensorFlow ? "OK" : "no TensorFlow (" + (c.Error ?? "import failed") + ")"));
            return sb.ToString();
        }
    }

    public sealed class GradCamStartResult
    {
        public GradCamState State { get; set; }
        public string Message { get; set; }
        public string LogPath { get; set; }
        public GradCamEnvInfo Env { get; set; }
    }

    public static class GradCamEnvironment
    {
        private static readonly object Gate = new object();
        private static Process _started;
        private static bool _exitHooked;
        private static PythonCandidate _cachedPython;
        private static bool _starting;

        public static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        public static string LogPath => Path.Combine(LogDirectory, "gradcam_service.log");

        // ------------------------------------------------------------ locate --

        /// <summary>Folder holding gradcam_service.py: next to the app, or the nearest parent (the project folder during development).</summary>
        public static string FindProjectDir()
        {
            string env = Environment.GetEnvironmentVariable("PRECISIONSKIN_HOME");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "gradcam_service.py"))) return env;

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 7 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "gradcam_service.py"))) return dir;
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent != null ? parent.FullName : null;
            }
            return null;
        }

        private static string FirstExisting(params string[] paths) => paths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

        // ----------------------------------------------------------- hardware --

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        private static string WindowsVersionText()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null) return ((k.GetValue("ProductName") as string) ?? "Windows") + " build " + (k.GetValue("CurrentBuild") as string ?? "?");
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static List<string> GpuNames()
        {
            var list = new List<string>();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-b6a3-3c1d6e6a9b7f}"))
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                string n = k == null ? null : k.GetValue("DriverDesc") as string;
                                if (!string.IsNullOrWhiteSpace(n) && !list.Contains(n)) list.Add(n);
                            }
            }
            catch { }
            return list;
        }

        public static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        // -------------------------------------------------------------- python --

        private static IEnumerable<PythonCandidate> CandidateList(string projectDir)
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (projectDir != null) yield return new PythonCandidate { File = Path.Combine(projectDir, ".venv", "Scripts", "python.exe") };
            string envPy = Environment.GetEnvironmentVariable("PRECISIONSKIN_PYTHON");
            if (!string.IsNullOrEmpty(envPy)) yield return new PythonCandidate { File = envPy };
            yield return new PythonCandidate { File = "py", PrefixArgs = "-3.12" };
            yield return new PythonCandidate { File = "py", PrefixArgs = "-3.11" };
            yield return new PythonCandidate { File = "py", PrefixArgs = "-3.10" };
            yield return new PythonCandidate { File = "python" };
            if (!string.IsNullOrEmpty(local))
                foreach (string v in new[] { "312", "311", "310" })
                    yield return new PythonCandidate { File = Path.Combine(local, "Programs", "Python", "Python" + v, "python.exe") };
        }

        private static bool Run(string file, string args, int timeoutMs, out string stdout, out string stderr, out int exit)
        {
            stdout = stderr = ""; exit = -1;
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } stderr = "timed out"; return false; }
                    stdout = o.Result.Trim(); stderr = e.Result.Trim(); exit = p.ExitCode;
                    return true;
                }
            }
            catch (Exception ex) { stderr = ex.Message; return false; }
        }

        private static void TestPython(PythonCandidate c)
        {
            bool isPath = c.File.IndexOf(Path.DirectorySeparatorChar) >= 0;
            if (isPath && !File.Exists(c.File)) { c.Exists = false; return; }
            string so, se; int ec;
            string args = (c.PrefixArgs + " -c \"import sys; print(sys.version_info[0]); import tensorflow as tf; print(tf.__version__)\"").Trim();
            if (!Run(c.File, args, 90000, out so, out se, out ec)) { c.Exists = !se.Contains("cannot find") && !se.Contains("No such file") && !se.Contains("The system cannot find"); c.Error = se; return; }
            c.Exists = true;
            if (ec == 0) { c.HasTensorFlow = true; c.TensorFlowVersion = so.Split('\n').Last().Trim(); }
            else
            {
                string last = (se ?? "").Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "";
                // "py -3.12" with no such Python installed prints "No suitable Python"/"not found": treat as not installed
                if (last.IndexOf("No suitable Python", StringComparison.OrdinalIgnoreCase) >= 0 || last.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 && last.IndexOf("tensorflow", StringComparison.OrdinalIgnoreCase) < 0)
                    c.Exists = false;
                c.Error = last.Trim();
            }
        }

        // -------------------------------------------------------------- detect --

        /// <summary>Inspects this PC and project. Slow (Python + TensorFlow import is tested): call from a background task.</summary>
        public static GradCamEnvInfo Detect(bool testPython = true)
        {
            var info = new GradCamEnvInfo
            {
                WindowsVersion = WindowsVersionText(),
                Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                LogicalProcessors = Environment.ProcessorCount,
                Gpus = GpuNames()
            };
            var ms = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
            if (GlobalMemoryStatusEx(ref ms)) info.RamGb = ms.ullTotalPhys / 1073741824.0;

            info.ProjectDir = FindProjectDir();
            if (info.ProjectDir != null)
            {
                info.ServiceScript = Path.Combine(info.ProjectDir, "gradcam_service.py");
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                info.ModelPath = FirstExisting(Path.Combine(info.ProjectDir, "precisionskin_best.keras"), Path.Combine(info.ProjectDir, "SkinDevApp", "precisionskin_best.keras"), Path.Combine(appDir, "precisionskin_best.keras"));
                info.LabelsPath = FirstExisting(Path.Combine(info.ProjectDir, "labels.json"), Path.Combine(info.ProjectDir, "SkinDevApp", "labels.json"), Path.Combine(appDir, "labels.json"));
            }

            Uri u = new Uri(GradCamService.BaseUrl);
            info.Host = u.Host; info.Port = u.Port;
            info.PortFree = IsPortFree(info.Port);

            if (testPython)
            {
                if (_cachedPython != null && (!_cachedPython.File.Contains(Path.DirectorySeparatorChar.ToString()) || File.Exists(_cachedPython.File)))
                {
                    info.Python = _cachedPython;
                    info.Candidates.Add(_cachedPython);
                }
                else
                {
                    foreach (PythonCandidate c in CandidateList(info.ProjectDir))
                    {
                        TestPython(c);
                        info.Candidates.Add(c);
                        if (c.HasTensorFlow) { info.Python = c; _cachedPython = c; break; }
                    }
                }
            }
            return info;
        }

        // --------------------------------------------------------------- start --

        public static bool IsStartedByApp => _started != null && !_started.HasExited;

        /// <summary>
        /// Makes sure the service is running. Healthy service → Ready (nothing is started). Otherwise detect → start → poll /health.
        /// Progress text is for the status line. Safe to call from the UI thread (async).
        /// </summary>
        public static async Task<GradCamStartResult> EnsureRunningAsync(IProgress<string> progress = null, int timeoutSeconds = 150)
        {
            var result = new GradCamStartResult { LogPath = LogPath };

            GradCamHealth h0 = await GradCamService.GetHealthAsync().ConfigureAwait(false);
            if (h0 != null && h0.Ok) { result.State = GradCamState.Ready; result.Message = "Service already running (v" + h0.ServiceVersion + ")."; return result; }

            lock (Gate) { if (_starting) { result.State = GradCamState.Starting; result.Message = "A start is already in progress."; return result; } _starting = true; }
            try
            {
                if (progress != null) progress.Report("Checking this PC and the Python environment...");
                GradCamEnvInfo env = await Task.Run(() => Detect()).ConfigureAwait(false);
                result.Env = env;

                if (env.ProjectDir == null || !File.Exists(env.ServiceScript)) { result.State = GradCamState.ScriptMissing; result.Message = "gradcam_service.py was not found next to the app or in a parent folder. Set PRECISIONSKIN_HOME to the project folder."; return result; }
                if (env.ModelPath == null) { result.State = GradCamState.ModelMissing; result.Message = "precisionskin_best.keras was not found in the project folder."; return result; }
                if (env.Python == null)
                {
                    bool anyPython = env.Candidates.Any(c => c.Exists);
                    result.State = anyPython ? GradCamState.DependencyMissing : GradCamState.PythonNotFound;
                    result.Message = anyPython
                        ? "Python was found but TensorFlow is missing. In the project folder run:  py -3.12 -m venv .venv  then  .venv\\Scripts\\python -m pip install -r requirements_gradcam.txt"
                        : "No Python 3.10-3.12 was found. Install Python 3.12 (tick 'Add python to PATH'), then press Retry.";
                    return result;
                }
                if (!env.PortFree) { result.State = GradCamState.PortUnavailable; result.Message = "Port " + env.Port + " is used by another program that is not the Grad-CAM++ service. Close it and press Retry."; return result; }

                Directory.CreateDirectory(LogDirectory);
                string args = (env.Python.PrefixArgs + " \"" + env.ServiceScript + "\" --model \"" + env.ModelPath + "\"" +
                               (env.LabelsPath != null ? " --labels \"" + env.LabelsPath + "\"" : "") + " --host " + env.Host + " --port " + env.Port).Trim();
                if (progress != null) progress.Report("Starting the service (loading the model can take up to a minute)...");

                Process p;
                try
                {
                    var psi = new ProcessStartInfo(env.Python.File, args)
                    {
                        UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = env.ProjectDir,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                    p = Process.Start(psi);
                }
                catch (Exception ex) { result.State = GradCamState.StartFailed; result.Message = "Could not launch Python: " + ex.Message; return result; }

                var log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                log.WriteLine("[" + DateTime.Now.ToString("s") + "] " + env.Python.Label + " " + args);
                log.WriteLine(env.Summary());
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                _started = p;
                HookExit();

                DateTime until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    if (p.HasExited)
                    {
                        result.State = GradCamState.StartFailed;
                        result.Message = "The service stopped right after starting (exit code " + p.ExitCode + "). Last log lines:\n" + TailLog(8);
                        return result;
                    }
                    GradCamHealth h = await GradCamService.GetHealthAsync().ConfigureAwait(false);
                    if (h != null && h.Ok) { result.State = GradCamState.Ready; result.Message = "Service ready (v" + h.ServiceVersion + ")."; return result; }
                    if (progress != null) progress.Report("Starting the service... " + (int)(until - DateTime.UtcNow).TotalSeconds + " s left");
                }
                result.State = GradCamState.Unavailable;
                result.Message = "The service did not answer within " + timeoutSeconds + " s. See the log: " + LogPath;
                return result;
            }
            finally { lock (Gate) _starting = false; }
        }

        public static string TailLog(int lines)
        {
            try
            {
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    return string.Join("\n", sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Reverse().Take(lines).Reverse());
            }
            catch { return "(no log yet)"; }
        }

        private static void HookExit()
        {
            if (_exitHooked) return;
            _exitHooked = true;
            AppDomain.CurrentDomain.ProcessExit += (s, e) => StopIfStartedByApp();
        }

        /// <summary>Stops the service only if this app started it.</summary>
        public static void StopIfStartedByApp()
        {
            try { if (_started != null && !_started.HasExited) _started.Kill(); } catch { }
        }

        public static string StateTitle(GradCamState s)
        {
            switch (s)
            {
                case GradCamState.Ready: return "Ready";
                case GradCamState.Starting: return "Starting service...";
                case GradCamState.PythonNotFound: return "Python environment not found";
                case GradCamState.DependencyMissing: return "Required dependency missing";
                case GradCamState.ModelMissing: return "Model file missing";
                case GradCamState.ScriptMissing: return "Service files missing";
                case GradCamState.PortUnavailable: return "Port unavailable";
                case GradCamState.StartFailed: return "Service failed to start";
                default: return "Service unavailable";
            }
        }
    }
}
