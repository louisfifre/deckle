namespace Deckle.Diagnostics.Logging;

// Process-lifetime state: closing the lazy LogWindow preserves an investigation,
// while a Deckle restart starts from the default view. That view hides the
// per-step detail, the Verbose entries (maintainer's decision, 2026-11-13,
// replacing the unfiltered start of 2026-10-23): the window opens on what
// happened, and one toolbar control shows the detail. No token is
// pre-picked; a filter only ever comes from the person.
public static class LogWindowFilterSession
{
    public static LogFilterSelection Selection { get; } = new() { ShowDetail = false };
}
