using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Anytype module provider. HTTP request lifecycle uses the transverse Network
// keyword so an OS-level outage (DeckleNetworkSource) correlates with API
// failures across modules; gesture, session and backend events carry
// module-local keywords.
[EventSource(Name = "Deckle-Anytype")]
public sealed class DeckleAnytypeSource : DeckleEventSource
{
    public static readonly DeckleAnytypeSource Log = new();

    private DeckleAnytypeSource() { }

    // Module-local keyword bits (transverse bits 0..9 reserved in Keywords;
    // 0x400+ belongs to the provider). Gesture covers gesture/session activity
    // that is not raw network traffic; Lifecycle covers the headless backend's
    // supervision, a distinct observable family from the API calls, and carries the
    // transverse Lifecycle bit so a cross-module lifecycle filter finds every line.
    private const EventKeywords Gesture   = (EventKeywords)0x400;
    private const EventKeywords Lifecycle = (EventKeywords)0x800 | (EventKeywords)Keywords.Lifecycle;

    // Retired ids, never to be reused: 15 (BackendTaskRegistered), 17-18
    // (BackendTaskOperationFailed/Detail).

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtApiRequestStarted         = 1;
    public const int EvtApiRequestCompleted       = 2;
    // 3 retired.
    public const int EvtGestureCompleted          = 4;
    public const int EvtSessionReportCreated      = 5;
    public const int EvtSessionStarted            = 6;
    // 7 retired.
    public const int EvtApiRequestRetriedDetail   = 8;
    public const int EvtApiRequestFailedDetail    = 9;
    public const int EvtSpaceWriteContended       = 10;
    public const int EvtBackendStarting               = 11;
    public const int EvtBackendReady                  = 12;
    public const int EvtBackendStartTimedOut          = 13;
    public const int EvtBackendNotProvisioned         = 14;
    // 16 retired.
    public const int EvtCredentialsResolved           = 19;
    public const int EvtBackendProcessAttached        = 20;
    // 21 retired.
    // 22 retired.
    // 23 retired.
    // 24 retired.
    public const int EvtBackendReconciliationStarted  = 25;
    public const int EvtBackendReconciliationLeaseAcquired = 26;
    public const int EvtBackendListenerObserved       = 27;
    public const int EvtBackendReconciliationCompleted = 28;
    public const int EvtBackendReconciliationCancelled = 29;
    // 30 retired.
    public const int EvtBackendEndpointConflictDetail = 31;
    public const int EvtBackendRestartScheduled       = 32;
    // 33 retired.
    // 34 retired.
    public const int EvtApiRequestFailed              = 35;
    public const int EvtBackendHealthProbed           = 36;
    public const int EvtBackendStopped                = 37;
    public const int EvtBackendSpawnFailed            = 38;
    public const int EvtBackendSpawnFailedDetail      = 39;
    public const int EvtBackendEndpointConflict       = 40;
    public const int EvtAnytypeRuntimeFaulted         = 41;
    public const int EvtBackendReconciliationFailed   = 42;
    public const int EvtBackendReconciliationLeaseWaiting  = 43;
    public const int EvtBackendReconciliationLeaseWaitEnded = 44;
    public const int EvtBackendRestartsFailing        = 45;
    public const int EvtBackendRestartAttemptFailed   = 46;
    public const int EvtBackendRecovered              = 47;
    public const int EvtBackendSupervisionStopped     = 48;
    public const int EvtBackendSupervisionGaveUp      = 49;
    public const int EvtBackendSupervisionFailed      = 50;
    public const int EvtBackendUnresponsive           = 51;
    public const int EvtBackendResponsive             = 52;
    public const int EvtBackendInstallationChecked    = 53;
    public const int EvtBackendInstallationPrepareFailed = 54;
    public const int EvtBackendProviderFileOperationFailed = 55;
    public const int EvtBackendProcessQueryFailed     = 56;
    public const int EvtApiTransportRecovered         = 57;
    public const int EvtBackendHealthProbeFailed      = 58;
    public const int EvtDeleteRefused                 = 59;
    public const int EvtObjectBinned                  = 60;
    public const int EvtBackendRestartAttemptStarting = 61;
    public const int EvtBackendRestartAttemptStopped  = 62;
    public const int EvtBackendRestartAttemptTimedOut = 63;
    public const int EvtBackendRestartAttemptSpawnFailed = 64;
    public const int EvtApiRefusalsEnded              = 65;
    public const int EvtApiRequestSuperseded          = 66;

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

