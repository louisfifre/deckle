using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtAmbientMasterForcedOff,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient master toggle forced OFF at boot — explicit user action required to enable")]
    public void AmbientMasterForcedOff()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientMasterForcedOff);
    }

    // The engine's own PipelineStartFailed names the failing stage; this line
    // records the App's answer to it, which the person sees: the switch went
    // back to off and was saved.
    [Event(EvtAmbientStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting failed to start and was switched off | {0}: {1}")]
    public void AmbientStartFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailed, ex_type, ex_message);
    }
}
