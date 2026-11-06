using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // The resident Anytype runtime is gone without a failure. outcome:
    // cancelled (reason shutdown). A runtime that
    // stopped on a failure says so at Error level, in AnytypeRuntimeFailed.
    // ex_type and ex_message are empty when the reason alone is the cause.
    [Event(EvtAnytypeRuntimeEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Anytype runtime ended")]
    public void AnytypeRuntimeEnded(string outcome, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAnytypeRuntimeEnded, outcome, reason, ex_type, ex_message);
    }

    // The resident Anytype runtime stopped on a failure, so the Anytype tools
    // are gone until the next run. reason: endpoint_conflict or
    // supervisor_faulted; ex_type and ex_message are empty when the
    // reason alone is the cause. A composition exception is not reported here:
    // DeckleAnytypeSource.AnytypeRuntimeFaulted already says it at Error, with
    // its phase.
    [Event(EvtAnytypeRuntimeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Anytype runtime failed, so the Anytype tools are unavailable until Deckle restarts\nreason={0} | ex_type={1} | ex_message={2}")]
    public void AnytypeRuntimeFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAnytypeRuntimeFailed, reason, ex_type, ex_message);
    }
}
