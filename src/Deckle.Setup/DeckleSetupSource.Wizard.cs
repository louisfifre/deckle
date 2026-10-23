using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Wizard lifecycle ──────────────────────────────────────────────────────

    // The setup window is the host of the install, update and relocate modes:
    // its open and close frame the whole run, so both go to the log.
    [Event(EvtWindowOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The setup window opened")]
    public void WindowOpened()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowOpened);
    }

    [Event(EvtWindowClosing,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The setup window is closing")]
    public void WindowClosing()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosing);
    }

    [Event(EvtWindowClosingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window closing | success={0}")]
    public void WindowClosingDetail(bool success)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosingDetail, success);
    }

    // ── Wizard-wide best-effort operations ────────────────────────────────────

    // Temp-folder cleanup, size and free-space probes, a copy attempt that has
    // a retry, opening Explorer: each failure leaves the user's result as it
    // was, so it stays Verbose, but a reader chasing a leftover folder or a
    // missing hint needs the operation, the path and the cause.
    [Event(EvtBestEffortFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "best effort failed | operation={0} | path={1} | ex_type={2} | ex_message={3}")]
    public void BestEffortFailedDetail(string operation, string path, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtBestEffortFailedDetail, operation, path, ex_type, ex_message);
    }

    // The bounded wait for the app that started an update ran out. The
    // running-process gate that follows still decides whether the payload is
    // replaced, but the hand-off did not happen as designed: the old app was
    // still alive after the wait, the likely cause of a blocked update that
    // follows. A degradation, so a Warning, which the log keeps.
    [Event(EvtPredecessorWaitTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The previous app was still running after the hand-off wait | pid={0} | timeout_ms={1}")]
    public void PredecessorWaitTimedOut(int pid, long timeout_ms)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPredecessorWaitTimedOut, pid, timeout_ms);
    }

    // The wait itself failed (the OS refused the handle, or the process object
    // was no longer usable), so the hand-off was not waited for at all. Like the
    // timeout, the running-process gate that follows still decides, and the
    // update goes on; the Warning names the error so a blocked update that
    // follows can be traced back to it.
    [Event(EvtPredecessorWaitFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The wait for the previous app failed | pid={0} | ex_type={1} | hresult={2}")]
    public void PredecessorWaitFailed(int pid, string ex_type, int hresult)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPredecessorWaitFailed, pid, ex_type, hresult);
    }

    // The pid ties this process's log to the child's: install-continue and
    // update-apply each start a new Deckle that writes its own run.
    [Event(EvtChildProcessLaunchedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "child process launched | role={0} | pid={1} | exe={2}")]
    public void ChildProcessLaunchedDetail(string role, int pid, string exe)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtChildProcessLaunchedDetail, role, pid, exe);
    }

}
