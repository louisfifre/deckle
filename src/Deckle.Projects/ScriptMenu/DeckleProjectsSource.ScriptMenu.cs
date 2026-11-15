using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// The script menu's block, ids 20 to 29. The error's last lines go to the page
// and never to a log: they are the project's own text.
public sealed partial class DeckleProjectsSource
{
    public const int EvtScriptMenuListed     = 20;
    public const int EvtScriptMenuUnreadable = 21;

    [Event(EvtScriptMenuListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entries={0} | elapsed_ms={1}")]
    public void ScriptMenuListed(long entries, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtScriptMenuListed, entries, elapsed_ms);
    }

    // The menu exists and could not be read: the page says so and shows the
    // error's last lines. reason is exit-code, timeout, no-rows or start-failed.
    [Event(EvtScriptMenuUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A project's script menu could not be read; its page says so\nreason={0}")]
    public void ScriptMenuUnreadable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtScriptMenuUnreadable, reason);
    }
}
