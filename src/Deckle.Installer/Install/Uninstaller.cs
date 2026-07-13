using System.Diagnostics;
using Deckle.Install;

namespace Deckle.Installer;

// ── Uninstaller ───────────────────────────────────────────────────────────────
//
// Reached when the installed copy is re-invoked with --uninstall (the
// UninstallString registered in Installed apps). It runs from inside the install
// folder, so the install location is simply this exe's directory. Same grammar as
// the install: a recap of what goes, every question up front, then an unattended
// run.
//
// Removal order: deregister first (shortcut, Installed-apps key), optionally drop
// the data folder, then schedule the binaries — including this running exe — for
// deletion. A process can't delete its own image, so a detached cmd waits for exit
// and removes the folder; the "Press Enter to close" hold therefore happens here,
// BEFORE scheduling, so the delayed delete never races a user still reading the
// console. Models are preserved unless the user explicitly opts in: re-downloading
// 3 GB is not something to do silently.
internal static class Uninstaller
{
    public static Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        ConsoleUi.Banner("Uninstaller");

        string installDir = Path.GetDirectoryName(Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the uninstaller's own path."))!;
        string? dataRoot = UserEnvironment.GetDataRoot();
        string dataDir = dataRoot ?? InstallPaths.DefaultDataDir;

        // ── Recap + questions, all up front ───────────────────────────────────────
        ConsoleUi.Headline("Deckle — ready to remove");
        Console.WriteLine();
        ConsoleUi.Row("App", installDir);
        ConsoleUi.Row("Data", dataDir);
        ConsoleUi.RowNote("models and settings — kept unless you opt in below");
        Console.WriteLine();

        bool interactive = !cli.AssumeYes && !Console.IsInputRedirected;
        if (interactive && !ConsoleUi.Confirm("Remove Deckle?", defaultYes: true))
        {
            ConsoleUi.Info("Cancelled.");
            ConsoleUi.HoldOpen();
            return Task.FromResult(0);
        }
        bool removeData = interactive
            && Directory.Exists(dataDir)
            && ConsoleUi.Confirm("Also delete data and models?", defaultYes: false);

        // A running Deckle keeps its image locked — the scheduled folder delete
        // would leave binaries behind. Same gate as the install side; the running
        // uninstaller itself is skipped by the detection.
        while (true)
        {
            string[] running = RunningProcesses.FromFolder(installDir);
            if (running.Length == 0) break;
            string names = string.Join(", ", running);
            if (!interactive)
            {
                ConsoleUi.Error($"Deckle is running ({names}) — close it and re-run the uninstaller.");
                return Task.FromResult(1);
            }
            ConsoleUi.Warn($"Deckle is running ({names}) — close it, then press Enter to retry.");
            ConsoleUi.WaitKey(ConsoleKey.Enter);
        }

        // ── Unattended run ────────────────────────────────────────────────────────
        ConsoleUi.Phase("Removing");
        Shortcut.RemoveStartMenu("Deckle");
        UninstallEntry.Remove();
        ConsoleUi.Ok("Start Menu · Installed apps");

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

        Console.WriteLine();
        ConsoleUi.Success("Deckle removed.");
        if (interactive) ConsoleUi.HoldOpen(); // must precede the self-delete schedule
        ScheduleFolderDeletion(installDir);
        return Task.FromResult(0);
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
            ConsoleUi.Warn($"install path holds shell metacharacters, skipping delayed delete: {installDir}");
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
        catch (Exception ex) { ConsoleUi.Warn($"could not delete {dir}: {ex.Message}"); }
    }
}
