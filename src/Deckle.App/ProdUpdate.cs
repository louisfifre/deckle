using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

// The prod copy is the build of main the maintainer runs every day, deployed
// by scripts/deploy-prod.ps1 into a folder of its own, so that agents build in
// their worktrees while it runs and nobody has to build main by hand. Its root
// holds four entries:
//
//   current\           the build that runs; autostart and shortcuts point here
//   next\              a complete build waiting to take current\'s place
//   previous\          the build current\ replaced, kept for a rollback
//   apply-update.ps1   the helper that swaps them, Prod\apply-update.ps1
//                      of the staged build
//
// A running Deckle holds every file of its folder open, so next\ can take
// current\'s place only once no Deckle runs from it, and no process can do that
// from inside the folder it renames. Every start of current\Deckle.exe is
// therefore the moment an update applies: when next\ is staged, this process
// starts the helper and exits before WinUI, the mutex or the crash supervisor
// exist. The helper waits for every Deckle of current\ to exit, renames the
// folders and starts the new current\Deckle.exe with the same arguments. A
// restart from the tray, a restart after a settings change and the Windows
// autostart all start current\Deckle.exe, so each of them applies a staged
// update without knowing about it. A crash relaunch does not: the supervisor
// that relaunches still runs from current\, so its child runs the build in place.
//
// The path never changes, which is why this layout rather than one folder per
// version: the autostart entry, the elevated startup task and the shell's own
// shortcuts all compare against Environment.ProcessPath.
internal static class ProdUpdate
{
    internal const string CurrentFolderName = "current";
    internal const string NextFolderName = "next";
    internal const string ApplyScriptName = "apply-update.ps1";

    // Set by the helper on the start that follows a failed swap, so that start
    // runs the build in place instead of handing off again in a loop. Removed
    // here at once, so the next restart retries the swap.
    internal const string NoUpdateVariable = "DECKLE_PROD_NO_UPDATE";

    private const string NoteFileName = "prod.txt";

    // True when the helper took over and Main must return; false when this
    // process is the Deckle that runs.
    internal static bool TryHandOff(string[] args)
    {
        bool skip = Environment.GetEnvironmentVariable(NoUpdateVariable) is not null;
        Environment.SetEnvironmentVariable(NoUpdateVariable, null);
        if (skip) return false;

        // A supervised child, a transactional mode (install, update, relocation)
        // and the self-check all run on a decision their launcher already took.
        if (Environment.GetEnvironmentVariable(CrashRelaunchPolicy.SupervisedVariable) is not null) return false;
        if (App.IsTransactionalMode(args) || App.IsSelfCheckMode(args)) return false;

        if (FindPendingRoot(AppContext.BaseDirectory) is not { } root) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                // Windows PowerShell ships with every Windows 11; PowerShell 7
                // is a developer install the prod copy does not require.
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            foreach (string arg in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
                "-File", Path.Combine(root, ApplyScriptName), "-Root", root,
            })
                psi.ArgumentList.Add(arg);
            foreach (string arg in args) psi.ArgumentList.Add(arg);

            using Process helper = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned no process.");
            Note($"update handed off | helper_pid={helper.Id} | root={root}");
            return true;
        }
        catch (Exception ex)
        {
            // The staged build stays where it is and the next start tries
            // again; this one runs the build already in place.
            Note($"update hand-off failed | ex_type={ex.GetType().Name} | ex_message={ex.Message} | outcome=runs_current");
            return false;
        }
    }

    // The prod copy root when baseDirectory is its current\ folder and a
    // staged build waits in next\ with the helper beside it; null otherwise,
    // which is every checkout, worktree and installed build.
    internal static string? FindPendingRoot(string baseDirectory)
    {
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!string.Equals(Path.GetFileName(folder), CurrentFolderName, StringComparison.OrdinalIgnoreCase))
            return null;

        string? root = Path.GetDirectoryName(folder);
        if (root is null) return null;

        bool staged = File.Exists(Path.Combine(root, NextFolderName, "Deckle.exe"));
        bool helper = File.Exists(Path.Combine(root, ApplyScriptName));
        return staged && helper ? root : null;
    }

    // The diagnostics stack does not exist yet, so like the crash supervisor
    // this leaves a capped line in the diagnostics folder; the helper's own
    // steps go to apply-update.log at the prod copy root.
    private static void Note(string line)
        => NoteFile.Append(
            Path.Combine(AppPaths.DiagnosticsDirectory, NoteFileName),
            $"pid={Environment.ProcessId} | {line}",
            DateTimeOffset.Now);
}
