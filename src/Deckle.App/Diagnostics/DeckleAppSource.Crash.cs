using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // Each crash milestone carries its cause; the Detail companions stay
    // because the journal predicate keeps them by name for older readers.
    [Event(EvtCrashUnhandled,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Unhandled exception caught | {0}: {1}")]
    public void CrashUnhandled(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashUnhandled, ex_type, ex_message);
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
           Message = "Unhandled exception caught on the AppDomain | {0}: {1}")]
    public void CrashAppDomain(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomain, ex_type, ex_message);
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
           Message = "An unobserved task exception was caught | {0}: {1} | inner_count={2}")]
    public void CrashTaskScheduler(string ex_type, string ex_message, int inner_count)
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskScheduler, ex_type, ex_message, inner_count);
    }

    [Event(EvtCrashTaskSchedulerDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "task scheduler unobserved exception | error={0} | message={1}")]
    public void CrashTaskSchedulerDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashTaskSchedulerDetail, ex_type, ex_message);
    }

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
           Tags = ObservationTags.Journal,
           Message = "ProcessExit triggered")]
    public void ProcessExit()
    {
        if (IsEnabled()) WriteEvent(EvtProcessExit);
    }

}
