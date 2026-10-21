using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Home;

// Home module provider. Nothing read from the Home space ever reaches a
// payload: schema events carry canonical keys and bounded reasons only.
[EventSource(Name = "Deckle-Home")]
public sealed class DeckleHomeSource : DeckleEventSource
{
    public static readonly DeckleHomeSource Log = new();

    private DeckleHomeSource() { }

    private const EventKeywords Gesture = (EventKeywords)0x400;

    public const int EvtGestureCompleted = 1;
    // 2 retired.
    public const int EvtSchemaRejectedDetail = 3;
    public const int EvtSchemaUnavailable = 4;
    public const int EvtSchemaAttemptFailed = 5;
    public const int EvtSchemaReady = 6;

    [Event(EvtGestureCompleted,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home gesture complete | gesture={0} | ms={1:F1}")]
    public void GestureCompleted(string gesture, double duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtGestureCompleted, gesture, duration_ms);
    }

    // Opens the incident on the first failed schema read; every Home gesture
    // waits on that read, so a streak of failures is one Warning, not one per
    // gesture. reason: a schema reason (schema_nonconformant,
    // zone_layout_invalid, zone_type_ambiguous), http_status, transport or
    // exception. No ex_message: an HTTP error message quotes the server's
    // reply, and a schema message goes to the Verbose detail below.
    [Event(EvtSchemaUnavailable,
           Level = EventLevel.Warning,
           Keywords = Gesture,
           Message = "The Home schema is not ready | reason={0} | ex_type={1} | status={2}")]
    public void SchemaUnavailable(string reason, string ex_type, int status_code)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaUnavailable, reason, ex_type, status_code);
    }

    [Event(EvtSchemaAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home schema read failed | reason={0} | ex_type={1} | status={2} | failures={3}")]
    public void SchemaAttemptFailed(string reason, string ex_type, int status_code, int failures)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaAttemptFailed, reason, ex_type, status_code, failures);
    }

    // The schema message names canonical keys only, never space content.
    [Event(EvtSchemaRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home schema rejected | reason={0}")]
    public void SchemaRejectedDetail(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaRejectedDetail, reason);
    }

    // Once per runtime: the schema was read and validated. after_failures > 0
    // closes the incident SchemaUnavailable opened.
    [Event(EvtSchemaReady,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Message = "The Home schema is ready | after_failures={0} | elapsed_ms={1:F0}")]
    public void SchemaReady(int after_failures, double elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaReady, after_failures, elapsed_ms);
    }
}
