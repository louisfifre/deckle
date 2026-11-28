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
    public const int EvtUboResourcesConverted = 11;
    public const int EvtUboResourcesFailed = 12;
    public const int EvtGestureStarted = 13;
    public const int EvtGestureEnded = 14;
    public const int EvtNavigationStarted = 15;
    public const int EvtNavigationEnded = 16;
    public const int EvtActivitySummary = 17;

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

    // uBlock Origin's scriptlets were converted to the engine's resources: two numbers.
    [Event(EvtUboResourcesConverted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "resource_count={0} | duration_ms={1}")]
    public void UboResourcesConverted(int resource_count, long duration_ms)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUboResourcesConverted, resource_count, duration_ms);
    }

    // The scriptlets could not be fetched or converted: fixed words, never an address.
    [Event(EvtUboResourcesFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "uBlock Origin's scriptlets could not be prepared\nstage={0} | reason={1}")]
    public void UboResourcesFailed(string stage, string reason)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtUboResourcesFailed, stage, reason);
    }

    // A gesture of the person (a click, a key, a drag) begins and ends in the
    // window: gesture_kind is a BrowserGesture's name, never a page. The end's
    // duration is the time the window spent on it, so a slow gesture reads
    // in the file alone.
    [Event(EvtGestureStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A gesture started")]
    public void GestureStarted(string gesture_kind, string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtGestureStarted, gesture_kind, work_id);
    }

    [Event(EvtGestureEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A gesture ended")]
    public void GestureEnded(string gesture_kind, string outcome, long duration_ms, string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtGestureEnded, gesture_kind, outcome, duration_ms, work_id);
    }

    // A top-level navigation of a page starts and ends: the outcome and the
    // engine's error status name, never the address.
    [Event(EvtNavigationStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A page navigation started")]
    public void NavigationStarted(string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationStarted, work_id);
    }

    [Event(EvtNavigationEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A page navigation ended")]
    public void NavigationEnded(string outcome, string web_error, long duration_ms, string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationEnded, outcome, web_error, duration_ms, work_id);
    }

    // What the pages did many times a second since the last summary, counted
    // and never logged one by one: the requests they made, how many the blocker
    // stopped, the changes of their label, source and icon, the navigations that
    // began, the lists of suggestions, the finds and the reads of a playing page's
    // media (the comfort's bursts), and the stalls of the window's UI thread (a tick of the 100 ms
    // watch more than 200 ms late) with the worst lateness. Written every 10 s
    // while any of it is not zero, and once more as a window closes.
    [Event(EvtActivitySummary,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Summary,
           Message = "interval_ms={0} | web_requests={1} | blocked_requests={2} | label_changes={3} | source_changes={4} | icon_changes={5} | navigations={6} | suggestion_lists={7} | find_searches={8} | media_reads={9} | ui_stalls={10} | worst_stall_ms={11}")]
    public void ActivitySummary(
        long interval_ms, long web_requests, long blocked_requests, long label_changes, long source_changes,
        long icon_changes, long navigations, long suggestion_lists, long find_searches, long media_reads,
        long ui_stalls, long worst_stall_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtActivitySummary, interval_ms, web_requests, blocked_requests, label_changes,
                source_changes, icon_changes, navigations, suggestion_lists, find_searches, media_reads,
                ui_stalls, worst_stall_ms);
    }
}
