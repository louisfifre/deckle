using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── UserFeedback ────────────────────────────────────────────────────
    //
    // Canonical channel for user notifications (HUD Replacement / Overlay).
    // Severity 0/1/2 = Info/Warning/Error, role 0/1 = Replacement/Overlay.
    // Filtered by HudFeedbackSink.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── Llm-side observation from TranscriptionEngine ─────────────────────────

    // User-facing guidance kept as the milestone; the missing profile name
    // moves to the Verbose mirror.
    [Event(EvtManualProfileNotFound,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The selected rewriting profile was not found, so the transcript was pasted without rewriting. Pick an existing profile on the Rewriting page.")]
    public void ManualProfileNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtManualProfileNotFound);
    }

    [Event(EvtManualProfileNotFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "manual profile not found | profile_name={0}")]
    public void ManualProfileNotFoundDetail(string profile_name)
    {
        if (IsEnabled()) WriteEvent(EvtManualProfileNotFoundDetail, profile_name);
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    [Event(EvtDisposeStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose | waiting on worker | prev_state={0} | timeout_ms={1}")]
    public void DisposeStart(string prev_state, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeStart, prev_state, timeout_ms);
    }

    [Event(EvtDisposeWorkerJoinTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The worker did not stop in time and was left running as the process exits")]
    public void DisposeWorkerJoinTimeout()
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoinTimeout);
    }

    [Event(EvtDisposeWorkerJoinTimeoutDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose timeout | join_ms={0} | state=worker_alive_thread_leaked")]
    public void DisposeWorkerJoinTimeoutDetail(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoinTimeoutDetail, join_ms);
    }

    [Event(EvtDisposeWorkerJoined,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose | worker joined | join_ms={0}")]
    public void DisposeWorkerJoined(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoined, join_ms);
    }

    // ── Settings persistence (transitional: see DeckleAudioSource) ──────

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // ── TranscriptionSettings persistence ─────────────────────────────────
    // Same exception as DeckleAudioSource for JsonSettingsStore delegates: the
    // exact operation in progress is not known at the call site. Preserved
    // separately from SettingsLoaded* to keep a dedicated event for messages
    // already prefixed on the module side.

    [Event(EvtWhispSettingsPrefixed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void WhispSettingsPrefixed(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhispSettingsPrefixed, message);
    }

    // ── ViewModel + WhisperPage UI side ─────────────────────────────────

    // Demoted to Verbose: a settings field mutating from the UI is an opaque
    // internal step with no user-facing milestone value; the line is property +
    // value detail, which belongs at Verbose. No milestone, no mirror.
    [Event(EvtSettingChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} ← {1}")]
    public void SettingChanged(string property, string value)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChanged, property, value);
    }

    [Event(EvtPageInitStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ctor start")]
    public void PageInitStart()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitStart);
    }

    [Event(EvtPageInitComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "init component complete")]
    public void PageInitComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitComplete);
    }

    [Event(EvtPageBuggedSliderSet,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bugged slider min/max set in code-behind")]
    public void PageBuggedSliderSet()
    {
        if (IsEnabled()) WriteEvent(EvtPageBuggedSliderSet);
    }

    [Event(EvtPageInitFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper page failed to initialize")]
    public void PageInitFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitFailed);
    }

    [Event(EvtPageInitFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "init component threw | ex_type={0} | ex_message={1}")]
    public void PageInitFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageInitFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPageLoadedStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded fired")]
    public void PageLoadedStart()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedStart);
    }

    [Event(EvtPageReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Whisper page ready")]
    public void PageReady()
    {
        if (IsEnabled()) WriteEvent(EvtPageReady);
    }

    [Event(EvtPageLoadedComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded complete | state=page-ready")]
    public void PageLoadedComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedComplete);
    }

    [Event(EvtPageLoadedFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper page failed to load")]
    public void PageLoadedFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedFailed);
    }

    [Event(EvtPageLoadedFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded threw | ex_type={0} | ex_message={1}")]
    public void PageLoadedFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPageModelScanFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Scanning for models failed")]
    public void PageModelScanFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageModelScanFailed);
    }

    [Event(EvtPageModelScanFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model scan failed | ex_message={0}")]
    public void PageModelScanFailedDetail(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageModelScanFailedDetail, ex_message);
    }

    [Event(EvtPageDiscardRestartChanges,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Discard restart-requiring changes")]
    public void PageDiscardRestartChanges()
    {
        if (IsEnabled()) WriteEvent(EvtPageDiscardRestartChanges);
    }

    [Event(EvtPageResetAll,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Reset all Whisper settings to defaults")]
    public void PageResetAll()
    {
        if (IsEnabled()) WriteEvent(EvtPageResetAll);
    }

    // Demoted to Verbose: a raw stack trace is pure technical detail attached to
    // PageInitFailed / PageLoadedFailed, not a user-facing milestone. It carries
    // multi-line content, which an Error-level message must not; at Verbose it is
    // the greppable detail behind the Capital failure milestone.
    [Event(EvtPageStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void PageStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtPageStackTrace, stack_trace);
    }
}
