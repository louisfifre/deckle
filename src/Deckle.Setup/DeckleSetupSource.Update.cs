using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── In-app updater ────────────────────────────────────────────────────────

    [Event(EvtUpdateUpToDate,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Deckle is up to date")]
    public void UpdateUpToDate()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateUpToDate);
    }

    [Event(EvtUpdateAvailable,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A newer Deckle release is available")]
    public void UpdateAvailable()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateAvailable);
    }

    [Event(EvtUpdateCheckDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update check | installed={0} | latest={1} | newer={2}")]
    public void UpdateCheckDetail(string installed, string latest, bool newer)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckDetail, installed, latest, newer);
    }

    [Event(EvtUpdateCheckFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The update check could not complete")]
    public void UpdateCheckFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckFailed);
    }

    [Event(EvtUpdateCheckFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update check failed | reason={0}")]
    public void UpdateCheckFailedDetail(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckFailedDetail, reason);
    }

    [Event(EvtUpdateCheckSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update check skipped | reason={0}")]
    public void UpdateCheckSkippedDetail(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckSkippedDetail, reason);
    }

    [Event(EvtUpdateDownloadStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Downloading the Deckle update")]
    public void UpdateDownloadStarted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadStarted);
    }

    [Event(EvtUpdateDownloadStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update download | version={0} | url={1} | size_bytes={2}")]
    public void UpdateDownloadStartedDetail(string version, string url, long size_bytes)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadStartedDetail, version, url, size_bytes);
    }

    [Event(EvtUpdateDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The update download failed")]
    public void UpdateDownloadFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadFailed);
    }

    [Event(EvtUpdateDownloadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update download failed | step={0} | reason={1}")]
    public void UpdateDownloadFailedDetail(string step, string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadFailedDetail, step, reason);
    }

    [Event(EvtUpdateHandoff,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Handing off to the downloaded version")]
    public void UpdateHandoff()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateHandoff);
    }

    [Event(EvtUpdateHandoffDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "update handoff | exe={0} | cleanup={1}")]
    public void UpdateHandoffDetail(string exe, string cleanup)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateHandoffDetail, exe, cleanup);
    }

}
