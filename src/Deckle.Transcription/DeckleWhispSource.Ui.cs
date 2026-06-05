using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── UserFeedback ────────────────────────────────────────────────────
    //
    // Canonical channel for user notifications (HUD Replacement / Overlay).
    // Severity 0/1/2 = Info/Warning/Error, role 0/1 = Replacement/Overlay.
    // Filtered by HudFeedbackEventListener.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── Llm-side observation from TranscriptionEngine ─────────────────────────

    [Event(EvtManualProfileNotFound,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "manual profile '{0}' not found in Profiles — transcript pasted without rewriting. Pick an existing profile on the Rewriting page.")]
    public void ManualProfileNotFound(string profile_name)
    {
        if (IsEnabled()) WriteEvent(EvtManualProfileNotFound, profile_name);
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
           Message = "dispose timeout | join_ms={0} — worker still alive, leaking thread (process exiting)")]
    public void DisposeWorkerJoinTimeout(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoinTimeout, join_ms);
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

    [Event(EvtSettingChanged,
           Level = EventLevel.Informational,
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
           Message = "init component threw | error={0}: {1}")]
    public void PageInitFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageInitFailed, ex_type, ex_message);
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
           Message = "loaded threw | error={0}: {1}")]
    public void PageLoadedFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedFailed, ex_type, ex_message);
    }

    [Event(EvtPageModelScanFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model scan failed: {0}")]
    public void PageModelScanFailed(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageModelScanFailed, ex_message);
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

    [Event(EvtPageStackTrace,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void PageStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtPageStackTrace, stack_trace);
    }
}
