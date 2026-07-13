using System.Diagnostics;
using Deckle.Install;

namespace Deckle.Installer;

// ── Uninstaller ───────────────────────────────────────────────────────────────
//
// Reached when the installed copy is re-invoked with --uninstall (the
// UninstallString registered in Installed apps). It runs from inside the install
// folder, so the install location is simply this exe's directory.
//
// The stub kept this role even after the install flow moved into the WinUI wizard:
// the wizard copies this exe into the install folder, and --uninstall reverses the
// integration here — deregister the shortcut and the Installed-apps key, optionally
// drop the data folder, then schedule the binaries (this running exe included) for
// deletion. UX is two message boxes up front — remove, then keep-or-drop data —
// followed by a marquee progress window while it works. -y/--yes skips both prompts
// and keeps data (what the Installed-apps QuietUninstallString uses).
//
// Removal order matters: a process can't delete its own image, so a detached cmd
// waits for exit and removes the folder. That schedule is the very last step, after
// the window is gone and this process is about to exit, so nothing races the delete.
internal static class Uninstaller
{
    public static int Run(CliArgs cli)
    {
        string installDir = Path.GetDirectoryName(Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the uninstaller's own path."))!;
        string? dataRoot = UserEnvironment.GetDataRoot();
        string dataDir = dataRoot ?? InstallPaths.DefaultDataDir;

        // ── Confirmations up front ────────────────────────────────────────────────
        bool removeData = false;
        if (!cli.AssumeYes)
        {
            bool proceed = MessageDialog.Confirm(nint.Zero,
                $"Remove Deckle?\n\nThis deletes the app folder:\n{installDir}",
                "Uninstall Deckle");
            if (!proceed) return 0;

            // Models are the expensive thing to lose — re-downloading is gigabytes —
            // so keeping them is the default (No), stated plainly.
            if (Directory.Exists(dataDir))
                removeData = MessageDialog.Confirm(nint.Zero,
                    $"Also delete Deckle's data folder?\n\n{dataDir}\n\n" +
                    "This holds your settings and the downloaded speech models (several gigabytes). " +
                    "Choose No to keep them.",
                    "Delete data and models");
        }

        // A running Deckle keeps its image locked, so the scheduled folder delete would
        // leave binaries behind. Detection skips this very process; no retry loop — a
        // silent stub can't usefully wait, so it asks the user to close it and re-run.
        string[] running = RunningProcesses.FromFolder(installDir);
        if (running.Length > 0)
        {
            MessageDialog.Error(nint.Zero,
                $"Deckle is still running ({string.Join(", ", running)}).\n\n" +
                "Close it, then run the uninstaller again.");
            return 1;
        }

        // ── Removal, under a marquee window ───────────────────────────────────────
        var window = new ProgressWindow("Uninstall Deckle");
        Task worker = Task.Run(() =>
        {
            try
            {
                window.ReportMarquee("Removing Deckle…");
                Shortcut.RemoveStartMenu("Deckle");
                UninstallEntry.Remove();
                if (removeData) TryDeleteTree(dataDir);
                if (dataRoot is not null) UserEnvironment.ClearDataRoot();
            }
            catch { /* best-effort removal — nothing to abort onto */ }
            finally { window.RequestClose(); }
        });

        window.Show();
        window.RunMessageLoop();
        worker.GetAwaiter().GetResult();

        // Last, with the window gone and this process about to exit: schedule the
        // delete of the install folder, this exe included.
        ScheduleFolderDeletion(installDir);
        return 0;
    }

    // Spawns a detached cmd that waits ~1 s for this process to release its image,
    // then recursively removes the install folder (this exe included). ping is the
    // universally-present delay primitive.
    //
    // The path is interpolated into a cmd.exe command line, so any cmd metacharacter
    // it carries (" & | < > ^ %) would break out of the quoting — botching the
    // uninstall, and in theory appending a trailing command. ProcessStartInfo's
    // ArgumentList can't help: it quotes with the MSVCRT rules, which cmd.exe's /c
    // parser does not follow. The path is our own running exe's directory, never an
    // adversarial input, so a legitimate install path never holds those characters;
    // we validate and, on the (pathological) miss, fall back to an in-process delete
    // that clears everything except the still-locked exe rather than emit an unsafe
    // command line.
    private static void ScheduleFolderDeletion(string installDir)
    {
        if (installDir.IndexOfAny(CmdMetacharacters) >= 0)
        {
            TryDeleteTree(installDir);
            return;
        }

        var psi = new ProcessStartInfo("cmd.exe",
            $"/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{installDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    // Characters cmd.exe treats specially on a /c command line (quotes plus the
    // redirection/pipe/escape/expansion metacharacters).
    private static readonly char[] CmdMetacharacters = { '"', '&', '|', '<', '>', '^', '%' };

    private static void TryDeleteTree(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
