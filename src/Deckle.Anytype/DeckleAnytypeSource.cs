using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Anytype module provider. Covers the HTTP transport to the local Anytype REST
// API (AnytypeApiClient) and the project-management gestures layered on top
// (SessionGestures, TaskGestures, ProjectGestures, QueryGestures). HTTP
// request lifecycle uses the transverse Network keyword so an OS-level outage
// (DeckleNetworkSource) correlates with API failures across modules; gesture
// and session events carry a module-local keyword.
[EventSource(Name = "Deckle-Anytype")]
public sealed class DeckleAnytypeSource : DeckleEventSource
{
    public static readonly DeckleAnytypeSource Log = new();

    private DeckleAnytypeSource() { }

    // Module-local keyword bits (transverse bits 0..9 reserved in Keywords;
    // 0x400+ belongs to the provider). Gesture/session activity that is not
    // raw network traffic; Lifecycle covers the headless backend's scheduled-task
    // start/health supervision, a distinct observable family from the API calls.
    private const EventKeywords Gesture   = (EventKeywords)0x400;
    private const EventKeywords Lifecycle = (EventKeywords)0x800;

    // Retired ids, never to be reused: 15 (BackendTaskRegistered), 17-18
    // (BackendTaskOperationFailed/Detail) — the scheduled-task hosting they
    // observed was replaced by in-process supervision (2026-07-02).

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtApiRequestStarted         = 1;
    public const int EvtApiRequestCompleted       = 2;
    public const int EvtApiRequestRetried         = 3;
    public const int EvtGestureCompleted          = 4;
    public const int EvtSessionReportCreated      = 5;
    public const int EvtSessionStarted            = 6;
    public const int EvtApiRequestFailed          = 7;
    // Verbose mirrors added for the Verbose/Info separation (ids 8-9).
    public const int EvtApiRequestRetriedDetail   = 8;
    public const int EvtApiRequestFailedDetail    = 9;
    public const int EvtSpaceWriteContended       = 10;
    // Backend lifecycle (ids 11-24; 15/17/18 retired, see above).
    public const int EvtBackendStarting               = 11;
    public const int EvtBackendReady                  = 12;
    public const int EvtBackendStartTimedOut          = 13;
    public const int EvtBackendNotProvisioned         = 14;
    public const int EvtBackendHealthProbed           = 16;
    public const int EvtCredentialsResolved           = 19;
    public const int EvtBackendProcessAttached        = 20;
    public const int EvtBackendStopped                = 21;
    public const int EvtBackendStoppedDetail          = 22;
    public const int EvtBackendSpawnFailed            = 23;
    public const int EvtBackendSpawnFailedDetail      = 24;

    // ── HTTP transport ──────────────────────────────────────────────────

    [Event(EvtApiRequestStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request | method={0} | path={1}")]
    public void ApiRequestStarted(string method, string path)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestStarted, method, path);
    }

