using System.Diagnostics;

using Deckle.Core;
using Deckle.Setup;

namespace Deckle.App;

// ── Relocate mode ────────────────────────────────────────────────────────────
//
// The data-root move's App-side pieces, third sibling of App.Install.cs and
// App.Update.cs. The running app cannot copy its own live root — sinks and
// engines hold files open under it — so Settings' validated target restarts
// the process into --relocate-data, whose only job is RelocatePage: copy,
// flip DECKLE_DATA_ROOT, relaunch plain. The relaunched process, running on
// the NEW root, removes the origin (HandleDataRootCleanup) — the last step of
// the transaction runs only once nothing holds the old tree.
public partial class App
{
    // Settings → restart into the relocate process. The child inherits this
    // process's environment, so its AppPaths still resolves the OLD root —
    // exactly what it must copy from.
    private void StartDataRelocation(string target)
    {
        string exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute  = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };
        psi.ArgumentList.Add("--relocate-data");
        psi.ArgumentList.Add("--to");
        psi.ArgumentList.Add(target);
        Process.Start(psi);
        QuitApp();
    }

    // The dedicated process: normal boot short-circuited, RelocatePage alone.
    private async Task RunDataRelocationAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);

        string? target = ArgumentValue(args, "--to");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail("relocate", "", "", "");

        // The caller (Settings pre-flight) already validated; re-refusing here
        // is the guard against a hand-typed command line. An invalid target
        // must not leave the user app-less — relaunch plain and bow out.
        if (!IsValidRelocationTarget(target))
        {
            RestartViaShellExecute();
            return;
        }

        var context = new SetupContext
        {
            RelocateMode  = true,
            DataDirectory = target!,
        };
        var setup = new Deckle.Setup.SetupWindow(context);
        setup.Body.Navigate(typeof(RelocatePage), setup);
        setup.Activate();

        // Success: the page relaunched the plain app on the new root. Cancel
        // or a closed window: the old root never stopped being live — relaunch
        // plain so the move degrades into a no-op, not a vanished app.
        bool ok = await setup.Completion;
        if (!ok) RestartViaShellExecute();
        Environment.Exit(0);
    }

    private static bool IsValidRelocationTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathRooted(target)) return false;

        string source = Path.GetFullPath(AppPaths.UserDataRoot).TrimEnd('\\');
        string full   = Path.GetFullPath(target).TrimEnd('\\');

        if (string.Equals(full, source, StringComparison.OrdinalIgnoreCase)) return false;
        if (full.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        if (source.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        // Only an empty or not-yet-existing folder: the post-move cleanup of a
        // failed copy deletes the target tree, which must never hold foreign files.
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) return false;

        return true;
    }

    // Plain boot, on the NEW root: remove the origin a relocation left behind.
    // Guarded three ways — never the live root, only an existing folder, and
    // only one that carries a data root's shape — so a mangled argument can
    // never delete something else. Failure-only logging, same posture as the
    // install continuation's temp cleanup.
    private static void HandleDataRootCleanup(string[] cliArgs)
    {
        int idx = Array.IndexOf(cliArgs, "--cleanup-data");
        if (idx < 0 || idx + 1 >= cliArgs.Length) return;
        string oldRoot = cliArgs[idx + 1];

        _ = Task.Run(() =>
        {
            try
            {
                string old     = Path.GetFullPath(oldRoot).TrimEnd('\\');
                string current = Path.GetFullPath(AppPaths.UserDataRoot).TrimEnd('\\');
                if (string.Equals(old, current, StringComparison.OrdinalIgnoreCase)) return;
                if (!Directory.Exists(old)) return;
                if (!File.Exists(Path.Combine(old, "settings.json"))
                    && !Directory.Exists(Path.Combine(old, "modules"))) return;

                Directory.Delete(old, recursive: true);
            }
            catch (Exception ex)
            {
                DeckleAppSource.Log.ShutdownWarning();
                DeckleAppSource.Log.ShutdownWarningDetail("relocated data root cleanup: " + ex.Message);
            }
        });
    }
}
