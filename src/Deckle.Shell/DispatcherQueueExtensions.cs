using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Deckle.Shell;

// Wrappers around `DispatcherQueue.TryEnqueue` for every site that marshals a
// callback to the UI thread. A rejected enqueue (queue shut down, typically an
// engine StatusChanged arriving during window teardown) would otherwise lose
// the UI event silently; both wrappers log it on `DeckleThreadingSource`.
//
// `TryEnqueueObserved` also emits `MarshalQueued` and `MarshalCompleted` with
// `wait_ms` and `run_ms`. Unless the Windowing policy and a Verbose Threading
// listener both admit them it falls back to `TryEnqueueOrLog`, so the
// instrumentation costs one admission check.
//
// Two thread-static guards break feedback loops through an event listener that
// marshals what it receives, as the LogWindow sink does:
//   • `_logging`: a rejection Warning routed back into a closed queue would be
//     rejected and logged again, forever.
//   • `_emittingMarshal`: `MarshalQueued` is emitted synchronously, so a
//     listener that marshals through `TryEnqueueObserved` re-enters on the same
//     thread and recurses until the stack overflows. A re-entrant call takes
//     the cold `TryEnqueueOrLog` path, which enqueues without emitting; only
//     the observation of nested marshalling is lost.
public static class DispatcherQueueExtensions
{
    [System.ThreadStatic]
    private static bool _logging;

    [System.ThreadStatic]
    private static bool _emittingMarshal;

    /// <summary>
    /// Enqueues the callback on the dispatcher queue. If enqueue fails (closed
    /// queue), emits a Warning on DeckleThreadingSource with the caller source
    /// and supplied description, then returns false.
    /// </summary>
    /// <param name="queue">Target dispatcher queue.</param>
    /// <param name="callback">Delegate to execute on the UI thread.</param>
    /// <param name="source">Free-form emitter identifier (e.g. "HUD", "LOGWIN"). Passed as an event payload field.</param>
    /// <param name="what">Short description of the lost event (e.g. "log entry", "recording state").</param>
    /// <param name="priority">Dispatcher queue scheduling priority. Default Normal. Pass Low to defer the callback after the current layout batch (coordination pattern used by Settings pages to clear `_initializing` after control hydration).</param>
    public static bool TryEnqueueOrLog(
        this DispatcherQueue queue,
        DispatcherQueueHandler callback,
        string source,
        string what,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        bool ok = queue.TryEnqueue(priority, callback);
        if (!ok && !_logging)
        {
            _logging = true;
            try
            {
                DeckleThreadingSource.Log.DispatcherEnqueueRejected();
                DeckleThreadingSource.Log.DispatcherEnqueueRejectedDetail(source, what);
            }
            finally { _logging = false; }
        }
        return ok;
    }

    /// <summary>
    /// Enqueues the callback on the dispatcher queue while instrumenting it for
    /// the cross-cutting Threading sub-provider. Emits `MarshalQueued` before
    /// enqueue, measures `wait_ms` (latency) and `run_ms` (execution) around
    /// the callback, and emits `MarshalCompleted` at the end. If enqueue fails,
    /// emits `DispatcherEnqueueRejected` exactly like `TryEnqueueOrLog`. Gated by
    /// `OperationalLogAdmission.IsDetailEnabled(Windowing, Verbose, Threading)`:
    /// when it refuses, falls back to `TryEnqueueOrLog` (zero extra allocation for instrumentation).
    /// </summary>
    /// <param name="queue">Target dispatcher queue.</param>
    /// <param name="operation">Short name of the marshalled operation (closed vocabulary documented on DeckleThreadingSource).</param>
    /// <param name="caller">Short logical-site name (e.g. "log-window", "hud-window", "overlay-manager").</param>
    /// <param name="callback">Delegate to execute on the UI thread.</param>
    /// <param name="rejectSource">Free-form identifier passed to DispatcherEnqueueRejected if enqueue fails (e.g. "HUD", "LOGWIN").</param>
    /// <param name="rejectWhat">Short description of the lost event (e.g. "log entry", "overlay enqueue").</param>
    /// <param name="priority">Dispatcher queue scheduling priority, propagated to the underlying TryEnqueue. Default Normal. Pass Low to defer the callback after the current layout batch: coordination pattern used by Settings pages to clear `_initializing` after control hydration.</param>
    public static bool TryEnqueueObserved(
        this DispatcherQueue queue,
        string operation,
        string caller,
        DispatcherQueueHandler callback,
        string rejectSource,
        string rejectWhat,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        // A marshal runs per log entry and per UI status change, so its detail
        // answers to the persisted Windowing policy as well as the listener:
        // the UI-thread hand-off is window plumbing, and the closed activity
        // vocabulary has no threading entry of its own.
        bool verboseEnabled = OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleThreadingSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Threading);

        // Cold path, also taken on re-entry (see `_emittingMarshal` above):
        // enqueue without emitting. The rejection Warning still fires,
        // independently of Verbose.
        if (!verboseEnabled || _emittingMarshal)
        {
            return queue.TryEnqueueOrLog(callback, rejectSource, rejectWhat, priority);
        }

        _emittingMarshal = true;
        try
        {
            var sw = Stopwatch.StartNew();
            DeckleThreadingSource.Log.MarshalQueued(operation, caller, queue_depth: -1);

            bool ok = queue.TryEnqueue(priority, () =>
            {
                int wait_ms = (int)sw.ElapsedMilliseconds;
                sw.Restart();
                try
                {
                    callback();
                }
                finally
                {
                    int run_ms = (int)sw.ElapsedMilliseconds;
                    DeckleThreadingSource.Log.MarshalCompleted(
                        operation, caller, wait_ms, run_ms);
                }
            });

            if (!ok && !_logging)
            {
                _logging = true;
                try
                {
                    DeckleThreadingSource.Log.DispatcherEnqueueRejected();
                    DeckleThreadingSource.Log.DispatcherEnqueueRejectedDetail(
                        rejectSource, rejectWhat);
                }
                finally { _logging = false; }
            }
            return ok;
        }
        finally { _emittingMarshal = false; }
    }
}
