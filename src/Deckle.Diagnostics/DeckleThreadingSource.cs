using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: significant dispatcher marshalling
// (`DispatcherQueue.TryEnqueue`) to the UI thread. Without this cross-cutting
// event, a UI deadlock or abnormal marshalling latency can at best be guessed
// through indirect correlation with business milestones; a site whose enqueue
// silently fails (queue already closed) also has no systematic trace. The
// primitive is strictly non-business (platform wiring on the
// `Microsoft.UI.Dispatching` side) and consumed by several modules with
// exactly the same parameter set: promotion to cross-cutting sub-provider
// under the two-clause criterion in `reference--eventsource-convention--1.2.md`
// §*Cross-cutting sub-providers*.
//
// Also inherits the historical `DispatcherEnqueueRejected` event migrated from
// `DeckleShellSource`: the event did not describe a shell operation, it
// described a dispatcher rejection cross-cutting every module that marshals to
// the UI thread. Its natural place is here, next to the
// `MarshalQueued` / `MarshalCompleted` trunk.
//
// "Common trunk + specialized events" pattern (see 1.2 §*Convention*).
// `MarshalQueued` + `MarshalCompleted` are the trunk emitted by every
// significant site around a `TryEnqueue`. `MarshalTimeout` is the specialized
// event for the abnormal case where the callback never ran within a bounded
// delay: no site is actively wired in this pass; it is declared to freeze the
// signature before detection is added. `DispatcherEnqueueRejected` is the
// specialized event for the case where `TryEnqueue` returns false (queue shut
// down): the event already existed in legacy under `DeckleShellSource`, simply
// repositioned here.
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
//   "warm-pass-tail"    — reserved legacy operation from the former HUD
//                         composition warm pass. Do not reuse for a different
//                         scheduling pattern.
//
// `caller` convention: short name of the logical site
// ("transcription-engine", "ambient-pipeline", "hue-driver", "hud-window",
// "log-window", "settings-window", etc.). Differentiates two marshallings of
// the same `operation` on distinct sites without inflating the schema.
//
// `queue_depth` convention: approximation of queue depth at `TryEnqueue` time.
// `DispatcherQueue` exposes no public getter; when not measurable, pass `-1`
// as the "unknown" sentinel.
//
// `wait_ms` / `run_ms` convention:
//   - `wait_ms` measures the time between the `TryEnqueue` call (Stopwatch
//     started just before) and callback execution start (Stopwatch read at the
//     beginning of the callback).
//   - `run_ms` measures callback execution time (Stopwatch restarted at the
//     beginning of the callback and read at the end).
[EventSource(Name = "Deckle.Diagnostics.Threading")]
public sealed class DeckleThreadingSource : DeckleEventSource
{
    public static readonly DeckleThreadingSource Log = new();

    private DeckleThreadingSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtMarshalQueued              = 1;
    public const int EvtMarshalCompleted           = 2;
    public const int EvtMarshalTimeout             = 3;
    public const int EvtDispatcherEnqueueRejected  = 4;

    // Trunk: emitted just before `TryEnqueue` on the caller-site side. Verbose
    // because marshalling is frequent by nature (a typical user Stop chains
    // several marshals per engine state) and grep-ability goes through typed
    // parameters rather than level.
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

    // Specialized: abnormal case where the callback never ran within a
    // bounded delay (the marshal remained queued longer than an app threshold).
    // Warning because this is an anomaly that deserves surfacing even when
    // Verbose is not listened to. No active site today; declared to freeze the
    // signature before detection (dedicated timer, per-operation watchdog) is
    // wired.
    [Event(EvtMarshalTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal timeout | operation={0} | caller={1} | waited_ms={2}")]
    public void MarshalTimeout(string operation, string caller, int waited_ms)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalTimeout, operation, caller, waited_ms);
    }

    // Specialized: `TryEnqueue` returned false (queue shut down). Migrated from
    // `DeckleShellSource.DispatcherEnqueueRejected` (event id 15 in legacy
    // Shell). The public signature stays identical to avoid breaking existing
    // callers: `caller_source` is the free label propagated by
    // `DispatcherQueueExtensions.TryEnqueueOrLog` (e.g. "HUD", "LOGWIN"),
    // `reason` describes the cause or context of the lost enqueue (e.g.
    // "queue-rejected", short description of the event being marshalled).
    [Event(EvtDispatcherEnqueueRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "dispatcher enqueue rejected | caller_source={0} | reason={1}")]
    public void DispatcherEnqueueRejected(string caller_source, string reason)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtDispatcherEnqueueRejected, caller_source, reason);
    }
}
