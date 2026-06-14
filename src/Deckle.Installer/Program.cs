using Deckle.Installer;

// Entry point. Top-level statements compile to a NativeAOT-friendly Main.

ConsoleUi.EnableVirtualTerminal();
CliArgs cli = CliArgs.Parse(args);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

int code;
try
{
    code = cli.Uninstall
        ? await Uninstaller.RunAsync(cli, cts.Token)
        : await InstallFlow.RunAsync(cli, cts.Token);
}
catch (OperationCanceledException)
{
    ConsoleUi.Warn("Cancelled.");
    code = 130; // 128 + SIGINT, the conventional Ctrl+C exit code
}
catch (Exception ex)
{
    ConsoleUi.Error(ex.Message);
    code = 1;
}

// Keep the window up when double-clicked (interactive run), so the user reads the
// outcome instead of a console that flashes and vanishes.
if (!cli.AssumeYes)
{
    Console.WriteLine();
    ConsoleUi.Info("Press Enter to close…");
    Console.ReadLine();
}

return code;
