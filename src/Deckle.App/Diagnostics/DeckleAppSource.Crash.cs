using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Crash safety net ────────────────────────────────────────────────

    [Event(EvtCrashUnhandled,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Unhandled exception caught")]
    public void CrashUnhandled()
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandled);
    }

    [Event(EvtCrashUnhandledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "unhandled exception | error={0} | message={1}")]
    public void CrashUnhandledDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandledDetail, ex_type, ex_message);
    }

    [Event(EvtCrashAppDomain,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Unhandled exception caught on the AppDomain")]
    public void CrashAppDomain()
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomain);
    }

    [Event(EvtCrashAppDomainDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "appdomain unhandled exception | error={0} | message={1}")]
    public void CrashAppDomainDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomainDetail, ex_type, ex_message);
    }

    [Event(EvtCrashTaskScheduler,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An unobserved task exception was caught")]
    public void CrashTaskScheduler()
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskScheduler);
    }

    [Event(EvtCrashTaskSchedulerDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "task scheduler unobserved exception | error={0} | message={1}")]
    public void CrashTaskSchedulerDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskSchedulerDetail, ex_type, ex_message);
    }

    // Demoted to Verbose: a bare stack trace carries no user-facing value on
    // its own — it is the technical companion of the crash milestones above.
    // Kept at its frozen id (no rename, no mirror).
    [Event(EvtCrashStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash stack trace | stack={0}")]
    public void CrashStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtCrashStackTrace, stack_trace);
    }

    [Event(EvtProcessExit,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ProcessExit triggered")]
    public void ProcessExit()
    {
        if (IsEnabled()) WriteEvent(EvtProcessExit);
    }

}
