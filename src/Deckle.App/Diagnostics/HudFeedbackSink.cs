using Deckle.Diagnostics;

namespace Deckle.App;

// Flashes the canonical UserFeedback emission on the HUD. A passive ILogSink,
// never its own EventListener: the dispatcher stays the sole listener. It
// lives in the host, beside AppHudFeedbackSink, its only receiver: only the
// app knows there is a HUD, so Deckle.Diagnostics names none.
//
// A single well-known event name — "UserFeedbackEmitted" — drives this sink;
// any provider that wants to flash a message on the HUD declares that event
// with the canonical signature.
//
// Signature contract on each provider:
//
//     [Event(<id>, Level = EventLevel.Informational,
//            Keywords = (EventKeywords)…,
//            Message = "A message was shown on screen")]
//     public void UserFeedbackEmitted(int severity, string title, string body, int role)
//     {
//         if (IsEnabled()) WriteEvent(<id>, severity, title, body, role);
//     }
//
// The payload is keyed by the [Event] parameter names, so the fields are read
// by name, not by positional index, and the Informational template stays a
// sentence that shows neither title nor body. Severity / Role pass as int because
// EventSource cannot serialise arbitrary user enums; the host reads them as
// severity 0/1/2 = Info/Warning/Error and role 0/1 = Replacement/Overlay.
//
// A producer that wants the HUD to react emits UserFeedbackEmitted in addition
// to its milestone; this sink ignores every other event.
internal sealed class HudFeedbackSink : ILogSink
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
        // The payload reads below fall back to defaults, so a malformed
        // emission cannot throw here. A receiver that does throw reaches the
        // dispatcher, which contains it and reports this sink as an incident.
        int severity = entry.Payload.TryGetValue("severity", out var s) && s is int si ? si : 0;
        string title = entry.Payload.TryGetValue("title", out var t) ? t as string ?? string.Empty : string.Empty;
        string body  = entry.Payload.TryGetValue("body",  out var b) ? b as string ?? string.Empty : string.Empty;
        int role     = entry.Payload.TryGetValue("role",  out var r) && r is int ri ? ri : 0;

        _sink.Write(new FeedbackEntry(title, body, severity, role));
    }
}
