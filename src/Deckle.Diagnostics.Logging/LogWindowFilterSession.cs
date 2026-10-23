namespace Deckle.Diagnostics.Logging;

// Process-lifetime state: closing the lazy LogWindow preserves an investigation,
// while a Deckle restart starts from the unfiltered view.
public static class LogWindowFilterSession
{
    public static LogFilterSelection Selection { get; } = new();
}
