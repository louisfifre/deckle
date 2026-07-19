using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Ambient observer ────────────────────────────────────────────────

    [Event(EvtAmbientPipelineState,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient pipeline state changed")]
    public void AmbientPipelineState()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPipelineState);
    }

    [Event(EvtAmbientPipelineStateDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient pipeline state | state={0}")]
    public void AmbientPipelineStateDetail(string state)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPipelineStateDetail, state);
    }

    [Event(EvtAmbientMasterForcedOff,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient master toggle forced OFF at boot — explicit user action required to enable")]
    public void AmbientMasterForcedOff()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientMasterForcedOff);
    }

    [Event(EvtAmbientStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting could not start")]
    public void AmbientStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailed);
    }

    [Event(EvtAmbientStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient start failed | error={0} | message={1}")]
    public void AmbientStartFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientStartFailedDetail, ex_type, ex_message);
    }

}
