using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: significant dispatcher marshalling
// (`DispatcherQueue.TryEnqueue`) to the UI thread. Without this event, a UI
// deadlock or abnormal marshalling latency can at best be guessed through
// indirect correlation with business milestones; a site whose enqueue
// silently fails (queue already closed) also has no systematic trace. The
// primitive is strictly non-business (platform wiring on the
// `Microsoft.UI.Dispatching` side) and consumed by several modules with
// exactly the same parameter set.
//
// `MarshalQueued` + `MarshalCompleted` are the common trunk emitted around a
// `TryEnqueue` by `DispatcherQueueExtensions.TryEnqueueObserved`;
// `MarshalTimeout` and `DispatcherEnqueueRejected` specialize the abnormal
// cases.
//
// Closed `operation` vocabulary (extend here if a new significant site emerges;
// no ad hoc operation on the call-site side):
//   "ui-update"         — update of a XAML control from a non-UI thread, or a
//                         Low-deferred template part tweak after materialization
//   "window-show"       — showing a window from a non-UI thread
//   "feedback-display"  — HUD or overlay display from a non-UI thread
//   "log-append"        — appending an entry into the LogWindow
//   "settings-reload"   — reloading settings UI after a Changed event
//   "init-flag-clear"   — clearing an `_initializing` flag after page
//                         constructor hydrates XAML controls, Low-deferred to
//                         wait for the end of the layout batch
//                         (Settings/Playground page pattern)
//   "engine-state-sync" — UI synchronization after an engine event (pipeline
//                         state, recording transition, screen capture stop)
//
// `caller` convention: short name of the logical site ("hud-window",
// "log-window", "overlay-manager", "ambient-page", etc.). Differentiates two
// marshallings of the same `operation` on distinct sites without inflating
// the schema.
//
// `queue_depth`: `DispatcherQueue` exposes no depth getter, so `-1` stands
// for "unknown".
[EventSource(Name = "Deckle-Threading")]
public sealed class DeckleThreadingSource : DeckleEventSource
{
    public static readonly DeckleThreadingSource Log = new();

    private DeckleThreadingSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtMarshalQueued                      = 1;
    public const int EvtMarshalCompleted                   = 2;
    public const int EvtMarshalTimeout                     = 3;
    public const int EvtDispatcherEnqueueRejected          = 4;
    public const int EvtMarshalTimeoutDetail               = 5;
    public const int EvtDispatcherEnqueueRejectedDetail    = 6;

    // Trunk: emitted just before `TryEnqueue`. Verbose because one user Stop
    // chains several marshals per engine state.
    [Event(EvtMarshalQueued,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal queued | operation={0} | caller={1} | queue_depth={2}")]
    public void MarshalQueued(string operation, string caller, int queue_depth)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalQueued, operation, caller, queue_depth);
    }

    // Trunk: emitted at the end of callback execution. `wait_ms` captures
    // marshalling latency (time the callback spent in the queue), `run_ms`
    // captures the callback's own execution time. A `wait_ms` drift indicates
    // a loaded UI thread; a `run_ms` drift indicates a heavy callback that
    // should be split.
    [Event(EvtMarshalCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal completed | operation={0} | caller={1} | wait_ms={2} | run_ms={3}")]
    public void MarshalCompleted(string operation, string caller, int wait_ms, int run_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalCompleted, operation, caller, wait_ms, run_ms);
    }

    // Specialized: the callback stayed queued longer than an app threshold.
    // Warning so it surfaces even when Verbose is not listened to. No emitter
    // yet: declared to freeze the signature before detection is wired.
    [Event(EvtMarshalTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "A marshal callback did not run within the expected delay")]
    public void MarshalTimeout()
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalTimeout);
    }

    [Event(EvtMarshalTimeoutDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal timeout | operation={0} | caller={1} | waited_ms={2}")]
    public void MarshalTimeoutDetail(string operation, string caller, int waited_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalTimeoutDetail, operation, caller, waited_ms);
    }

    // Specialized: `TryEnqueue` returned false (queue shut down). In the
    // Verbose mirror, `caller_source` is the free label passed to
    // `DispatcherQueueExtensions.TryEnqueueOrLog` (e.g. "HUD", "LOGWIN") and
    // `reason` describes the lost enqueue.
    [Event(EvtDispatcherEnqueueRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "A dispatcher enqueue was rejected")]
    public void DispatcherEnqueueRejected()
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtDispatcherEnqueueRejected);
    }

    [Event(EvtDispatcherEnqueueRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "dispatcher enqueue rejected | caller_source={0} | reason={1}")]
    public void DispatcherEnqueueRejectedDetail(string caller_source, string reason)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtDispatcherEnqueueRejectedDetail, caller_source, reason);
    }
}
