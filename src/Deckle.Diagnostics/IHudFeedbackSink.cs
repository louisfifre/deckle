namespace Deckle.Diagnostics;

// Contract for surfacing UserFeedback emissions on the HUD. A single
// well-known event name — "UserFeedbackEmitted" — drives this sink;
// every Deckle.* provider that wants to flash a message on the HUD
// declares that same event with the canonical signature
// (int severity, string title, string body, int role).
//
// The HudFeedbackEventListener filters on the event name and hands a
// FeedbackEntry to this sink. Severity and Role are integers on the
// wire so EventSource accepts them as primitive parameters — the App
// maps them back to its UserFeedbackSeverity / UserFeedbackRole enums
// when wiring the bridge.
public interface IHudFeedbackSink
{
    void Write(FeedbackEntry entry);
}

// DTO for a single UserFeedback emission. Kept here as a primitive POCO
// so Deckle.Diagnostics stays free of app/HUD references.
public sealed record FeedbackEntry(
    string Title,
    string Body,
    int Severity,
    int Role);
