// ============================================================================
// V6LiveReader.cs  -  namespace SkinDevApp.V6   (application-coupled: camera loop side)
//
// The V6 LIVE reading: every few seconds it runs the full V6 assessment (Global model + the three detector ensembles + trained fusion) on the newest camera
// frame, publishes it for the live panel, and keeps its evidence heat map for the live preview. One assessment at a time, newest frame wins, paused while a
// capture is being finalized. It only runs when V6FeatureFlag is on. It never decides when to capture (that stays with V5's steadiness logic) and never saves
// anything: the saved result comes from the final analysis of the captured frame, as before.
//
// Timing: one assessment takes about 2 s on CPU, so the reading refreshes every 2-3 s, not per frame.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.V6
{
    public sealed class V6LiveReader : IDisposable
    {
        private sealed class Pending : IDisposable
        {
            public Mat Working, Thumb; public FrameQuality Quality;
            public void Dispose() { Working?.Dispose(); Thumb?.Dispose(); }
        }

        private readonly object _lock = new object();
        private Pending _pending;
        private UnifiedAssessment _latest;
        private DateTime _latestUtc;
        private Mat _srcThumb;
        private V6LiveOverlayState _overlay;
        private int _busy;                                                  // 1 while an assessment is running

        /// <summary>Pause between two assessments (idle gap after one finishes).</summary>
        public int IntervalMs { get; set; } = 800;
        /// <summary>The evidence overlay fades out completely after this many seconds.</summary>
        public double MaxAgeSeconds { get; set; } = 8.0;

        public event Action<UnifiedAssessment> Updated;

        public UnifiedAssessment Latest { get { lock (_lock) return _latest; } }
        public bool IsAssessing => Volatile.Read(ref _busy) == 1;

        /// <summary>The camera loop offers its newest frame (clones are taken here; the caller keeps its own).</summary>
        public void Offer(Mat working, Mat thumb, FrameQuality quality)
        {
            if (working == null || working.Empty()) return;
            var p = new Pending { Working = working.Clone(), Thumb = thumb?.Clone(), Quality = quality };
            lock (_lock) { _pending?.Dispose(); _pending = p; }
        }

        public async Task RunAsync(Func<bool> paused, CancellationToken ct)
        {
            try { await Task.Run(() => V6Service.EnsureLoaded(), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.WriteLine("[V6 LIVE] load failed: " + ex.Message); }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (paused != null && paused()) { await Task.Delay(150, ct).ConfigureAwait(false); continue; }
                    Pending p; lock (_lock) { p = _pending; _pending = null; }
                    if (p == null) { await Task.Delay(100, ct).ConfigureAwait(false); continue; }

                    Interlocked.Exchange(ref _busy, 1);
                    UnifiedAssessment a;
                    try { a = V6Service.Assess(p.Working, p.Quality, false); }
                    finally { Interlocked.Exchange(ref _busy, 0); }

                    if (a != null)
                    {
                        V6LiveOverlayState ov = a.State == V6States.InferenceError ? null : V6LiveOverlay.Prepare(a, p.Working.Size(), p.Quality?.FaceBox);
                        lock (_lock)
                        {
                            _overlay?.Dispose(); _srcThumb?.Dispose();
                            _latest = a; _latestUtc = DateTime.UtcNow; _overlay = ov; _srcThumb = p.Thumb; p.Thumb = null;      // the thumbnail now belongs to the reader
                        }
                        Updated?.Invoke(a);
                    }
                    p.Dispose();
                    await Task.Delay(IntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine("[V6 LIVE] tick failed: " + ex);
                    try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>The live frame with the evidence heat map of the lead condition on top, faded by age and motion. Null when there is nothing valid to draw. Caller disposes.</summary>
        public Mat RenderOverlay(Mat liveFrame, double motionThreshold)
        {
            if (liveFrame == null || liveFrame.Empty()) return null;
            using (Mat cur = MotionMeter.MakeThumb(liveFrame))
            {
                lock (_lock)
                {
                    if (_overlay == null || _srcThumb == null) return null;
                    double age = (DateTime.UtcNow - _latestUtc).TotalSeconds;
                    double motion = MotionMeter.Between(_srcThumb, cur);
                    double factor = V6LiveOverlay.Factor(age, MaxAgeSeconds, motion, motionThreshold);
                    return V6LiveOverlay.Render(liveFrame, _overlay, factor);
                }
            }
        }

        /// <summary>Forget the current reading and any waiting frame (new scan, camera change).</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _pending?.Dispose(); _pending = null;
                _overlay?.Dispose(); _overlay = null;
                _srcThumb?.Dispose(); _srcThumb = null;
                _latest = null;
            }
        }

        public void Dispose() => Clear();
    }
}
