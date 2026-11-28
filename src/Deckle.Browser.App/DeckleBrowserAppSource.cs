using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Browser.App;

// The Deckle Browser app's own story: its start, its end, the exception that
// ends it and the ones it survives. What the window does is the Browser window's provider's
// (Deckle-BrowserViews), and a window's creation and load are Deckle-Windowing's.
// A second copy turned away has no sink (the first holds the log open): it
// leaves a note file instead, as the published Deckle's does.
[EventSource(Name = "Deckle-BrowserApp")]
public sealed class DeckleBrowserAppSource : DeckleEventSource
{
    public static readonly DeckleBrowserAppSource Log = new();

    private DeckleBrowserAppSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event.
    public const int EvtAppStarting = 1;
    public const int EvtAppEnded = 2;
    public const int EvtUnhandledException = 3;
    public const int EvtSurvivedException = 4;
    public const int EvtSurvivedExceptionRepeated = 5;
    public const int EvtSurvivedStack = 6;
    public const int EvtWebViewMeasured = 7;

    // Which Deckle Browser this is, in the payload: the message stays a sentence.
    [Event(EvtAppStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Browser starting")]
    public void AppStarting(string version, string build, string session, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtAppStarting, version, build, session, pid);
    }

    // Paired with AppStarting by the session and the pid; an AppStarting
    // without it reads as a run that was killed.
    [Event(EvtAppEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Browser ended")]
    public void AppEnded(string session, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtAppEnded, session, pid);
    }

    // The exception ends the app (the AppDomain net).
    [Event(EvtUnhandledException,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Browser stopped on an exception nobody handled\norigin={0} | ex_type={1} | ex_message={2}")]
    public void UnhandledException(string origin, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUnhandledException, origin, ex_type, ex_message);
    }

    // An exception the UI framework's net caught and marked handled: the app
    // goes on, degraded, so it is a Warning, and the first occurrence per
    // exception type and site opens the incident with its cause on one line
    // (SurvivedExceptionLedger). The stack goes in SurvivedStack beside it.
    [Event(EvtSurvivedException,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An exception was caught and Deckle Browser keeps running\norigin={0} | ex_type={1} | site={2} | ex_message={3}")]
    public void SurvivedException(string origin, string ex_type, string site, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedException, origin, ex_type, site, ex_message);
    }

    // The same incident again: the log already has the opening line, and a
    // fault that fires on every frame must not fill it.
    [Event(EvtSurvivedExceptionRepeated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "origin={0} | ex_type={1} | site={2} | occurrences={3}")]
    public void SurvivedExceptionRepeated(string origin, string ex_type, string site, int occurrences)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedExceptionRepeated, origin, ex_type, site, occurrences);
    }

    [Event(EvtSurvivedStack,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stack={0}")]
    public void SurvivedStack(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedStack, stack_trace);
    }

    // What the self-check measured of the web view with a local page loaded:
    // a count and a size, never an address.
    [Event(EvtWebViewMeasured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "processes={0} | private_bytes={1}")]
    public void WebViewMeasured(long processes, long private_bytes)
    {
        if (IsEnabled()) WriteEvent(EvtWebViewMeasured, processes, private_bytes);
    }
}
