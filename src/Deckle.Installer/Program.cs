using System.Diagnostics;
using Deckle.Installer;

// Entry point. WinExe: no console is allocated, so the stub is silent by design —
// it speaks only through its native progress window and, on failure, a message box.
// Routing is minimal: --uninstall reverses an install, anything else installs.
//
// Both entry points own the whole window lifecycle: they create the window, drive
// the work on a background thread, and pump the message loop here on the main
// thread (a window must be serviced on its creating thread).
CliArgs cli = CliArgs.Parse(args);
string mode = cli.Uninstall ? "uninstall" : "install";

// Start and end bracket the whole run so the log always says how it finished,
// including the crash path, which is logged and then left to crash as before.
InstallLog.Info(
    $"Installer starting | mode={mode} | assume_yes={cli.AssumeYes} | exe={Environment.ProcessPath}");
var clock = Stopwatch.StartNew();
string outcome = "failed";
int exitCode = 1;
try
{
    exitCode = cli.Uninstall
        ? Uninstaller.Run(cli)
        : InstallFlow.Run(cli);
    outcome = InstallLog.Cancelled ? "cancelled" : exitCode == 0 ? "completed" : "failed";
    return exitCode;
}
catch (Exception ex)
{
    InstallLog.Error($"Installer crashed | reason=unhandled_exception | {InstallLog.Cause(ex)}");
    throw;
}
finally
{
    InstallLog.Info(
        $"Installer finished | mode={mode} | outcome={outcome} | exit_code={exitCode} | elapsed_ms={clock.ElapsedMilliseconds}");
}
