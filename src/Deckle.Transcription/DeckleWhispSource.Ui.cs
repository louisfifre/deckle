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
           Message = "A message was shown on screen")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── Llm-side observation from TranscriptionEngine ─────────────────────────

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
           Message = "profile_name={0}")]
    public void ManualProfileNotFoundDetail(string profile_name)
    {
        if (IsEnabled()) WriteEvent(EvtManualProfileNotFoundDetail, profile_name);
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    [Event(EvtDisposeStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prev_state={0} | timeout_ms={1}")]
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
           Message = "join_ms={0}")]
    public void DisposeWorkerJoinTimeoutDetail(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoinTimeoutDetail, join_ms);
    }

    [Event(EvtDisposeWorkerJoined,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "join_ms={0}")]
    public void DisposeWorkerJoined(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoined, join_ms);
    }

    [Event(EvtPageInitStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription page is being created")]
    public void PageInitStart()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitStart);
    }

    [Event(EvtPageInitComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription page components are ready")]
    public void PageInitComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitComplete);
    }

    [Event(EvtPageInitFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper page failed to initialize\nex_type={0} | ex_message={1}")]
    public void PageInitFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageInitFailed, ex_type, ex_message);
    }

    [Event(EvtPageLoadedStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription page is loading")]
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
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription page finished loading")]
    public void PageLoadedComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedComplete);
    }

    [Event(EvtPageLoadedFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper page failed to load\nex_type={0} | ex_message={1}")]
    public void PageLoadedFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedFailed, ex_type, ex_message);
    }

    [Event(EvtPageModelScanFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Scanning for models failed\nex_type={0} | ex_message={1}")]
    public void PageModelScanFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageModelScanFailed, ex_type, ex_message);
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

    // The settings store's anomaly callbacks (TranscriptionSettingsService):
    // every module provider carries this pair, same shape, so the store stays
    // provider-agnostic.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // Verbose detail behind PageInitFailed / PageLoadedFailed: a stack trace is
    // multi-line, which a milestone message must not be.
    [Event(EvtPageStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stack_trace={0}")]
    public void PageStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtPageStackTrace, stack_trace);
    }
}
