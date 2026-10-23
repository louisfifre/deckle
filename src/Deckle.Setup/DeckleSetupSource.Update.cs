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
           Message = "installed={0} | latest={1} | newer={2}")]
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
           Message = "The update check could not complete\nex_type={0} | ex_message={1} | status={2}")]
    public void UpdateCheckFailed(string ex_type, string ex_message, int status)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckFailed, ex_type, ex_message, status);
    }

    [Event(EvtUpdateCheckSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0}")]
    public void UpdateCheckSkippedDetail(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateCheckSkippedDetail, reason);
    }

    // The download is the update pipeline of this run: start, completion and
    // cancel go to the log, and a failure is an Error, which it keeps: the
    // update does not happen. All four carry the work_id of one attempt (a retry
    // is a new attempt), so a start with no end reads as abandoned.
    [Event(EvtUpdateDownloadStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Downloading the Deckle update")]
    public void UpdateDownloadStarted(string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadStarted, work_id);
    }

    [Event(EvtUpdateDownloadStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "version={0} | url={1} | size_bytes={2}")]
    public void UpdateDownloadStartedDetail(string version, string url, long size_bytes)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadStartedDetail, version, url, size_bytes);
    }

    [Event(EvtUpdateDownloadCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Deckle update was downloaded")]
    public void UpdateDownloadCompleted(string work_id, string version, long size_bytes, long duration_ms)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadCompleted, work_id, version, size_bytes, duration_ms);
    }

    [Event(EvtUpdateDownloadCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The update download was cancelled because its window was closed")]
    public void UpdateDownloadCancelled(string work_id, string step)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadCancelled, work_id, step);
    }

    // reason names the failure; ex_type is empty when no exception was thrown
    // (a download the shared Downloader reported failed, a payload without
    // Deckle.exe), and ex_message then carries that report.
    [Event(EvtUpdateDownloadFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The update download failed\nwork_id={0} | reason={1} | step={2} | ex_type={3} | ex_message={4}")]
    public void UpdateDownloadFailed(string work_id, string reason, string step, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateDownloadFailed, work_id, reason, step, ex_type, ex_message);
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
           Message = "exe={0} | cleanup={1}")]
    public void UpdateHandoffDetail(string exe, string cleanup)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUpdateHandoffDetail, exe, cleanup);
    }

}
