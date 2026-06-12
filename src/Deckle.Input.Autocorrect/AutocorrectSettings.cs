namespace Deckle.Input.Autocorrect;

// Module settings. Enrollment is the activation gate (CONTEXT.md § Autocorrect):
// an app never enrolled is never touched. The v1 prototype edits this list via
// the CLI `enroll` command; the enrollment prompt arrives with the notification
// brick, later.
public sealed class AutocorrectSettings
{
    public bool Enabled { get; set; } = true;

    // Process names without extension, compared case-insensitively.
    public List<string> EnrolledProcesses { get; set; } = new() { "notepad" };
}
