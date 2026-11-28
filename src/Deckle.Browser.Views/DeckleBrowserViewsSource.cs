using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Browser.Views;

// Browser window provider: the web view's start and its failure. No event
// carries an address, a page title or a domain: what a person visits never
// reaches the log (deckle-logging), so a line names a stage and a duration
// only.
[EventSource(Name = "Deckle-BrowserViews")]
public sealed class DeckleBrowserViewsSource : DeckleEventSource
{
    public static readonly DeckleBrowserViewsSource Log = new();

    private DeckleBrowserViewsSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event.
    public const int EvtWebViewReady = 1;
    public const int EvtWebViewFailed = 2;
    public const int EvtSessionSaveFailed = 3;
    public const int EvtPageSuspended = 4;
    public const int EvtEngineVersion = 5;
    public const int EvtTrackingPreventionFailed = 6;
    public const int EvtProcessFailed = 7;
    public const int EvtAdblockEngineBuilt = 8;
    public const int EvtAdblockListFailed = 9;
    public const int EvtAdblockBuildFailed = 10;

    [Event(EvtSessionSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Browser session could not be saved\nex_type={0}")]
    public void SessionSaveFailed(string ex_type)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSessionSaveFailed, ex_type);
    }

    // A hidden page was asked to suspend; the outcome only, never which page.
    [Event(EvtPageSuspended,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "suspended={0}")]
    public void PageSuspended(bool suspended)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPageSuspended, suspended);
    }

    [Event(EvtWebViewReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "duration_ms={0}")]
    public void WebViewReady(long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWebViewReady, duration_ms);
    }

    [Event(EvtWebViewFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Browser window could not start its web view\nex_type={0} | ex_message={1}")]
    public void WebViewFailed(string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWebViewFailed, ex_type, ex_message);
    }

    // The web engine's own version: a machine fact, not something visited.
    [Event(EvtEngineVersion,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "engine_version={0}")]
    public void EngineVersion(string engine_version)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtEngineVersion, engine_version);
    }

    [Event(EvtTrackingPreventionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Strict tracking prevention could not be applied\nex_type={0}")]
    public void TrackingPreventionFailed(string ex_type)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtTrackingPreventionFailed, ex_type);
    }

    // A web view process ended badly: which kind of process, never which page.
    [Event(EvtProcessFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A web view process failed\nprocess_kind={0}")]
    public void ProcessFailed(string process_kind)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtProcessFailed, process_kind);
    }

    // The ad blocker's engine was built from the lists kept on disk: two numbers.
    [Event(EvtAdblockEngineBuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "rule_count={0} | duration_ms={1}")]
    public void AdblockEngineBuilt(int rule_count, long duration_ms)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtAdblockEngineBuilt, rule_count, duration_ms);
    }

    // A filter list could not be fetched: the list's own name (EasyList...), never a page.
    [Event(EvtAdblockListFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A filter list could not be downloaded\nlist_name={0}")]
    public void AdblockListFailed(string list_name)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtAdblockListFailed, list_name);
    }

    [Event(EvtAdblockBuildFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The ad blocker's engine could not be built\nex_type={0}")]
    public void AdblockBuildFailed(string ex_type)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtAdblockBuildFailed, ex_type);
    }
}
