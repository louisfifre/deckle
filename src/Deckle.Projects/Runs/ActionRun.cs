namespace Deckle.Projects;

// An action that is running in a terminal now.
//
// RunId pairs the start and the end in the log (a process-wide counter). Title
// is the terminal window's title, which "Show terminal" finds it by.
// UsedFallback is true when Windows Terminal could not be started and the
// action runs in the system's console host: the state line says so.
public sealed record ActionRun(
    long RunId,
    ActionRef Action,
    string Label,
    DateTimeOffset StartedAt,
    string Title,
    bool UsedFallback = false);

public enum RunOutcome
{
    // The exit code was 0.
    Succeeded,

    // The exit code was another number.
    Failed,

    // The terminal was closed, or the shell was killed, before the action wrote
    // its exit code: there is no code to show. The spec does not name this
    // outcome; it is read as a failure with its own line.
    Closed,

    // No terminal window ever reported in: nothing started.
    NotStarted,
}

// How the last run of an action ended. It stays until that action runs again or
// Deckle closes (spec): the failure of "Build & run" is still there while
// "Self-check" runs. Nothing is saved to disk (spec, Open 2, answer A).
//
// TerminalTitle is the title of the window the run had. The wrapper keeps that
// window open after a run that ended by itself, until a key is pressed, so
// "Show terminal" can still bring it to the front to read the output.
public sealed record ActionResult(
    RunOutcome Outcome, int? ExitCode, DateTimeOffset EndedAt, bool UsedFallback = false, string? TerminalTitle = null)
{
    public bool IsFailure => Outcome != RunOutcome.Succeeded;
}
