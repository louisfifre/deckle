namespace Deckle.Diagnostics.Logging;

// Process-lifetime state: closing the lazy LogWindow preserves an investigation,
// while a Deckle restart starts from the unfiltered view. The unfiltered start is
// the maintainer's choice, taken on seeing it run (2026-10-23): no severity is
// pre-checked, Verbose included, and a filter only ever comes from the person.
public static class LogWindowFilterSession
{
    public static LogFilterSelection Selection { get; } = new();
}
