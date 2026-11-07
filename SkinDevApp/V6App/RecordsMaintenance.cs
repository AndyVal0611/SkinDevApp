// ============================================================================
// RecordsMaintenance.cs  -  namespace SkinDevApp.V6
//
// Start-up housekeeping of the patient records, run once in the background after the database is ready:
//   1. every photograph that already has a final-model result on disk gets its database rows (AIResultStore.SyncSession);
//   2. every scan folder is put in its place under "Patient Records" (ScanStorage.PlaceAll; first run moves the older Captures folders);
//   3. photographs taken before the final model was installed are assessed from their saved image ("Re-analysis"), so every scan
//      in the application shows the same model. The earlier model's stored values are kept in the database for audit, not shown.
// Progress is published through Status so the records screen can show it. Nothing is deleted.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkinDevApp.Data;
using SkinDevApp.Imaging;

namespace SkinDevApp.V6
{
    public static class RecordsMaintenance
    {
        private static int _running;
        public static string Status { get; private set; } = "Not started";
        public static event Action StatusChanged;

        private static void Report(string s) { Status = s; try { StatusChanged?.Invoke(); } catch { } }

        public static Task RunAsync() => Task.Run(() =>
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                if (!StudyDatabase.IsReady) { Report("Database not available"); return; }
                Report("Checking stored AI results");
                foreach (string id in AllSessions())
                    try { AIResultStore.SyncSession(id); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Sync] " + id + ": " + ex.Message); }

                Report("Organising scan folders");
                int moved = 0;
                try { moved = ScanStorage.PlaceAll(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[PlaceAll] " + ex.Message); }

                int done = Reanalyse();
                Report("Up to date" + (moved > 0 ? " · " + moved + " folder(s) organised" : "") + (done > 0 ? " · " + done + " older photograph(s) re-analysed" : ""));
            }
            catch (Exception ex) { Report("Stopped: " + ex.Message); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });

        private static System.Collections.Generic.List<string> AllSessions()
        {
            var list = new System.Collections.Generic.List<string>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT ScanSessionID FROM ScanSessions WHERE Finalized=1;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

        /// <summary>Assesses every stored photograph that has no final-model result yet. Returns how many photographs were assessed.</summary>
        public static int Reanalyse()
        {
            var sessions = AIResultStore.SessionsMissingResults();
            int done = 0, n = 0;
            foreach (string sid in sessions)
            {
                n++;
                Report("Re-analysing older scans with the final model (" + n + " of " + sessions.Count + ")");
                foreach (var v in AIResultStore.ViewsOf(sid).Where(x => !x.HasRow))
                {
                    string img = File.Exists(v.Analysed ?? "") ? v.Analysed : File.Exists(v.Original ?? "") ? v.Original : null;
                    if (img == null || string.IsNullOrEmpty(v.Folder) || !Directory.Exists(v.Folder)) continue;
                    try
                    {
                        using (Mat m = Cv2.ImRead(img, ImreadModes.Color))
                        {
                            if (m.Empty()) continue;
                            FrameQuality q;
                            using (var an = new FrameQualityAnalyzer()) q = an.Analyze(m);
                            UnifiedAssessment a = V6Service.Assess(m, q, true);      // a saved photograph already passed the live face / stability gates when it was taken: a face the analyser cannot find again is a warning, as for uploads
                            if (a == null || a.State == V6States.InferenceError) continue;          // models unavailable: try again next start, never store a failure as a result
                            V6Archive.Write(v.Folder, a, m, q != null ? q.FaceBox : (Rect?)null, AIResultStore.OriginReanalysis);
                            AIResultStore.Store(sid, v.CaptureId, v.Folder, a, AIResultStore.OriginReanalysis);
                            done++;
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Reanalyse] " + v.CaptureId + ": " + ex.Message); }
                }
                try { AIResultStore.RefreshSession(sid); } catch { }
            }
            return done;
        }
    }
}