    [Event(EvtApiRequestRetriedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request retried | method={0} | path={1} | status={2} | backoff_ms={3:F0}")]
    public void ApiRequestRetriedDetail(string method, string path, int status_code, double backoff_ms)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestRetriedDetail, method, path, status_code, backoff_ms);
    }

    // The cause travels on the milestone because errors.jsonl keeps it and
    // drops the Verbose detail. reason: ApiFailureReasons. A repeating failure
    // writes this line once, when it starts: transport and timeout failures
    // until the backend answers (ApiTransportRecovered), identical refusals
    // until another request ends the run (ApiRefusalsEnded).
    [Event(EvtApiRequestFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "An API request failed | reason={2} | method={0} | status={1} | {3}: {4}")]
    public void ApiRequestFailed(
        string method, int status_code, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestFailed, method, status_code, reason, ex_type, ex_message);
    }

    [Event(EvtApiRequestFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api request failed | method={0} | path={1} | status={2} | error={3}")]
    public void ApiRequestFailedDetail(string method, string path, int status_code, string error)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestFailedDetail, method, path, status_code, error);
    }

    // Closes a run of identical refusals ApiRequestFailed(reason=http_status|
    // untrusted_endpoint) opened. The server's error text stays out of the log
    // (it is free text, and the detail above carries the machine code instead).
    // A run of one is already told by its opening line and writes nothing here.
    [Event(EvtApiRefusalsEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Network,
           Tags = ObservationTags.Journal,
           Message = "A run of refused Anytype API requests ended | reason={0} | status={1} | refused_requests={2} | duration_s={3:F0}")]
    public void ApiRefusalsEnded(string reason, int status_code, int refused_requests, double duration_s)
    {
        if (IsEnabled()) WriteEvent(EvtApiRefusalsEnded, reason, status_code, refused_requests, duration_s);
    }

    // A write refused because the version it carried is stale. The caller re-reads
    // and retries, so this is the protocol working, not a failure.
    [Event(EvtApiRequestSuperseded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "api write superseded | method={0} | path={1} | status={2} | code={3}")]
    public void ApiRequestSuperseded(string method, string path, int status_code, string code)
    {
        if (IsEnabled()) WriteEvent(EvtApiRequestSuperseded, method, path, status_code, code);
    }

    // Closes the transport incident ApiRequestFailed(reason=transport|timeout)
    // opened: the requests in between stayed Verbose details.
    [Event(EvtApiTransportRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Network,
           Tags = ObservationTags.Journal,
           Message = "The Anytype API answers again | failed_requests={0} | downtime_s={1:F0}")]
    public void ApiTransportRecovered(int failed_requests, double downtime_s)
    {
        if (IsEnabled()) WriteEvent(EvtApiTransportRecovered, failed_requests, downtime_s);
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

    // A move to the bin is the one gesture that hides an object from every later
    // read, so both of its ends travel in the journal: the move that did not
    // happen says why, the move that did says what. reason: DeleteRefusalReasons.
    // ex_type and cause tell why the references could not be read and are empty
    // for every other reason; the cause is an error code or a closed word, never
    // the server's text. The object's name stays out: it is the person's content,
    // the id is enough.
    [Event(EvtDeleteRefused,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Tags = ObservationTags.Journal,
           Message = "A move to the Anytype bin was refused | reason={0} | type={1} | object_id={2} | {3}: {4}")]
    public void DeleteRefused(
        string reason, string type_key, string object_id, string ex_type, string cause)
    {
        if (IsEnabled()) WriteEvent(EvtDeleteRefused, reason, type_key, object_id, ex_type, cause);
    }

    [Event(EvtObjectBinned,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Tags = ObservationTags.Journal,
           Message = "An object was moved to the Anytype bin | type={0} | object_id={1}")]
    public void ObjectBinned(string type_key, string object_id)
    {
        if (IsEnabled()) WriteEvent(EvtObjectBinned, type_key, object_id);
    }

    // Two waiters naming the same target are a real concurrent edit, visible by
    // grepping target.
    [Event(EvtSpaceWriteContended,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "space write contended | operation={0} | target={1} | waited_ms={2:F0}")]
    public void SpaceWriteContended(string operation, string target, double waited_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpaceWriteContended, operation, target, waited_ms);
    }

    // ── Backend installation ──────────────────────────────────────────────

    // Which provider version this boot runs against, and what preparation
    // did to get there: none, migrated_legacy, migration_incomplete or absent.
    // An empty active_version means no activated provider. An active version
    // other than the pin is a known state (the legacy copy, a patched build),
    // so the pair is reported here rather than as a Warning every boot.
    [Event(EvtBackendInstallationChecked,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Anytype backend installation checked | action={0} | active={1} | pinned={2}")]
    public void BackendInstallationChecked(string action, string active_version, string pinned_version)
    {
        if (IsEnabled()) WriteEvent(EvtBackendInstallationChecked, action, active_version, pinned_version);
    }

    // reason: exception or legacy_copy_incomplete. The spawn that follows would
    // otherwise be the first sign the provider store is broken.
    [Event(EvtBackendInstallationPrepareFailed,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend installation could not be prepared | reason={0} | {1}: {2}")]
    public void BackendInstallationPrepareFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendInstallationPrepareFailed, reason, ex_type, ex_message);
    }

    // A provider-store file operation failed and the store fell back (no active
    // version, cleanup left behind). operation: read_activation,
    // delete_activation_temp, delete_staging, delete_bundle_zip.
    [Event(EvtBackendProviderFileOperationFailed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend provider file operation failed | operation={0} | ex_type={1} | ex_message={2}")]
    public void BackendProviderFileOperationFailed(string operation, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendProviderFileOperationFailed, operation, ex_type, ex_message);
    }

    // ── Backend lifecycle ─────────────────────────────────────────────────
    //
    // The headless backend is spawned windowless and supervised in-process:
    // readiness through the health endpoint, death through the process handle,
    // restarts on a capped backoff. Milestones read as a narrative of the
    // lifecycle and carry their cause; the Verbose events carry the greppable
    // detail.

    [Event(EvtBackendStarting,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Starting the Anytype backend | work_id={0}")]
    public void BackendStarting(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendStarting, work_id);
    }

    [Event(EvtBackendReady,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend is ready | work_id={0}")]
    public void BackendReady(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendReady, work_id);
    }

    // Warning: the start was requested but the backend never answered in time —
    // a degradation a human would want to notice (the backend will be absent).
    // pid is the live serve the window waited on, 0 when none was seen: a
    // wedged serve outlives every retry, and without its pid the journal read
    // as if nothing were running.
    [Event(EvtBackendStartTimedOut,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend did not become ready | pid={0} | work_id={1}")]
    public void BackendStartTimedOut(int pid, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendStartTimedOut, pid, work_id);
    }

    // Warning: the backend binary is not on disk — provisioning has not run,
    // so Deckle cannot start it. A state the user must act on.
    [Event(EvtBackendNotProvisioned,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend is not provisioned")]
    public void BackendNotProvisioned()
    {
        if (IsEnabled()) WriteEvent(EvtBackendNotProvisioned);
    }

    // reason: ok, http_status, timeout, refused or unreachable. Status 0 marks
    // "no HTTP response"; the reason says why there was none.
    [Event(EvtBackendHealthProbed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend health probed | healthy={0} | status={1} | reason={2} | ms={3:F1}")]
    public void BackendHealthProbed(bool healthy, int status_code, string reason, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtBackendHealthProbed, healthy, status_code, reason, duration_ms);
    }

    // The probe itself threw (not an HTTP failure, which BackendHealthProbed
    // reports): the monitor counts it as a failed probe and keeps going.
    [Event(EvtBackendHealthProbeFailed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend health probe failed | ex_type={0} | ex_message={1}")]
    public void BackendHealthProbeFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendHealthProbeFailed, ex_type, ex_message);
    }

    // The watched serve still holds its process but stopped answering health
    // probes. Opens once per incident; BackendResponsive closes it.
    [Event(EvtBackendUnresponsive,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The Anytype backend stopped answering | reason={1} | failed_probes={0}")]
    public void BackendUnresponsive(int failed_probes, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtBackendUnresponsive, failed_probes, reason);
    }

    [Event(EvtBackendResponsive,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend answers again | failed_probes={0} | downtime_s={1:F0}")]
    public void BackendResponsive(int failed_probes, double downtime_s)
    {
        if (IsEnabled()) WriteEvent(EvtBackendResponsive, failed_probes, downtime_s);
    }

    // Which serve instance the supervisor is watching, and how it got it:
    // "spawned" (started by us, windowless) or "adopted" (found already
    // running at boot). The pid is the join key against Task Manager and the
    // stopped event below.
    [Event(EvtBackendProcessAttached,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend process attached | pid={0} | mode={1}")]
    public void BackendProcessAttached(int pid, string mode)
    {
        if (IsEnabled()) WriteEvent(EvtBackendProcessAttached, pid, mode);
    }

    // Warning: the serve died under supervision — the restart ladder engages,
    // but a human following the flow should see the interruption. exit_known
    // false means the code could not be read (adopted handle without query
    // rights) and exit_code is then meaningless; uptime answers the
    // crash-loop-or-one-off question, and is -1 when the start time was
    // unreadable. work_id pairs the stop with what it ends: the start that
    // wrote BackendStarting, or the restart incident this stop opens.
    // Inside a restart incident the attempts that die write
    // BackendRestartAttemptStopped instead, at Verbose.
    [Event(EvtBackendStopped,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend stopped | exit_code={1} | exit_known={2} | pid={0} | uptime_s={3:F0} | work_id={4}")]
    public void BackendStopped(int pid, int exit_code, bool exit_known, double uptime_s, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendStopped, pid, exit_code, exit_known, uptime_s, work_id);
    }

    // A Process accessor failed. Mostly the expected race of a serve exiting
    // during inspection, kept so an access-denied adoption is not invisible.
    [Event(EvtBackendProcessQueryFailed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend process query failed | pid={0} | operation={1} | ex_type={2} | ex_message={3}")]
    public void BackendProcessQueryFailed(int pid, string operation, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendProcessQueryFailed, pid, operation, ex_type, ex_message);
    }

    // reason: start_returned_null or exception. Inside a restart incident the
    // spawn failures write BackendRestartAttemptSpawnFailed instead, at Verbose.
    [Event(EvtBackendSpawnFailed,
           Level = EventLevel.Error,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend could not be started | reason={0} | {1}: {2}")]
    public void BackendSpawnFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendSpawnFailed, reason, ex_type, ex_message);
    }

    [Event(EvtBackendSpawnFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend spawn failed | executable={0}")]
    public void BackendSpawnFailedDetail(string executable)
    {
        if (IsEnabled()) WriteEvent(EvtBackendSpawnFailedDetail, executable);
    }

    [Event(EvtBackendReconciliationStarted,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend reconciliation started | reconciliation_id={0} | trigger={1}")]
    public void BackendReconciliationStarted(string reconciliation_id, string trigger)
    {
        if (IsEnabled()) WriteEvent(EvtBackendReconciliationStarted, reconciliation_id, trigger);
    }

    [Event(EvtBackendReconciliationLeaseAcquired,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend reconciliation lease acquired | reconciliation_id={0} | waited_ms={1:F1} | abandoned={2}")]
    public void BackendReconciliationLeaseAcquired(
        string reconciliation_id, double waited_ms, bool abandoned)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendReconciliationLeaseAcquired, reconciliation_id, waited_ms, abandoned);
    }

    // The lease wait has no bound by design (a successor must never spawn a
    // second serve), so a stuck holder would otherwise freeze the Anytype
    // runtime at startup without a word. Opens once per wait.
    [Event(EvtBackendReconciliationLeaseWaiting,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "Waiting for another Deckle to release the Anytype backend | reason=lease_held | waited_ms={1:F0} | reconciliation_id={0}")]
    public void BackendReconciliationLeaseWaiting(string reconciliation_id, double waited_ms)
    {
        if (IsEnabled()) WriteEvent(EvtBackendReconciliationLeaseWaiting, reconciliation_id, waited_ms);
    }

    [Event(EvtBackendReconciliationLeaseWaitEnded,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend lease was released | waited_ms={1:F0} | abandoned={2} | reconciliation_id={0}")]
    public void BackendReconciliationLeaseWaitEnded(
        string reconciliation_id, double waited_ms, bool abandoned)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendReconciliationLeaseWaitEnded, reconciliation_id, waited_ms, abandoned);
    }

    [Event(EvtBackendListenerObserved,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend listener observed | reconciliation_id={0} | expected_pid={1} | listener_pid={2} | healthy={3} | executable={4}")]
    public void BackendListenerObserved(
        string reconciliation_id, int expected_pid, int listener_pid,
        bool healthy, string executable)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendListenerObserved, reconciliation_id, expected_pid,
            listener_pid, healthy, executable);
    }

    [Event(EvtBackendReconciliationCompleted,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend reconciliation completed | reconciliation_id={0} | decision={1} | pid={2} | ms={3:F1}")]
    public void BackendReconciliationCompleted(
        string reconciliation_id, string decision, int pid, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendReconciliationCompleted, reconciliation_id, decision, pid, duration_ms);
    }

    [Event(EvtBackendReconciliationCancelled,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend reconciliation cancelled | reconciliation_id={0} | ms={1:F1}")]
    public void BackendReconciliationCancelled(string reconciliation_id, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendReconciliationCancelled, reconciliation_id, duration_ms);
    }

    // The third end of a reconciliation: an exception out of inspection or
    // spawn, which would otherwise leave a Started with no end.
    [Event(EvtBackendReconciliationFailed,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "Anytype backend reconciliation failed | {1}: {2} | reconciliation_id={0} | ms={3:F1}")]
    public void BackendReconciliationFailed(
        string reconciliation_id, string ex_type, string ex_message, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendReconciliationFailed, reconciliation_id, ex_type, ex_message, duration_ms);
    }

    // reason: listener_ambiguous, listener_query_failed, owner_unreadable or
    // owner_untrusted. The free-text detail (path, OS error) stays Verbose.
    [Event(EvtBackendEndpointConflict,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Another process owns the Anytype endpoint | reason={0} | listener_pid={1}")]
    public void BackendEndpointConflict(string reason, int listener_pid)
    {
        if (IsEnabled()) WriteEvent(EvtBackendEndpointConflict, reason, listener_pid);
    }

    [Event(EvtBackendEndpointConflictDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend endpoint conflict | reconciliation_id={0} | listener_pid={1} | detail={2}")]
    public void BackendEndpointConflictDetail(
        string reconciliation_id, int listener_pid, string detail)
    {
        if (IsEnabled()) WriteEvent(
            EvtBackendEndpointConflictDetail, reconciliation_id, listener_pid, detail);
    }

    // ── Backend supervision ───────────────────────────────────────────────
    //
    // One restart incident, one work_id. BackendStopped (or a start that never
    // served, whose id is the start's own) opens it. Every attempt of the
    // ladder stays Verbose: the reconciler writes the BackendRestartAttempt*
    // events below instead of the journal-tagged Starting, Stopped, StartTimedOut
    // and SpawnFailed lines it writes for a first start. BackendRestartsFailing
    // marks a streak past the threshold once, and BackendRecovered, or
    // BackendSupervisionGaveUp when restarting is pointless, closes it.

    // The ladder is about to spawn a serve. Verbose twin of BackendStarting.
    [Event(EvtBackendRestartAttemptStarting,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart attempt starting | work_id={0}")]
    public void BackendRestartAttemptStarting(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartAttemptStarting, work_id);
    }

    // The serve an attempt spawned exited before serving. Verbose twin of
    // BackendStopped, with the same facts.
    [Event(EvtBackendRestartAttemptStopped,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart attempt stopped | exit_code={1} | exit_known={2} | pid={0} | uptime_s={3:F0} | work_id={4}")]
    public void BackendRestartAttemptStopped(int pid, int exit_code, bool exit_known, double uptime_s, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartAttemptStopped, pid, exit_code, exit_known, uptime_s, work_id);
    }

    // An attempt waited its window and the backend never answered. Verbose
    // twin of BackendStartTimedOut.
    [Event(EvtBackendRestartAttemptTimedOut,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart attempt timed out | pid={0} | work_id={1}")]
    public void BackendRestartAttemptTimedOut(int pid, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartAttemptTimedOut, pid, work_id);
    }

    // An attempt could not even start the process. Verbose twin of
    // BackendSpawnFailed; reason is start_returned_null or exception.
    [Event(EvtBackendRestartAttemptSpawnFailed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart attempt could not start | reason={0} | {1}: {2}")]
    public void BackendRestartAttemptSpawnFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartAttemptSpawnFailed, reason, ex_type, ex_message);
    }

    [Event(EvtBackendRestartScheduled,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart scheduled | attempt={0} | backoff_ms={1:F0}")]
    public void BackendRestartScheduled(int attempt, double backoff_ms)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartScheduled, attempt, backoff_ms);
    }

    [Event(EvtBackendRestartAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "backend restart attempt failed | attempt={0} | outcome={1} | ex_type={2} | ex_message={3} | work_id={4}")]
    public void BackendRestartAttemptFailed(int attempt, string outcome, string ex_type, string ex_message, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartAttemptFailed, attempt, outcome, ex_type, ex_message, work_id);
    }

    // last_outcome is the reconciliation decision of the last attempt
    // (timed-out, start-rejected) or "exception" with its type and message.
    [Event(EvtBackendRestartsFailing,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The Anytype backend keeps failing to restart | last_outcome={1} | {2}: {3} | attempts={0} | work_id={4}")]
    public void BackendRestartsFailing(int attempts, string last_outcome, string ex_type, string ex_message, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRestartsFailing, attempts, last_outcome, ex_type, ex_message, work_id);
    }

    // Journal: it closes a BackendStopped the journal already holds, so a
    // reader on another machine does not take a recovered backend for a dead one.
    [Event(EvtBackendRecovered,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype backend is back | attempts={0} | downtime_s={1:F0} | work_id={2}")]
    public void BackendRecovered(int attempts, double downtime_s, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendRecovered, attempts, downtime_s, work_id);
    }

    [Event(EvtBackendSupervisionStopped,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Anytype backend supervision stopped")]
    public void BackendSupervisionStopped()
    {
        if (IsEnabled()) WriteEvent(EvtBackendSupervisionStopped);
    }

    // reason: not_provisioned or endpoint_conflict. From here on nothing will
    // restart the backend before Deckle itself restarts.
    [Event(EvtBackendSupervisionGaveUp,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Anytype backend supervision gave up | reason={0} | work_id={1}")]
    public void BackendSupervisionGaveUp(string reason, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtBackendSupervisionGaveUp, reason, work_id);
    }

    [Event(EvtBackendSupervisionFailed,
           Level = EventLevel.Error,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Anytype backend supervision failed | {0}: {1}")]
    public void BackendSupervisionFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackendSupervisionFailed, ex_type, ex_message);
    }

    // ── Anytype runtime ───────────────────────────────────────────────────

    // phase names where the composition broke (prepare, supervise, credentials,
    // tokens, host, mcp_drain, mcp_dispose, api_dispose, supervisor_dispose),
    // since one resident task runs them all.
    [Event(EvtAnytypeRuntimeFaulted,
           Level = EventLevel.Error,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Anytype runtime failed | phase={0} | {1}: {2}")]
    public void AnytypeRuntimeFaulted(string phase, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAnytypeRuntimeFaulted, phase, ex_type, ex_message);
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
