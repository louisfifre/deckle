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
}
