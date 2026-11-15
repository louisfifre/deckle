using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// The actions' block, ids 30 to 39. An action run starts and ends, the two
// paired by one run_id in the file (deckle-logging). Nothing here names a
// project, a path or an action: an action's id is a script's name.
public sealed partial class DeckleProjectsSource
{
    public const int EvtActionRunStarted    = 30;
    public const int EvtActionRunEnded      = 31;
    public const int EvtActionRunNotStarted = 32;
    public const int EvtTerminalFallbackUsed = 33;
    public const int EvtActionRunClosedEarly = 34;

    // terminal is windows-terminal or console; fallback is true when the one
    // asked for could not be started.
    [Event(EvtActionRunStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A project action started in a terminal")]
    public void ActionRunStarted(long run_id, string terminal, bool fallback)
    {
        if (IsEnabled()) WriteEvent(EvtActionRunStarted, run_id, terminal, fallback);
    }

    // outcome is Succeeded, Failed, Closed or NotStarted; exit_code is -1 when
    // the action wrote none (the terminal was closed, or never reported in).
    [Event(EvtActionRunEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A project action ended")]
    public void ActionRunEnded(long run_id, string outcome, long exit_code, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtActionRunEnded, run_id, outcome, exit_code, elapsed_ms);
    }

    // Work that does not happen says so with its reason, tagged like a start.
    // reason is folder-missing, no-menu, no-shell, already-running or
    // terminal-unavailable.
    [Event(EvtActionRunNotStarted,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A project action was not started\nreason={0}")]
    public void ActionRunNotStarted(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtActionRunNotStarted, reason);
    }

    // A fallback names what was asked and what was obtained.
    [Event(EvtTerminalFallbackUsed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The chosen terminal could not be started; the system's console was used instead\nasked={0} | got={1}")]
    public void TerminalFallbackUsed(string asked, string got)
    {
        if (IsEnabled()) WriteEvent(EvtTerminalFallbackUsed, asked, got);
    }

    // The terminal was closed (or the shell killed) before the action wrote its
    // exit code: the run ends as Closed. Deckle has no Stop button, so this is
    // how a person's Ctrl+C or a closed window shows.
    [Event(EvtActionRunClosedEarly,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A project action's terminal closed before the action reported an exit code\nrun_id={0} | elapsed_ms={1}")]
    public void ActionRunClosedEarly(long run_id, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtActionRunClosedEarly, run_id, elapsed_ms);
    }
}
