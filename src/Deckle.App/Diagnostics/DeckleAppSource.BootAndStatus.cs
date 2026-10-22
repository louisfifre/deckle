using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtPathsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "paths | root={0} | settings={1} | telemetry={2} | models={3} | native={4}")]
    public void PathsDetail(string root, string settings, string telemetry, string models, string native)
    {
        if (IsEnabled()) WriteEvent(EvtPathsDetail, root, settings, telemetry, models, native);
    }

    [Event(EvtStartupMilestones,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "startup milestones | {0}")]
    public void StartupMilestones(string milestones_text)
    {
        if (IsEnabled()) WriteEvent(EvtStartupMilestones, milestones_text);
    }

    // Several per take, so Verbose behind the transcription activity gate.
    // status is the engine's tray text (en-US resource), never dictated text.
    [Event(EvtStatusChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "status changed | status={0} | recording={1}")]
    public void StatusChanged(string status, bool recording)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this, EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            return;
        WriteEvent(EvtStatusChanged, status, recording);
    }

}
