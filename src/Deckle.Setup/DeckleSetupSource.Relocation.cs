using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Data-root relocation ──────────────────────────────────────────────────

    [Event(EvtRelocateStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Moving the app data folder")]
    public void RelocateStarted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateStarted);
    }

    [Event(EvtRelocateStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate | from={0} | to={1} | bytes={2}")]
    public void RelocateStartedDetail(string from, string to, long bytes)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateStartedDetail, from, to, bytes);
    }

    [Event(EvtRelocateCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app data folder moved")]
    public void RelocateCompleted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCompleted);
    }

    [Event(EvtRelocateCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate done | copied_bytes={0} | files={1} | skipped={2} | duration_ms={3}")]
    public void RelocateCompletedDetail(long copied_bytes, int files, int skipped, long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCompletedDetail, copied_bytes, files, skipped, duration_ms);
    }

    [Event(EvtRelocateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app data move failed")]
    public void RelocateFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateFailed);
    }

    [Event(EvtRelocateFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate failed | step={0} | reason={1}")]
    public void RelocateFailedDetail(string step, string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateFailedDetail, step, reason);
    }
}
