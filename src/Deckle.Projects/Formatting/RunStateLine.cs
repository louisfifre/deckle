namespace Deckle.Projects;

// How a state line looks, so the view picks the leading mark without parsing
// the text: nothing for Neutral and Success (the spec draws no icon on a
// success), a progress ring for Running, the warning glyph in the critical
// colour for Failure.
public enum RunLineKind
{
    Neutral,
    Running,
    Success,
    Failure,
}

// One state line: its words, its kind, and the note to add under it when the
// action ran in the system's console because Windows Terminal was not found
// (spec: "the action's state line says so").
public sealed record RunLine(string Text, RunLineKind Kind, string? Note = null);

// The words of the state lines of an action (spec, Pinned actions), in one
// place a test can hold to the spec's sentences. Time is passed in, so "4 min
// ago" reads the same on every run.
public static class RunStateLine
{
    public const string FallbackNote = "Windows Terminal was not found, so this ran in the Windows console.";

    public const string MenuNoLongerHasIt = "The script menu no longer has this action";

    // The card of a pinned action that is not running: "Not run yet",
    // "Succeeded 4 min ago · exit code 0", "Failed 4 min ago · exit code 1",
    // "Closed 4 min ago before it ended", "The terminal did not start".
    public static RunLine Idle(ActionResult? last, DateTimeOffset now)
    {
        if (last is null) return new RunLine("Not run yet", RunLineKind.Neutral);

        string ago = ElapsedFormat.Ago(now - last.EndedAt);
        string? note = last.UsedFallback ? FallbackNote : null;
        return last.Outcome switch
        {
            RunOutcome.Succeeded => new RunLine($"Succeeded {ago} · exit code {last.ExitCode}", RunLineKind.Success, note),
            RunOutcome.Failed => new RunLine($"Failed {ago} · exit code {last.ExitCode}", RunLineKind.Failure, note),
            RunOutcome.Closed => new RunLine($"Closed {ago} before it ended", RunLineKind.Failure, note),
            _ => new RunLine("The terminal did not start", RunLineKind.Failure, note),
        };
    }

    // "Running · 1 min 12 s".
    public static RunLine Running(ActionRun run, DateTimeOffset now) =>
        new($"Running · {ElapsedFormat.Running(now - run.StartedAt)}", RunLineKind.Running, run.UsedFallback ? FallbackNote : null);

    // Another action of the project runs: "Waits for Self-check to end".
    public static RunLine Waiting(string runningLabel) => new($"Waits for {runningLabel} to end", RunLineKind.Neutral);

    // A pinned entry the menu no longer gives keeps its card.
    public static RunLine Gone() => new(MenuNoLongerHasIt, RunLineKind.Neutral);

    // The line under the header's buttons on a project page: the main action's
    // last result or its running state. Null when the main action has not run
    // (nothing is said), which is also what a project without one gets.
    //
    //   "Build & run failed 4 min ago · exit code 1"      Failure
    //   "Build & run succeeded 4 min ago · exit code 0"   Success
    //   "Build & run running · 1 min 12 s"                Running
    public static RunLine? Main(string label, ActionResult? last, ActionRun? running, DateTimeOffset now)
    {
        if (running is not null)
            return new RunLine($"{label} running · {ElapsedFormat.Running(now - running.StartedAt)}", RunLineKind.Running,
                running.UsedFallback ? FallbackNote : null);
        if (last is null) return null;

        string ago = ElapsedFormat.Ago(now - last.EndedAt);
        string? note = last.UsedFallback ? FallbackNote : null;
        return last.Outcome switch
        {
            RunOutcome.Succeeded => new RunLine($"{label} succeeded {ago} · exit code {last.ExitCode}", RunLineKind.Success, note),
            RunOutcome.Failed => new RunLine($"{label} failed {ago} · exit code {last.ExitCode}", RunLineKind.Failure, note),
            RunOutcome.Closed => new RunLine($"{label} closed {ago} before it ended", RunLineKind.Failure, note),
            _ => new RunLine($"{label} did not start", RunLineKind.Failure, note),
        };
    }
}
