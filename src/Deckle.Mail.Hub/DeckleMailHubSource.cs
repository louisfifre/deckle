using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Mail.Hub;

// Mail window provider: what data it loaded, which pages it opened and how,
// and the gestures made on its rows. No event carries the text of a mail, a
// sender or an address: a line names the page, the gesture and counts only
// (deckle-logging), so the record stays the same once real mail stands
// behind the window.
[EventSource(Name = "Deckle-MailHub")]
public sealed class DeckleMailHubSource : DeckleEventSource
{
    public static readonly DeckleMailHubSource Log = new();

    private DeckleMailHubSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event.
    public const int EvtDataLoaded = 1;
    public const int EvtNavigationFailed = 2;
    public const int EvtPageShown = 3;
    public const int EvtGestureApplied = 4;

    [Event(EvtDataLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Mail window loaded its data")]
    public void DataLoaded(string source, int mailboxes, int waiting, int lines)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDataLoaded, source, mailboxes, waiting, lines);
    }

    [Event(EvtNavigationFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Opening a page of the Mail window failed\npage={0} | error={1}")]
    public void NavigationFailed(string page, string error)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationFailed, page, error);
    }

    [Event(EvtPageShown,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0} | duration_ms={1}")]
    public void PageShown(string page, long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPageShown, page, duration_ms);
    }

    [Event(EvtGestureApplied,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "gesture={0} | applied={1}")]
    public void GestureApplied(string gesture, bool applied)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtGestureApplied, gesture, applied);
    }
}
