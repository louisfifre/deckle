using System.ComponentModel;
using System.Diagnostics;

namespace Deckle.Projects;

// Opens a new Windows Terminal window:
//
//   wt.exe -w new --title <title> --suppressApplicationTitle -d <dir> <shell> -NoProfile -ExecutionPolicy Bypass -EncodedCommand <b64>
//
// `-w new` forces a window of its own under the default settings (measured; a
// person's own windowingBehavior setting may still override it, not verified).
// `--title` with `--suppressApplicationTitle` keeps the title Deckle chose, which
// is how "Show terminal" finds the window again. The command travels encoded
// (see RunWrapper), so nothing in it reaches wt.exe's reading of ';' and quotes.
//
// wt.exe is an app execution alias in WindowsApps: it hands the command to a
// Windows Terminal process and returns after about 120 ms with exit code 0
// whatever the command does. Started here means the alias answered, nothing
// more.
public sealed class WindowsTerminalLauncher(IProcessStarter starter) : ITerminalLauncher
{
    public TerminalStart Launch(TerminalCommand command)
    {
        var start = new ProcessStartInfo("wt.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments(command)) start.ArgumentList.Add(argument);

        try
        {
            starter.Start(start)?.Dispose();
            return new TerminalStart(true, false, TerminalStart.WindowsTerminal);
        }
        catch (Win32Exception)
        {
            // Not installed, or the alias is disabled.
            return TerminalStart.Failed(TerminalStart.WindowsTerminal, "not-found");
        }
        catch (InvalidOperationException)
        {
            return TerminalStart.Failed(TerminalStart.WindowsTerminal, "start-failed");
        }
    }

    // The argument list, one entry per argument: the process class does the
    // quoting for the command line.
    public static IReadOnlyList<string> Arguments(TerminalCommand command)
    {
        List<string> arguments =
        [
            "-w", "new",
            "--title", TitleText(command.Title),
            "--suppressApplicationTitle",
            "-d", EscapeSemicolons(command.WorkingDirectory),
        ];
        if (command.ScriptText is not null)
        {
            arguments.Add(command.Shell ?? "powershell.exe");
            arguments.Add("-NoProfile");
            arguments.Add("-ExecutionPolicy");
            arguments.Add("Bypass");
            arguments.Add("-EncodedCommand");
            arguments.Add(RunWrapper.Encode(command.ScriptText));
        }
        return arguments;
    }

    // wt.exe reads ';' as the separator of its own commands. A title is only
    // for the person to read and for Show terminal to match, so a ';' in a
    // project's name becomes a comma, here and in the match.
    public static string TitleText(string title) => title.Replace(';', ',');

    // A folder called "a;b" would split the command: wt's own escape is "\;"
    // (from its documentation; not tried on a real folder here).
    private static string EscapeSemicolons(string path) => path.Replace(";", "\\;");
}
