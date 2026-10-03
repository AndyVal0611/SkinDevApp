// ============================================================================
// SaveQueue.cs  -  namespace SkinDevApp.Data
//
// Runs scan-to-database saves one after another on a worker thread and keeps count of
// what is still unfinished, so the app can wait for them before it closes.
//
// The scan folders on disk are always written first; the database rows are an index of
// them. If a save fails, the scan is not lost: SessionImporter.RecoverLinkedSessions()
// re-indexes it at the next start.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkinDevApp.Data
{
    public static class SaveQueue
    {
        private static readonly object Gate = new object();
        private static Task _tail = Task.CompletedTask;
        private static int _pending;

        /// <summary>Saves queued or running right now.</summary>
        public static int Pending => Volatile.Read(ref _pending);

        /// <summary>Queue a save. The returned task faults if the work throws.</summary>
        public static Task<string> Enqueue(Func<string> work)
        {
            Interlocked.Increment(ref _pending);
            lock (Gate)
            {
                Task<string> t = _tail.ContinueWith(_ =>
                {
                    try { return work(); }
                    finally { Interlocked.Decrement(ref _pending); }
                }, TaskScheduler.Default);

                _tail = t.ContinueWith(x => { var ignored = x.Exception; }, TaskScheduler.Default);   // keep the chain going after a failure
                return t;
            }
        }

        /// <summary>Wait until every queued save has finished. False when the time ran out.</summary>
        public static bool WaitAll(TimeSpan timeout)
        {
            Task t;
            lock (Gate) t = _tail;
            try { return t.Wait(timeout); }
            catch (AggregateException) { return true; }
        }
    }
}
