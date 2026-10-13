namespace Deckle.Diagnostics;

// Not HudFeedbackSink's interface: the host-side receiver HudFeedbackSink
// forwards each FeedbackEntry to.
public interface IHudFeedbackSink
{
    void Write(FeedbackEntry entry);
}

// Primitive fields so Deckle.Diagnostics stays free of app/HUD references.
public sealed record FeedbackEntry(
    string Title,
    string Body,
    int Severity,
    int Role);
