namespace Deckle.Diagnostics.Logging;

// Process-lifetime state: closing the lazy LogWindow preserves an investigation,
// while a Deckle restart starts from the unfiltered view. app.jsonl deliberately
// uses a different, persisted LogFilterSelection instance.
public static class LogWindowFilterSession
{
    public static LogFilterSelection Selection { get; } = new();
}
