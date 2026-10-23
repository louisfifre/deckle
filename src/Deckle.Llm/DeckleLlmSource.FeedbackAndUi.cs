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
    //
    // Each line carries its exception in its own payload: these fire rarely
    // and a log that keeps only Warnings and Errors must still say why.

    [Event(EvtPageNavigatedToFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The LLM settings page failed to open | ex_type={0} | ex_message={1}")]
    public void PageNavigatedToFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailed, ex_type, ex_message);
    }

    [Event(EvtEndpointRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Endpoint refresh failed | ex_type={0} | ex_message={1}")]
    public void EndpointRefreshFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailed, ex_type, ex_message);
    }

    [Event(EvtManualRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Manual refresh failed | ex_type={0} | ex_message={1}")]
    public void ManualRefreshFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailed, ex_type, ex_message);
    }

    [Event(EvtOllamaRefreshSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama refresh was skipped | ex_type={0} | ex_message={1}")]
    public void OllamaRefreshSkipped(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkipped, ex_type, ex_message);
    }

    [Event(EvtResetAllFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Resetting the LLM settings failed | ex_type={0} | ex_message={1}")]
    public void ResetAllFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailed, ex_type, ex_message);
    }

}
