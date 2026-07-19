using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Boot ────────────────────────────────────────────────────────────

    [Event(EvtPathsInitialized,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Paths initialized")]
    public void PathsInitialized()
    {
        if (IsEnabled()) WriteEvent(EvtPathsInitialized);
    }

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

    // ── Status ──────────────────────────────────────────────────────────
    //
    // StatusChanged stays on the App provider: LogWindow displays it under
    // [APP], consistent with the one-provider-per-module doctrine.

    [Event(EvtStatusChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Status changed")]
    public void StatusChanged()
    {
        if (IsEnabled()) WriteEvent(EvtStatusChanged);
    }

    [Event(EvtStatusChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "status changed | status={0}")]
    public void StatusChangedDetail(string status)
    {
        if (IsEnabled()) WriteEvent(EvtStatusChangedDetail, status);
    }

}
