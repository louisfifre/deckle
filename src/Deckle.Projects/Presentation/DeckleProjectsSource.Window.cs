using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// The window's block, ids 50 to 59. The host already writes the window's own
// milestones (opened, loaded); these are what a page adds: which one was shown
// and how long it took, and the one that failed. kind is home, category or
// project.
public sealed partial class DeckleProjectsSource
{
    public const int EvtProjectsPageShown  = 50;
    public const int EvtProjectsPageFailed = 51;

    [Event(EvtProjectsPageShown,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "kind={0} | elapsed_ms={1}")]
    public void ProjectsPageShown(string kind, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtProjectsPageShown, kind, elapsed_ms);
    }

    // A page that throws while it is built: the window shows a message in its
    // place and keeps the pane and the other pages.
    [Event(EvtProjectsPageFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A page failed to show; the window shows a message in its place\nkind={0} | ex_type={1}")]
    public void ProjectsPageFailed(string kind, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtProjectsPageFailed, kind, ex_type);
    }
}
