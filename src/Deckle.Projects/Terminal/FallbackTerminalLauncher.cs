namespace Deckle.Projects;

// The launcher the app uses: the chosen terminal, and the system's console host
// when the chosen one cannot be started (wt.exe is not on the machine). The
// fallback is a Warning naming what was asked and what was obtained
// (deckle-logging), and the action's state line says so (spec, Behaviours).
public sealed class FallbackTerminalLauncher(ITerminalLauncher chosen, ITerminalLauncher fallback) : ITerminalLauncher
{
    public TerminalStart Launch(TerminalCommand command)
    {
        TerminalStart first = chosen.Launch(command);
        if (first.Started) return first;

        TerminalStart second = fallback.Launch(command);
        if (!second.Started) return second;

        DeckleProjectsSource.Log.TerminalFallbackUsed(first.Terminal, second.Terminal);
        return second with { UsedFallback = true };
    }
}

public static class TerminalLaunchers
{
    // Windows Terminal with the console as its fallback, or the console alone
    // when that is what the person chose (no fallback is then involved).
    public static ITerminalLauncher Create(TerminalChoice choice, IProcessStarter? starter = null)
    {
        starter ??= new SystemProcessStarter();
        var console = new DefaultConsoleLauncher(starter);
        return choice == TerminalChoice.WindowsDefault
            ? console
            : new FallbackTerminalLauncher(new WindowsTerminalLauncher(starter), console);
    }
}
