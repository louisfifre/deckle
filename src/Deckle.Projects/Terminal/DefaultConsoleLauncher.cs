using System.ComponentModel;
using System.Diagnostics;

namespace Deckle.Projects;

// Opens the shell itself and lets Windows host it in its default terminal
// (UseShellExecute, with the working folder): the way out when wt.exe is not
// there, and the choice of a person who prefers the system's own window.
//
// Measured: on the maintainer's machine the default host is Windows Terminal
// too, and the shell was a real child process whose exit code was readable.
// The wrapper's result file serves this path as well, so one watching serves
// both launchers.
public sealed class DefaultConsoleLauncher(IProcessStarter starter) : ITerminalLauncher
{
    public TerminalStart Launch(TerminalCommand command)
    {
        var start = new ProcessStartInfo(command.Shell ?? "powershell.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = command.WorkingDirectory,
        };
        if (command.ScriptText is not null)
            start.Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {RunWrapper.Encode(command.ScriptText)}";

        try
        {
            starter.Start(start)?.Dispose();
            return new TerminalStart(true, false, TerminalStart.Console);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return TerminalStart.Failed(TerminalStart.Console, "start-failed");
        }
    }
}
