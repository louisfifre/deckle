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
// `MarshalStalled` (through MarshalStallWatch) and `DispatcherEnqueueRejected`
// specialize the abnormal cases.
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
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    public const int EvtMarshalQueued                      = 1;
    public const int EvtMarshalCompleted                   = 2;
    // 3 retired.
    public const int EvtDispatcherEnqueueRejected          = 4;
    // 5 retired.
    public const int EvtDispatcherEnqueueRejectedDetail    = 6;
    public const int EvtMarshalStalled                     = 7;
    public const int EvtMarshalStallRepeated               = 8;
    public const int EvtMarshalStallRecovered              = 9;

    // Trunk: emitted just before `TryEnqueue`. Verbose because one user Stop
    // chains several marshals per engine state.
    [Event(EvtMarshalQueued,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal queued | operation={0} | caller={1} | queue_depth={2}")]
    public void MarshalQueued(string operation, string caller, int queue_depth)
    {
        HotLoops.ThreadingMarshal.Tick();
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

    // ── UI thread stall incident (MarshalStallWatch) ────────────────────
    //
    // A callback that waited at least `threshold_ms` in the queue means the
    // UI thread was blocked that long. The measure is taken when the callback
    // finally runs, so the stall is reported once it is over. `operation` and
    // `caller` name the first callback found late: a victim of the stall, not
    // necessarily its cause.
    [Event(EvtMarshalStalled,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "The UI thread ran a queued callback late | operation={0} | caller={1} | waited_ms={2} | threshold_ms={3}")]
    public void MarshalStalled(string operation, string caller, int waited_ms, int threshold_ms)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalStalled, operation, caller, waited_ms, threshold_ms);
    }

    // Each further late callback while the incident is open. At most one per
    // threshold span, so no activity gate is needed.
    [Event(EvtMarshalStallRepeated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal stall repeated | operation={0} | caller={1} | waited_ms={2} | stalls={3}")]
    public void MarshalStallRepeated(string operation, string caller, int waited_ms, int stalls)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalStallRepeated, operation, caller, waited_ms, stalls);
    }

    // First on-time callback after a stall. `duration_ms` runs from the first
    // late callback to this one; `worst_wait_ms` is the longest queue wait of
    // the incident. Journal-tagged so the journal shows the stall's end.
    [Event(EvtMarshalStallRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Threading,
           Tags = ObservationTags.Journal,
           Message = "The UI thread is responsive again | stalls={0} | worst_wait_ms={1} | duration_ms={2}")]
    public void MarshalStallRecovered(int stalls, int worst_wait_ms, long duration_ms)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Threading)) return;
        // Explicit array: with (int, int, long) the compiler would bind the
        // (int, long, long, long) overload and write every field as a long,
        // out of step with the declared signature.
        WriteEvent(EvtMarshalStallRecovered, new object[] { stalls, worst_wait_ms, duration_ms });
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

// Turns the per-callback queue wait into one stall incident. A blocked UI
// thread makes every queued callback late at once, so logging each late
// callback as a Warning would bury the journal under the same cause; the
// first late callback opens the incident (Warning), the following ones are
// Verbose, the first on-time callback closes it (Informational).
//
// Fed from the callback itself, after it ran, by every observed enqueue. The
// on-time path costs one volatile read and returns: it runs on the UI thread
// for every marshal.
public sealed class MarshalStallWatch
{
    public static readonly MarshalStallWatch Shared = new();

    // One second of a frozen UI is visible to a person; below that a busy
    // layout pass or a large XAML materialization is ordinary.
    public const int StallThresholdMs = 1000;

    private readonly object _gate = new();
    private volatile bool _open;
    private int _stalls;
    private int _worstWaitMs;
    private long _openedAtMs;

    public void Observe(string operation, string caller, int wait_ms)
        => Observe(operation, caller, wait_ms, Environment.TickCount64);

    internal void Observe(string operation, string caller, int wait_ms, long nowMs)
    {
        bool late = wait_ms >= StallThresholdMs;
        if (!late && !_open) return;

        lock (_gate)
        {
            if (late)
            {
                _stalls++;
                if (wait_ms > _worstWaitMs) _worstWaitMs = wait_ms;
                if (!_open)
                {
                    _open = true;
                    _openedAtMs = nowMs;
                    DeckleThreadingSource.Log.MarshalStalled(operation, caller, wait_ms, StallThresholdMs);
                }
                else
                {
                    DeckleThreadingSource.Log.MarshalStallRepeated(operation, caller, wait_ms, _stalls);
                }
                return;
            }

            if (!_open) return;
            DeckleThreadingSource.Log.MarshalStallRecovered(_stalls, _worstWaitMs, nowMs - _openedAtMs);
            _open = false;
            _stalls = 0;
            _worstWaitMs = 0;
        }
    }
}
