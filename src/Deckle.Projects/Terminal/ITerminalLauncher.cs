using System.Diagnostics;

namespace Deckle.Projects;

// Opens a terminal. Returns once the terminal was asked for: what runs inside
// it is followed by the wrapper, not by this call.
public interface ITerminalLauncher
{
    TerminalStart Launch(TerminalCommand command);
}

// How a process is started, so a test of the launchers sees the command lines
// they build without starting anything.
public interface IProcessStarter
{
    // The started process (the caller disposes it), or null when the system
    // gave none back. Throws Win32Exception when the program cannot be started.
    Process? Start(ProcessStartInfo info);
}

public sealed class SystemProcessStarter : IProcessStarter
{
    public Process? Start(ProcessStartInfo info) => Process.Start(info);
}
