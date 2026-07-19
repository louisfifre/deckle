using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Summary page ──────────────────────────────────────────────────────────

    [Event(EvtSummaryShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup summary was shown")]
    public void SummaryShown()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShown);
    }

    [Event(EvtSummaryShownDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "summary | success={0} | items={1}")]
    public void SummaryShownDetail(bool success, int items)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShownDetail, success, items);
    }

}
