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
    // exception. work_id names the incident; SchemaReady closes it with the
    // same id. No ex_message: an HTTP error message quotes the server's reply,
    // and a schema message lists what the space holds, so a provider that reads
    // Home logs the error type and its code, nothing else.
    [Event(EvtSchemaUnavailable,
           Level = EventLevel.Warning,
           Keywords = Gesture,
           Message = "The Home schema is not ready | work_id={0} | reason={1} | ex_type={2} | status={3}")]
    public void SchemaUnavailable(string work_id, string reason, string ex_type, int status_code)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaUnavailable, work_id, reason, ex_type, status_code);
    }

    [Event(EvtSchemaAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home schema read failed | work_id={0} | reason={1} | ex_type={2} | status={3} | failures={4}")]
    public void SchemaAttemptFailed(string work_id, string reason, string ex_type, int status_code, int failures)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaAttemptFailed, work_id, reason, ex_type, status_code, failures);
    }

    // How far the space is from the schema, as a count: the number of checks
    // that failed. Which ones is what the person reads in the tool error.
    [Event(EvtSchemaRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = Gesture,
           Message = "home schema rejected | reason={0} | failed_checks={1}")]
    public void SchemaRejectedDetail(string reason, int failed_checks)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaRejectedDetail, reason, failed_checks);
    }

    // Once per runtime: the schema was read and validated. after_failures > 0
    // closes the incident SchemaUnavailable opened, with its work_id; a first
    // read that succeeds has no incident and an empty work_id.
    [Event(EvtSchemaReady,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Tags = ObservationTags.Journal,
           Message = "The Home schema is ready | work_id={0} | after_failures={1} | elapsed_ms={2:F0}")]
    public void SchemaReady(string work_id, int after_failures, double elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSchemaReady, work_id, after_failures, elapsed_ms);
    }
}
