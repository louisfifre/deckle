using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── UserFeedback (HUD bridge) ───────────────────────────────────────
    // Canonical event consumed by HudFeedbackSink (filter on
    // event name). severity 0/1/2 = Info/Warning/Error,
    // role 0/1 = Replacement/Overlay.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── LlmPage UI failures ─────────────────────────────────────────────

    [Event(EvtPageNavigatedToFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The LLM settings page failed to open")]
    public void PageNavigatedToFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailed);
    }

    [Event(EvtPageNavigatedToFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page navigated-to failed | error={0} | message={1}")]
    public void PageNavigatedToFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailedDetail, ex_type, message);
    }

    [Event(EvtEndpointRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Endpoint refresh failed")]
    public void EndpointRefreshFailed()
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailed);
    }

    [Event(EvtEndpointRefreshFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "endpoint refresh failed | error={0} | message={1}")]
    public void EndpointRefreshFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailedDetail, ex_type, message);
    }

    [Event(EvtManualRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Manual refresh failed")]
    public void ManualRefreshFailed()
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailed);
    }

    [Event(EvtManualRefreshFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "manual refresh failed | error={0} | message={1}")]
    public void ManualRefreshFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailedDetail, ex_type, message);
    }

    [Event(EvtOllamaRefreshSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama refresh was skipped")]
    public void OllamaRefreshSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkipped);
    }

    [Event(EvtOllamaRefreshSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ollama refresh skipped | error={0} | message={1}")]
    public void OllamaRefreshSkippedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkippedDetail, ex_type, message);
    }

    [Event(EvtResetAllFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Resetting the LLM settings failed")]
    public void ResetAllFailed()
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailed);
    }

    [Event(EvtResetAllFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reset all failed | error={0} | message={1}")]
    public void ResetAllFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailedDetail, ex_type, message);
    }

}
