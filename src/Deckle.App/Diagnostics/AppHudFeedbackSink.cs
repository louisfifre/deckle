using Deckle.Diagnostics;

namespace Deckle.App;

// Routes each FeedbackEntry by Role: 1 (Overlay) to a stacked card that leaves
// the main HUD alone, anything else (Replacement) to the main HUD surface.
// Called on the EventListener thread, which can be any business thread; each
// target marshals to the UI thread itself.
internal sealed class AppHudFeedbackSink : IHudFeedbackSink
{
    private readonly System.Action<int, string, string> _onReplacement;
    private readonly System.Action<int, string, string> _onOverlay;

    public AppHudFeedbackSink(
        System.Action<int, string, string> onReplacement,
        System.Action<int, string, string> onOverlay)
    {
        _onReplacement = onReplacement;
        _onOverlay = onOverlay;
    }

    public void Write(FeedbackEntry entry)
    {
        switch (entry.Role)
        {
            case 1: _onOverlay(entry.Severity, entry.Title, entry.Body); break;
            default: _onReplacement(entry.Severity, entry.Title, entry.Body); break;
        }
    }
}
