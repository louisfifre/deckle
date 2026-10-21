using System.Diagnostics;

using Deckle.Core;
using Deckle.Setup;

namespace Deckle.App;

// The running app cannot copy its own live root (sinks and engines hold files
// open under it), so Settings restarts the process into --relocate-data, which
// copies, flips DECKLE_DATA_ROOT and relaunches plain. The relaunched process,
// on the new root, removes the origin once nothing holds the old tree.
public partial class App
{
    // The child inherits this process's environment, so its AppPaths still
    // resolves the old root: exactly what it must copy from.
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

    private async Task RunDataRelocationAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);

        string? target = ArgumentValue(args, "--to");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail(RelocationMode, "", "", "");

        // The caller (Settings pre-flight) already validated; re-refusing here
        // is the guard against a hand-typed command line. An invalid target
        // must not leave the user app-less — relaunch plain and bow out.
        if (!IsValidRelocationTarget(target))
        {
            ReportTransactionalModeExit(RelocationMode, "refused", "invalid_target");
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
        ReportTransactionalModeExit(RelocationMode, ok ? "completed" : "cancelled", "none");
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

    // Guarded three ways (never the live root, only an existing folder, only one
    // shaped like a data root) so a mangled argument can never delete anything else.
    private static void HandleDataRootCleanup(string[] cliArgs)
    {
        int idx = Array.IndexOf(cliArgs, "--cleanup-data");
        if (idx < 0 || idx + 1 >= cliArgs.Length) return;
        string oldRoot = cliArgs[idx + 1];

        _ = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                string old     = Path.GetFullPath(oldRoot).TrimEnd('\\');
                string current = Path.GetFullPath(AppPaths.UserDataRoot).TrimEnd('\\');
                string? skip =
                    string.Equals(old, current, StringComparison.OrdinalIgnoreCase) ? "is_live_root"
                    : !Directory.Exists(old) ? "not_found"
                    : !File.Exists(Path.Combine(old, "settings.json"))
                      && !Directory.Exists(Path.Combine(old, "modules")) ? "not_a_data_root"
                    : null;
                if (skip is not null)
                {
                    DeckleAppSource.Log.DataRootCleanupFinished("skipped", skip, stopwatch.ElapsedMilliseconds);
                    return;
                }

                DeckleAppSource.Log.DataRootCleanupStarting(old);
                Directory.Delete(old, recursive: true);
                DeckleAppSource.Log.DataRootCleanupFinished("completed", "none", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                DeckleAppSource.Log.DataRootCleanupFailed(ex.GetType().Name, ex.Message, stopwatch.ElapsedMilliseconds);
            }
        });
    }
}
