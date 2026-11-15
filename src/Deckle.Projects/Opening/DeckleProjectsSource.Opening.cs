using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// The links' block, ids 40 to 49: what opens a project in another tool. kind is
// the link (explorer, terminal, script-menu, web-page), never an address.
public sealed partial class DeckleProjectsSource
{
    public const int EvtProjectLinkOpened = 40;
    public const int EvtProjectLinkFailed = 41;

    [Event(EvtProjectLinkOpened,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "kind={0}")]
    public void ProjectLinkOpened(string kind)
    {
        if (IsEnabled()) WriteEvent(EvtProjectLinkOpened, kind);
    }

    // A link did not open: the folder is gone, the system refused or no terminal
    // started. The page goes on.
    [Event(EvtProjectLinkFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A project link could not be opened\nkind={0} | reason={1}")]
    public void ProjectLinkFailed(string kind, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtProjectLinkFailed, kind, reason);
    }
}
