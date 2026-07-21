using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Home;

[EventSource(Name = "Deckle-Home")]
public sealed class DeckleHomeSource : DeckleEventSource
{
    public static readonly DeckleHomeSource Log = new();

    private DeckleHomeSource() { }

    private const EventKeywords Gesture = (EventKeywords)0x400;

    public const int EvtGestureCompleted = 1;
    public const int EvtSchemaRejected = 2;
    public const int EvtSchemaRejectedDetail = 3;

    [Event(EvtGestureCompleted,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home gesture complete | gesture={0} | ms={1:F1}")]
    public void GestureCompleted(string gesture, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtGestureCompleted, gesture, duration_ms);
    }

    [Event(EvtSchemaRejected,
           Level = EventLevel.Warning,
           Keywords = Gesture,
           Message = "The Home schema is not ready")]
    public void SchemaRejected()
    {
        if (IsEnabled()) WriteEvent(EvtSchemaRejected);
    }

    [Event(EvtSchemaRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home schema rejected | reason={0}")]
    public void SchemaRejectedDetail(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaRejectedDetail, reason);
    }
}
