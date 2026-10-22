using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // The resident Anytype runtime is gone. outcome: cancelled (reason
    // shutdown), skipped (not_provisioned) or failed (endpoint_conflict,
    // host_start_failed, exception, supervisor_faulted). ex_type and ex_message are empty when
    // the reason alone is the cause.
    [Event(EvtAnytypeRuntimeEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Anytype runtime ended | outcome={0} | reason={1} | {2}: {3}")]
    public void AnytypeRuntimeEnded(string outcome, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAnytypeRuntimeEnded, outcome, reason, ex_type, ex_message);
    }

    // reason: timeout (requests outlived the drain budget) or exception.
    // The host and the API client then stay alive until the process exits.
    [Event(EvtAnytypeHostDrainIncomplete,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Anytype MCP host did not drain and stays alive until exit | reason={0} | elapsed_ms={1}")]
    public void AnytypeHostDrainIncomplete(string reason, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAnytypeHostDrainIncomplete, reason, elapsed_ms);
    }
}
