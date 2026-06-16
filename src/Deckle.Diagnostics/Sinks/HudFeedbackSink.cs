namespace Deckle.Diagnostics;

// Flashes the canonical UserFeedback emission on the HUD. A passive ILogSink:
// the single DispatchEventListener builds the EventEntry, then offers it here.
// Before the dispatch refonte this was its own EventListener subscribing to the
// whole Deckle-* family just to watch one event name; folding it into a sink
// removes that extra subscription and leaves the dispatcher as the sole
// listener.
//
// A single well-known event name — "UserFeedbackEmitted" — drives this sink;
// any provider that wants to flash a message on the HUD declares that event
// with the canonical signature.
//
// Signature contract on each provider:
//
//     [Event(<id>, Level = EventLevel.Informational,
//            Keywords = (EventKeywords)…,
//            Message = "{1}: {2}")]
//     public void UserFeedbackEmitted(int severity, string title, string body, int role)
//     {
//         if (IsEnabled()) WriteEvent(<id>, severity, title, body, role);
//     }
//
// The payload reaches this sink as a name-keyed dictionary on the EventEntry
// (the dispatcher builds it from the [Event] parameter names), so the fields
// are read by their snake_case keys — severity / title / body / role — not by
// positional index. Severity / Role pass as int because EventSource cannot
// serialise arbitrary user enums; the host app maps the integers back to its
// UserFeedback enums (severity 0/1/2 = Info/Warning/Error, role 0/1 =
// Replacement/Overlay).
//
// Events with any other name are ignored (Wants returns false) — this sink
// never sees a milestone ("RecordingStarted", etc.) because the application
// code emits UserFeedbackEmitted *in addition* to its milestone when it wants
// the HUD to react.
public sealed class HudFeedbackSink : ILogSink
{
    public const string CanonicalEventName = "UserFeedbackEmitted";

    private readonly IHudFeedbackSink _sink;

    public HudFeedbackSink(IHudFeedbackSink sink)
    {
        _sink = sink;
    }

    public bool Wants(EventEntry entry) =>
        string.Equals(entry.EventName, CanonicalEventName, System.StringComparison.Ordinal);

    public void Write(EventEntry entry)
    {
        try
        {
            int severity = entry.Payload.TryGetValue("severity", out var s) && s is int si ? si : 0;
            string title = entry.Payload.TryGetValue("title", out var t) ? t as string ?? string.Empty : string.Empty;
            string body  = entry.Payload.TryGetValue("body",  out var b) ? b as string ?? string.Empty : string.Empty;
            int role     = entry.Payload.TryGetValue("role",  out var r) && r is int ri ? ri : 0;

            _sink.Write(new FeedbackEntry(title, body, severity, role));
        }
        catch
        {
            // A malformed feedback emission must not crash the dispatcher.
        }
    }
}
