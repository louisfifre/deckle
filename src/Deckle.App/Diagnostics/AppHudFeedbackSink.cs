using Deckle.Diagnostics;

namespace Deckle.App;

// Concrete host sink that receives each `FeedbackEntry` captured by the
// `HudFeedbackEventListener` (a `UserFeedbackEmitted` event emitted by a
// `Deckle.*` provider) and routes it to the right HUD surface by `Role`:
//
//   role 0 (Replacement) → surface principale `HudWindow.ShowUserFeedback`
//                          (the chrono is swapped out for the message).
//   role 1 (Overlay)     → stacked card via `HudOverlayManager.Enqueue`
//                          (above / below the main HUD without interrupting
//                          the current workflow).
//
// Marshalling to the UI thread is owned by each target method
// (`HudWindow.ShowUserFeedback` / `HudOverlayManager.Enqueue` do their own
// `EnqueueUI` / `_dispatcher.TryEnqueueOrLog`). The sink is called on the
// EventListener thread, which can be any business thread; EventSource does not
// serialize it.
//
// The sink consumes the primitive `FeedbackEntry` fields directly; no HUD
// module dependency flows back into Deckle.Diagnostics.
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