    [Event(EvtApiRequestCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request complete | method={0} | path={1} | status={2} | ms={3:F1}")]
    public void ApiRequestCompleted(string method, string path, int status_code, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestCompleted, method, path, status_code, duration_ms);
    }

    // Warning, not Verbose: a single retry on 429/5xx is a degradation a human
    // would want to notice even though the call recovers on the second attempt
    // (Diagnostics CLAUDE.md calibration rule).
    [Event(EvtApiRequestRetried,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "An API request was retried")]
    public void ApiRequestRetried()
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestRetried);
    }

    [Event(EvtApiRequestRetriedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request retried | method={0} | path={1} | status={2} | backoff_ms={3:F0}")]
    public void ApiRequestRetriedDetail(string method, string path, int status_code, double backoff_ms)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestRetriedDetail, method, path, status_code, backoff_ms);
    }

    [Event(EvtApiRequestFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "An API request failed")]
    public void ApiRequestFailed()
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestFailed);
    }

    [Event(EvtApiRequestFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request failed | method={0} | path={1} | status={2} | error={3}")]
    public void ApiRequestFailedDetail(string method, string path, int status_code, string error)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestFailedDetail, method, path, status_code, error);
    }

    // ── Gestures ────────────────────────────────────────────────────────

    [Event(EvtGestureCompleted,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "gesture complete | gesture={0} | ms={1:F1}")]
    public void GestureCompleted(string gesture, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtGestureCompleted, gesture, duration_ms);
    }

    // Info/Verbose pair: the Capital sentence carries no id (SessionStarted),
    // the Verbose mirror carries the report id. The mirror follows the Info.
    [Event(EvtSessionStarted,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Message = "Session report created")]
    public void SessionStarted()
    {
        if (IsEnabled()) WriteEvent(EvtSessionStarted);
    }

    [Event(EvtSessionReportCreated,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "session report created | report_id={0}")]
    public void SessionReportCreated(string report_id)
    {
        if (IsEnabled()) WriteEvent(EvtSessionReportCreated, report_id);
    }

    // A mutating gesture had to wait for the cross-process write lock: another
    // session held it. Verbose+structured — two waiters naming the same target are
    // a real concurrent edit, visible by grepping target. waited_ms is the time
    // spent blocked before the lock was granted.
    [Event(EvtSpaceWriteContended,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "space write contended | operation={0} | target={1} | waited_ms={2:F0}")]
    public void SpaceWriteContended(string operation, string target, double waited_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpaceWriteContended, operation, target, waited_ms);
    }

    // ── Backend lifecycle ─────────────────────────────────────────────────
    //
    // The headless backend is spawned windowless and supervised in-process:
    // readiness through the health endpoint, death through the process handle,
    // restarts on a capped backoff. Milestones (Info/Warning, no args) read as
    // a narrative of the lifecycle; the Verbose mirrors carry the greppable
    // detail.

    [Event(EvtBackendStarting,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Message = "Starting the Anytype backend")]
    public void BackendStarting()
    {
        if (IsEnabled()) WriteEvent(EvtBackendStarting);
    }

    [Event(EvtBackendReady,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Message = "The Anytype backend is ready")]
    public void BackendReady()
    {
        if (IsEnabled()) WriteEvent(EvtBackendReady);
    }

    // Warning: the start was requested but the backend never answered in time —
    // a degradation a human would want to notice (the backend will be absent).
    [Event(EvtBackendStartTimedOut,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The Anytype backend did not become ready")]
    public void BackendStartTimedOut()
    {
        if (IsEnabled()) WriteEvent(EvtBackendStartTimedOut);
    }

    // Warning: the backend binary is not on disk — provisioning has not run,
    // so Deckle cannot start it. A state the user must act on.
    [Event(EvtBackendNotProvisioned,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The Anytype backend is not provisioned")]
    public void BackendNotProvisioned()
    {
        if (IsEnabled()) WriteEvent(EvtBackendNotProvisioned);
    }

    [Event(EvtBackendHealthProbed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend health probed | healthy={0} | status={1} | ms={2:F1}")]
    public void BackendHealthProbed(bool healthy, int status_code, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtBackendHealthProbed, healthy, status_code, duration_ms);
    }

    // Which serve instance the supervisor is watching, and how it got it:
    // "spawned" (started by us, windowless) or "adopted" (found already
    // running at boot). The pid is the join key against Task Manager and the
    // stopped detail below.
    [Event(EvtBackendProcessAttached,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend process attached | pid={0} | mode={1}")]
    public void BackendProcessAttached(int pid, string mode)
    {
        if (IsEnabled()) WriteEvent(EvtBackendProcessAttached, pid, mode);
    }

    // Warning: the serve died under supervision — the restart ladder engages,
    // but a human following the flow should see the interruption.
    [Event(EvtBackendStopped,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The Anytype backend stopped")]
    public void BackendStopped()
    {
        if (IsEnabled()) WriteEvent(EvtBackendStopped);
    }

    // exit_code -1 means the code could not be read (adopted handle without
    // query rights); uptime answers the crash-loop-or-one-off question.
    [Event(EvtBackendStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend stopped | pid={0} | exit_code={1} | uptime_s={2:F0}")]
    public void BackendStoppedDetail(int pid, int exit_code, double uptime_s)
    {
        if (IsEnabled()) WriteEvent(EvtBackendStoppedDetail, pid, exit_code, uptime_s);
    }

    [Event(EvtBackendSpawnFailed,
           Level = EventLevel.Error,
           Keywords = Lifecycle,
           Message = "The Anytype backend could not be started")]
    public void BackendSpawnFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBackendSpawnFailed);
    }

    [Event(EvtBackendSpawnFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend spawn failed | error={0}")]
    public void BackendSpawnFailedDetail(string error)
    {
        if (IsEnabled()) WriteEvent(EvtBackendSpawnFailedDetail, error);
    }

    // Which provisioning world the credentials resolved to: "headless" (the
    // vault holds the bot API key → the fixed 31012 listener) or "desktop"
    // (legacy file bearer → the Desktop pairing). The first question to answer
    // when a host talks to the wrong backend; carries no key material.
    [Event(EvtCredentialsResolved,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "credentials resolved | profile={0}")]
    public void CredentialsResolved(string profile)
    {
        if (IsEnabled()) WriteEvent(EvtCredentialsResolved, profile);
    }
}
