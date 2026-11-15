using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// What is read from a project's folder, ids 10 to 19. An event here carries
// counts, durations and reasons, never a path, a project's name or a remote's
// address.
public sealed partial class DeckleProjectsSource
{
    public const int EvtProjectsRead      = 10;
    public const int EvtGitUnavailable    = 11;
    public const int EvtManifestUnreadable = 12;
    public const int EvtAgentReadingDone  = 13;
    public const int EvtReadingFailed     = 14;

    // One batch of readings finished: how many projects and how long the
    // batch took, so a slow Home is visible without a name in the line.
    [Event(EvtProjectsRead,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "projects={0} | elapsed_ms={1}")]
    public void ProjectsRead(long projects, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtProjectsRead, projects, elapsed_ms);
    }

    // Git could not answer for a project: the page shows nothing for git and
    // goes on. reason is one of not-found, timeout, dubious-ownership,
    // exit-code. Written once per reason until git answers again, not once
    // per project and refresh: a missing git is one incident.
    [Event(EvtGitUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Git could not be read for a project; its git readouts stay empty\nreason={0}")]
    public void GitUnavailable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtGitUnavailable, reason);
    }

    // A manifest that could not be parsed gives no source and never fails the
    // reading: only its format is named.
    [Event(EvtManifestUnreadable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "format={0}")]
    public void ManifestUnreadable(string format)
    {
        if (IsEnabled()) WriteEvent(EvtManifestUnreadable, format);
    }

    // One project's folder could not be read at all (a refused folder, say):
    // its previous reading stands and the others finish.
    [Event(EvtReadingFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A project's folder could not be read; its previous readouts stay\nex_type={0}")]
    public void ReadingFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtReadingFailed, ex_type);
    }

    [Event(EvtAgentReadingDone,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "tokens={0} | truncated={1} | elapsed_ms={2}")]
    public void AgentReadingDone(long tokens, bool truncated, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAgentReadingDone, tokens, truncated, elapsed_ms);
    }
}
