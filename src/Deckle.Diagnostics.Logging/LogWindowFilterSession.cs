using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics.Logging;

// Process-lifetime state: closing the lazy LogWindow preserves an investigation,
// while a Deckle restart starts again from the initial view.
public static class LogWindowFilterSession
{
    public static LogFilterSelection Selection { get; } = CreateInitialSelection();

    // The window opens on what a person reads: every severity but Verbose is
    // checked, so the name=value detail rows stay out until someone asks for
    // them. Reset clears the selection and shows Verbose too.
    public static LogFilterSelection CreateInitialSelection()
    {
        var selection = new LogFilterSelection();
        foreach (EventLevel level in LogFilterSelection.SeverityOrder)
            if (level != EventLevel.Verbose)
                selection.Add(new LogFilterToken(LogFilterDimension.Severity, level.ToString()));
        return selection;
    }
}
