using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics.Listeners;

// Listens for the canonical UserFeedback emission across every
// Deckle.* provider. A single well-known event name —
// "UserFeedbackEmitted" — drives this listener; any provider that
// wants to flash a message on the HUD declares that event with the
// canonical signature.
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
// Severity / Role pass as int because EventSource cannot serialise
// arbitrary user enums; the host app maps the integers back to its
// UserFeedback enums (severity 0/1/2 = Info/Warning/Error,
// role 0/1 = Replacement/Overlay).
//
// Events with any other name are ignored — this listener never sees a
// jalon ("RecordingStarted", etc.) because the application code emits
// UserFeedbackEmitted *in addition* to its milestone when it wants the
// HUD to react.
public sealed class HudFeedbackEventListener : EventListener
{
    public const string CanonicalEventName = "UserFeedbackEmitted";

    private readonly IHudFeedbackSink _sink;
    private readonly System.Collections.Generic.List<EventSource> _earlySources = new();
    private bool _ready;

    public HudFeedbackEventListener(IHudFeedbackSink sink)
    {
        _sink = sink;
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle.", System.StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (!string.Equals(eventData.EventName, CanonicalEventName, System.StringComparison.Ordinal)) return;
        if (eventData.Payload is null || eventData.Payload.Count < 4) return;

        try
        {
            int severity = (eventData.Payload[0] is int s0) ? s0 : 0;
            string title = (eventData.Payload[1] as string) ?? string.Empty;
            string body  = (eventData.Payload[2] as string) ?? string.Empty;
            int role     = (eventData.Payload[3] is int r0) ? r0 : 0;

            _sink.Write(new FeedbackEntry(title, body, severity, role));
        }
        catch
        {
            // A malformed feedback emission must not crash the pipeline.
        }
    }
}
