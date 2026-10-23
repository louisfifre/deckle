using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // Work that does not happen says so, with its reason, tagged like a start:
    // the saved "on" of the master switch is not honoured at boot. reason:
    // boot_policy (the app never starts screen capture and Hue traffic on its
    // own; explicit user action is required to enable it).
    [Event(EvtAmbientMasterForcedOff,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient master toggle forced OFF at boot")]
    public void AmbientMasterForcedOff(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientMasterForcedOff, reason);
    }

    // The engine's own PipelineStartFailed names the failing stage; this line
    // records the App's answer to it, which the person sees: the switch went
    // back to off and was saved. The lighting did not start, so this is an Error.
    [Event(EvtAmbientStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting failed to start and was switched off\nex_type={0} | ex_message={1}")]
    public void AmbientStartFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailed, ex_type, ex_message);
    }
}
