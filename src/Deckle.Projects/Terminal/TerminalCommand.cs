namespace Deckle.Projects;

// What to open in a terminal, as text. A null ScriptText opens the terminal at
// the folder and nothing else (the "Terminal" link); otherwise the shell runs
// the script (an action's wrapper, or the menu).
public sealed record TerminalCommand(
    string WorkingDirectory,
    string Title,
    string? Shell = null,
    string? ScriptText = null);

// What asking for a terminal came to. Started means the terminal was asked for
// and the process started, not that the command ended: nothing about the
// command is known from here, which is why an action is followed by its wrapper.
public sealed record TerminalStart(bool Started, bool UsedFallback, string Terminal, string? FailureReason = null)
{
    public const string WindowsTerminal = "windows-terminal";
    public const string Console = "console";

    public static TerminalStart Failed(string terminal, string reason) => new(false, false, terminal, reason);
}
