using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // An exception a net caught and the process survived: the UI framework's net
    // marked it handled, or the task scheduler's marked it observed. Work went on,
    // so it is a Warning, and the first occurrence per exception type and site
    // opens the incident with its cause on one line. The stack goes in
    // CrashStackTrace beside it. Later occurrences are SurvivedExceptionRepeated.
    [Event(EvtSurvivedException,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An exception was caught and the app keeps running\norigin={0} | ex_type={1} | site={2} | ex_message={3}")]
    public void SurvivedException(string origin, string ex_type, string site, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedException, origin, ex_type, site, ex_message);
    }

    // The same incident again. Verbose and not logged: the log already
    // has the opening line, and a fault that fires on every frame must not fill it.
    [Event(EvtSurvivedExceptionRepeated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "origin={0} | ex_type={1} | site={2} | occurrences={3}")]
    public void SurvivedExceptionRepeated(string origin, string ex_type, string site, int occurrences)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedExceptionRepeated, origin, ex_type, site, occurrences);
    }

    // An exception that ends the process stays an Error. The Detail companion
    // stays because the log predicate keeps it by name for older readers.
    [Event(EvtCrashAppDomain,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An unhandled exception is ending Deckle\nex_type={0} | ex_message={1}")]
    public void CrashAppDomain(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomain, ex_type, ex_message);
    }

    [Event(EvtCrashAppDomainDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "error={0} | message={1}")]
    public void CrashAppDomainDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCrashAppDomainDetail, ex_type, ex_message);
    }

    [Event(EvtCrashStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stack={0}")]
    public void CrashStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtCrashStackTrace, stack_trace);
    }

    [Event(EvtProcessExit,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "ProcessExit triggered")]
    public void ProcessExit()
    {
        if (IsEnabled()) WriteEvent(EvtProcessExit);
    }

}
