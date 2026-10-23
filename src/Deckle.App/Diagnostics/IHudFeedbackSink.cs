namespace Deckle.App;

// Not HudFeedbackSink's interface: the host-side receiver HudFeedbackSink
// forwards each FeedbackEntry to. AppHudFeedbackSink is the one that ships; a
// test puts its own receiver here to make the HUD fail on demand.
internal interface IHudFeedbackSink
{
    void Write(FeedbackEntry entry);
}

// The fields HudFeedbackSink reads from the UserFeedbackEmitted payload.
internal sealed record FeedbackEntry(
    string Title,
    string Body,
    int Severity,
    int Role);
