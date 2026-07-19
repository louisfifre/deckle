using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── HUD / LogWindow surfaces (host-owned) ───────────────────────────

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The HUD reported a warning")]
    public void HudWarning()
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning);
    }

    [Event(EvtHudWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hud warning | message={0}")]
    public void HudWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarningDetail, message);
    }

    [Event(EvtLogWindowWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The log window reported a warning")]
    public void LogWindowWarning()
    {
        if (IsEnabled()) WriteEvent(EvtLogWindowWarning);
    }

    [Event(EvtLogWindowWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "log window warning | message={0}")]
    public void LogWindowWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtLogWindowWarningDetail, message);
    }

    // ── UserFeedback (HUD bridge) ───────────────────────────────────────
    // Canonical channel for user notifications emitted from the host app.
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
}
