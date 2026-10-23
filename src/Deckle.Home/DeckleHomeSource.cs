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
    public const int EvtGesturePartiallyApplied = 7;
    public const int EvtDeleteRefused = 8;
    public const int EvtObjectBinned = 9;

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

    // A batch create or update failed after some of its writes went through.
    // Counts and the error type only: the item names are Home content.
    [Event(EvtGesturePartiallyApplied,
           Level = EventLevel.Warning,
           Keywords = Gesture,
           Message = "A Home batch was only partly applied | gesture={0} | applied={1} | not_attempted={2} | ex_type={3}")]
    public void GesturePartiallyApplied(string gesture, int applied, int not_attempted, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtGesturePartiallyApplied, gesture, applied, not_attempted, ex_type);
    }

    // A move to the bin that was asked for and did not happen. reason:
    // HomeDeleteRefusalReasons. The type key only: neither the name nor the id
    // of a Home object travels in a payload.
    [Event(EvtDeleteRefused,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Tags = ObservationTags.Journal,
           Message = "A Home object was not moved to the bin | reason={0} | type_key={1}")]
    public void DeleteRefused(string reason, string type_key)
    {
        if (IsEnabled()) WriteEvent(EvtDeleteRefused, reason, type_key);
    }

    // The move that did happen: the object no longer shows in any later read,
    // so the journal says it went, though not which object.
    [Event(EvtObjectBinned,
           Level = EventLevel.Informational,
           Keywords = Gesture,
           Tags = ObservationTags.Journal,
           Message = "A Home object was moved to the bin | type_key={0}")]
    public void ObjectBinned(string type_key)
    {
        if (IsEnabled()) WriteEvent(EvtObjectBinned, type_key);
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
