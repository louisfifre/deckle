using System.Diagnostics;

using Deckle.Installer.Platform;
using Deckle.Installer.Ui;

namespace Deckle.Installer.Install;

// ── Uninstaller ───────────────────────────────────────────────────────────────
//
// Reached when the installed copy is re-invoked with --uninstall (the
// UninstallString registered in Installed apps). It runs from inside the install
// folder, so the install location is simply this exe's directory.
//
// Removal order: deregister first (shortcut, Installed-apps key), optionally drop
// the data folder, then schedule the binaries — including this running exe — for
// deletion. A process can't delete its own image, so a detached cmd waits for exit
// and removes the folder. Models are preserved unless the user explicitly opts in:
// re-downloading 3 GB is not something to do silently.
internal static class Uninstaller
{
    public static Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        ConsoleUi.Banner("Deckle Uninstaller", "Removes Deckle from this PC.");

        string installDir = Path.GetDirectoryName(Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the uninstaller's own path."))!;

        if (!cli.AssumeYes && !ConsoleUi.Confirm("Remove Deckle?", defaultYes: true))
        {
            ConsoleUi.Info("Cancelled.");
            return Task.FromResult(0);
        }

        // ── Deregister ────────────────────────────────────────────────────────────
        Shortcut.RemoveStartMenu("Deckle");
        ConsoleUi.Ok("Start Menu shortcut removed");
        UninstallEntry.Remove();
        ConsoleUi.Ok("removed from Installed apps");

        // ── Data / models (opt-in, preserved by default) ─────────────────────────
        string? dataRoot = UserEnvironment.GetDataRoot();
        string dataDir = dataRoot ?? InstallPaths.DefaultDataDir;
        bool removeData = !cli.AssumeYes
            && Directory.Exists(dataDir)
            && ConsoleUi.Confirm($"Also delete data and models at {dataDir}?", defaultYes: false);
        if (removeData)
        {
            TryDeleteTree(dataDir);
            ConsoleUi.Ok("data and models deleted");
        }
        if (dataRoot is not null)
        {
            UserEnvironment.ClearDataRoot();
            ConsoleUi.Ok("DECKLE_DATA_ROOT cleared");
        }

        // ── Binaries (self-deleting) ──────────────────────────────────────────────
        ScheduleFolderDeletion(installDir);
        ConsoleUi.Ok("Deckle removed");
        return Task.FromResult(0);
    }

    // Spawns a detached cmd that waits ~1 s for this process to release its image,
    // then recursively removes the install folder (this exe included). ping is the
    // universally-present delay primitive.
    private static void ScheduleFolderDeletion(string installDir)
    {
        var psi = new ProcessStartInfo("cmd.exe",
            $"/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{installDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { ConsoleUi.Warn($"could not delete {dir}: {ex.Message}"); }
    }
}
