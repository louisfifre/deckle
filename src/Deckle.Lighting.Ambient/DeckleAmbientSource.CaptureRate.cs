using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // The capture rate a run or a preview scan asked for, logged once at its
    // capture start. It is decided in code, so this line is the only place a
    // person reading the log learns what the rate was and why: display_hz =
    // the refresh rate Windows announced for the captured display (0 when it
    // announced none, the rate then falls back to the performance ceiling),
    // cap_hz = the tier ceiling, performance_mode = on | off, target_hz =
    // min(display_hz, cap_hz) = the rate the capture was asked for. work_id
    // is the run's or the scan's.
    [Event(EvtCaptureRateChosen,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient capture rate chosen")]
    public void CaptureRateChosen(
        string work_id, int display_hz, int cap_hz, string performance_mode, int target_hz)
    {
        if (IsEnabled())
            WriteEvent(EvtCaptureRateChosen, work_id, display_hz, cap_hz, performance_mode, target_hz);
    }
}
