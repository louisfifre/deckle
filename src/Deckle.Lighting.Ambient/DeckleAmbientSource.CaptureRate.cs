using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // The capture rate a run or a preview scan asked for, logged once at its
    // capture start: target_hz = the rate the capture was asked for. work_id
    // is the run's or the scan's.
    [Event(EvtCaptureRateChosen,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient capture rate chosen")]
    public void CaptureRateChosen(string work_id, int target_hz)
    {
        if (IsEnabled())
            WriteEvent(EvtCaptureRateChosen, work_id, target_hz);
    }
}
