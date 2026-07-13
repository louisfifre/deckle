using Deckle.Installer;

// Entry point. WinExe: no console is allocated, so the stub is silent by design —
// it speaks only through its native progress window and, on failure, a message box.
// Routing is minimal: --uninstall reverses an install, anything else installs.
//
// Both entry points own the whole window lifecycle: they create the window, drive
// the work on a background thread, and pump the message loop here on the main
// thread (a window must be serviced on its creating thread).
CliArgs cli = CliArgs.Parse(args);

return cli.Uninstall
    ? Uninstaller.Run(cli)
    : InstallFlow.Run(cli);
