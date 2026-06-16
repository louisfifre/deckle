using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Deckle.Shell;

// ─── DispatcherQueueExtensions ─────────────────────────────────────────────
//
// Wrappers around `DispatcherQueue.TryEnqueue`, consumed by every Deckle site
// that marshals a callback to the UI thread.
//
// `TryEnqueueOrLog` is the historical wrapper: emits a
// `DispatcherEnqueueRejected` Warning when enqueue fails (queue shut down).
// Without it, the UI event is silently lost, typical during window teardown
// when engine StatusChanged arrives after the dispatcher queue is already
// closed. Since the cross-cutting instrumentation wave, the Warning is emitted
// on `DeckleThreadingSource` (cross-cutting `Deckle.Diagnostics.Threading`
// sub-provider). The event did not describe a shell operation; it described a
// dispatcher rejection crossing any module that marshals to the UI thread. The
// public signature for callers stays identical (`source` and `what`) to avoid
// breaking existing sites.
//
// `TryEnqueueObserved` is the instrumented wrapper from the cross-cutting wave:
// in addition to rejection, it emits the `MarshalQueued` (before TryEnqueue)
// and `MarshalCompleted` (at callback end) pair with `wait_ms` (marshalling
// latency) and `run_ms` (callback execution duration) measurements. Strict
// `IsEnabled(Verbose, Threading)` gate at the top: when no listener is attached,
// instrumentation has zero net cost (one ETW test + return), and the wrapper
// falls back to `TryEnqueueOrLog` behavior. `MarshalTimeout` remains declared
// on the provider but is not actively wired in this pass; its contract is
// frozen for a later pass that will detect callbacks that stayed too long in
// queue through a dedicated watchdog.
//
// Anti-recursion guard `_logging` (warning path). If LogWindow calls one of
// these wrappers and its own queue is closed, the logged Warning routes back to
// LogWindow → re-TryEnqueue → re-fail → loop. A thread-static flag
// short-circuits the second attempt. The guard remains relevant after the
// EventSource migration: `LogWindowSink` still receives the Warning event and
// pushes it back into the same LogWindow-side `DispatcherQueue`.
//
// Anti-recursion guard `_emittingMarshal` (verbose path). Same loop class,
// triggered by the *systematic* `MarshalQueued` emission in `TryEnqueueObserved`:
// a `LogWindow.Write` call from `LogWindowSink.Write` (invoked by
// `DispatchEventListener.OnEventWritten` on a worker thread) goes through
// `TryEnqueueObserved`, which synchronously emits `MarshalQueued`; the
// dispatcher observes it and re-routes to `LogWindow.Write`
// → new emission → synchronous recursion → stack overflow. Empirically
// observed on 2026-05-25; signature: JSONL tail flooded with `MarshalQueued
// operation=log-append caller=log-window` at several kHz, then crash. When
// reentrancy is detected, fall back to the cold `TryEnqueueOrLog` path, which
// enqueues the callback without emission. The useful event (the one that
// triggered the chain) still lands in the UI queue; only observation of nested
// marshalling is skipped.
//
// Why not a simple `if (!queue.TryEnqueue(...)) _log.Warning(...)` inline at
// each site? Centralizing reduces duplication (8 rejection sites + 5 observed
// sites) and guarantees the anti-recursion guard pattern is everywhere, with no
// risk of forgetting it.

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
    /// emits `DispatcherEnqueueRejected` exactly like `TryEnqueueOrLog`. Strict
    /// `IsEnabled(Verbose, Threading)` gate: when no listener is attached,
    /// falls back to `TryEnqueueOrLog` (zero extra allocation for instrumentation).
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
        bool verboseEnabled = DeckleThreadingSource.Log.IsEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Threading);

        // Cold path: no Queued/Completed instrumentation, just raw enqueue +
        // the historical rejection path. The Warning gate on
        // DispatcherEnqueueRejected remains open independently of Verbose. This
        // path is also used during reentrancy (see `_emittingMarshal` note at
        // top of file): enqueue without emission to avoid retriggering the
        // synchronous listener → Write → TryEnqueueObserved chain.
        if (!verboseEnabled || _emittingMarshal)
        {
            return queue.TryEnqueueOrLog(callback, rejectSource, rejectWhat, priority);
        }

        // Hot path: full instrumentation. Stopwatch captured in the closure to
        // measure wait_ms (queue → callback start) and run_ms (callback
        // duration). Guarded by thread-static `_emittingMarshal`: any
        // MarshalQueued/Completed emission that synchronously re-enters this
        // wrapper on the same thread sees the guard and switches to the cold
        // path instead of re-emitting.
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
