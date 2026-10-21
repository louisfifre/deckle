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

    // status is the HTTP status when the server answered, 0 when it never did
    // (offline, DNS, TLS): the two call for different readings.
    [Event(EvtUpdateCheckFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The update check could not complete | ex_type={0} | ex_message={1} | status={2}")]
    public void UpdateCheckFailed(string ex_type, string ex_message, int status)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckFailed, ex_type, ex_message, status);
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

    // The download is the update pipeline of this run: start, completion and
    // cancel go to the journal; a failure is a Warning, which it keeps anyway.
    [Event(EvtUpdateDownloadStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
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

    [Event(EvtUpdateDownloadCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The Deckle update was downloaded | version={0} | size_bytes={1} | duration_ms={2}")]
    public void UpdateDownloadCompleted(string version, long size_bytes, long duration_ms)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadCompleted, version, size_bytes, duration_ms);
    }

    [Event(EvtUpdateDownloadCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The update download was cancelled | reason=window_closed | step={0}")]
    public void UpdateDownloadCancelled(string step)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadCancelled, step);
    }

    // reason names the failure; ex_type is empty when no exception was thrown
    // (a download the shared Downloader reported failed, a payload without
    // Deckle.exe), and ex_message then carries that report.
    [Event(EvtUpdateDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The update download failed | reason={0} | step={1} | ex_type={2} | ex_message={3}")]
    public void UpdateDownloadFailed(string reason, string step, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadFailed, reason, step, ex_type, ex_message);
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
