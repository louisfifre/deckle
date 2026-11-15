using System.Diagnostics;

namespace Deckle.Projects.Tests;

// A clock the test moves by hand, so "4 min ago" and a 20 s timeout need no
// wait.
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

// A terminal that is not one: records what it was asked and answers what the
// test says. Nothing is started.
internal sealed class FakeTerminalLauncher(TerminalStart? answer = null) : ITerminalLauncher
{
    public List<TerminalCommand> Commands { get; } = [];

    public TerminalStart Answer { get; set; } = answer ?? new TerminalStart(true, false, TerminalStart.WindowsTerminal);

    // When set, launching throws it, as a launcher that breaks would.
    public Exception? Throws { get; set; }

    public TerminalStart Launch(TerminalCommand command)
    {
        Commands.Add(command);
        if (Throws is not null) throw Throws;
        return Answer;
    }
}

internal sealed class FakeProcessProbe : IProcessProbe
{
    public HashSet<int> Alive { get; } = [];

    public bool IsAlive(int processId) => Alive.Contains(processId);
}

// A process starter that starts nothing: records the command lines and throws
// what the test says for a program.
internal sealed class FakeProcessStarter : IProcessStarter
{
    public List<ProcessStartInfo> Started { get; } = [];

    public HashSet<string> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Process? Start(ProcessStartInfo info)
    {
        if (Missing.Contains(info.FileName)) throw new System.ComponentModel.Win32Exception(2);
        Started.Add(info);
        return null;
    }
}

internal sealed class FakeWindowSystem : IWindowSystem
{
    public List<TopWindow> Windows { get; } = [];

    public HashSet<nint> Minimized { get; } = [];

    public List<string> Calls { get; } = [];

    public IReadOnlyList<TopWindow> TopWindows() => Windows;

    public bool IsMinimized(nint handle) => Minimized.Contains(handle);

    public void Restore(nint handle)
    {
        Calls.Add($"restore {handle}");
        Minimized.Remove(handle);
    }

    public bool BringToFront(nint handle)
    {
        Calls.Add($"front {handle}");
        return true;
    }
}
