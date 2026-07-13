using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Install;
using Deckle.Modules;

namespace Deckle.Setup;

// ── DeployPage ───────────────────────────────────────────────────────────────
//
// Install mode's terminal step in the temp process: place the binaries and
// integrate, then hand the rest of the wizard to the installed copy. The split
// exists because AppPaths froze on the default data root the moment this
// process touched it — provisioning (models, natives) must run in a process
// whose AppPaths resolved the CHOSEN root, and that can only be the installed
// Deckle.exe launched with DECKLE_DATA_ROOT in its environment.
//
// Sequence: gate on a running Deckle (binaries are locked while their image
// runs) → copy the extracted payload into the app folder → integrate (Start
// Menu shortcut, Installed-apps entry with the stub copied in as uninstaller,
// DECKLE_DATA_ROOT set/cleared, presence.json written into the chosen data
// root) → spawn `Deckle.exe --install-continue` → Complete. Failures stay on
// this page with Retry — nothing before the spawn is irreversible.
public sealed partial class DeployPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;
    private DispatcherQueue? _dispatcher;

    public DeployPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup      = setup;
        _context    = setup.Context;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Deploy"),
            Loc.Get("Setup_StepSubtitle_Deploy"));
        setup.SetBackEnabled(false);
        setup.SetNextEnabled(false);
        setup.SetNextVisible(false);
        setup.SetCancelVisible(false);

        RetryButton.Content = Loc.Get("Setup_Deploy_Retry");

        _ = DeployAsync();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        DeployProgress.Visibility = Visibility.Visible;
        _ = DeployAsync();
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task DeployAsync()
    {
        if (_setup is null || _context is null) return;
        var context = _context;

        long startTicks = Environment.TickCount64;
        string step = "gate";
        try
        {
            // Running-process gate first: Windows locks a running image, so a
            // live Deckle in either the target folder or a previous install
            // location blocks the copy. Surfaced as a retryable state, not a
            // failure — the user closes the app and clicks Retry.
            SetStatus(Loc.Get("Setup_Deploy_Checking"));
            string[] running = await Task.Run(() => FindRunning(context));
            if (running.Length > 0)
            {
                DeckleSetupSource.Log.DeployBlockedByRunningApp();
                ShowBlocked(Loc.Get("Setup_Deploy_AppRunning"));
                return;
            }

            step = "copy";
            SetStatus(Loc.Get("Setup_Deploy_Copying"));
            long copiedBytes = await Task.Run(() => CopyPayload(context));

            step = "integrate";
            SetStatus(Loc.Get("Setup_Deploy_Integrating"));
            await Task.Run(() => Integrate(context, copiedBytes));

            step = "launch";
            SetStatus(Loc.Get("Setup_Deploy_Launching"));
            LaunchInstalled(context);

            DeckleSetupSource.Log.DeployCompleted();
            DeckleSetupSource.Log.DeployCompletedDetail(
                context.InstallDirectory, context.DataDirectory,
                copiedBytes, Environment.TickCount64 - startTicks);

            _setup.Complete(true);
        }
        catch (Exception ex)
        {
            DeckleSetupSource.Log.DeployFailed();
            DeckleSetupSource.Log.DeployFailedDetail(step, $"{ex.GetType().Name}: {ex.Message}");
            ShowBlocked(Loc.Format("Setup_Deploy_Failed_Format", ex.Message));
        }
    }

    // ── Steps (background thread) ─────────────────────────────────────────────

    private static string[] FindRunning(SetupContext context)
    {
        var names = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in RunningProcesses.FromFolder(context.InstallDirectory))
            names.Add(name);
        if (UninstallEntry.Read() is { } existing
            && !PathsEqual(existing.InstallDir, context.InstallDirectory))
        {
            foreach (string name in RunningProcesses.FromFolder(existing.InstallDir))
                names.Add(name);
        }
        return [.. names];
    }

    // Empties a previous install before copying, so files a newer version
    // renamed or dropped never linger beside the fresh payload — same guard as
    // the console flow had: only a folder actually holding Deckle.exe is
    // cleaned, which keeps a mistyped custom folder from being emptied. The
    // registered uninstaller is spared: on an update run the stub that
    // launched us IS that file (StubPath points at it), and Integrate copies
    // from StubPath — deleting it here would delete the copy's source.
    private static long CopyPayload(SetupContext context)
    {
        string source = Path.GetFullPath(context.SourceDirectory);
        string target = Path.GetFullPath(context.InstallDirectory);

        Directory.CreateDirectory(target);
        if (File.Exists(Path.Combine(target, "Deckle.exe")))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(target))
            {
                if (string.Equals(Path.GetFileName(entry), "Deckle-Installer.exe",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
        }

        long copied = 0;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string dest = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            copied += new FileInfo(dest).Length;
        }
        return copied;
    }

    private static void Integrate(SetupContext context, long installedBytes)
    {
        string appExe = Path.Combine(context.InstallDirectory, "Deckle.exe");

        Shortcut.CreateStartMenu(appExe, "Deckle", "Deckle");

        // The stub becomes the registered uninstaller, living in the install
        // folder. A dev launch of `--install` has no stub — then there is no
        // uninstaller to point at, so the Installed-apps entry is skipped
        // rather than registered broken.
        if (context.StubPath is { } stub && File.Exists(stub))
        {
            string uninstaller = Path.Combine(context.InstallDirectory, "Deckle-Installer.exe");
            if (!PathsEqual(stub, uninstaller)) File.Copy(stub, uninstaller, overwrite: true);
            UninstallEntry.Write(
                context.InstallDirectory, PayloadVersion(appExe), uninstaller, installedBytes);
        }

        // The variable exists only while the data folder is off the default:
        // set on a non-default choice, cleared when the user comes back to the
        // default (a stale variable would silently override that choice).
        if (!PathsEqual(context.DataDirectory, InstallPaths.DefaultDataDir))
            UserEnvironment.SetDataRoot(context.DataDirectory);
        else if (UserEnvironment.GetDataRoot() is not null)
            UserEnvironment.ClearDataRoot();

        // The presence choice goes into the CHOSEN data root — not through
        // ModulePresence.Save, whose path rides this temp process's AppPaths.
        PresenceFile.SaveTo(
            ModulePresence.FilePathUnder(context.DataDirectory),
            [.. context.SelectedModules]);
    }

    private void LaunchInstalled(SetupContext context)
    {
        string appExe = Path.Combine(context.InstallDirectory, "Deckle.exe");

        // UseShellExecute=false so the child's environment block is ours to
        // shape: the HKCU write above only reaches processes launched after a
        // WM_SETTINGCHANGE round-trip, while this child must resolve the
        // chosen root on its very first AppPaths touch.
        var psi = new ProcessStartInfo(appExe)
        {
            UseShellExecute  = false,
            WorkingDirectory = context.InstallDirectory,
        };
        psi.ArgumentList.Add("--install-continue");
        if (context.SelectedModules.Contains(ModuleIds.Transcription)
            && context.SelectedModel is { } model)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(model.Id);
        }
        if (context.CleanupDirectory is { } cleanup)
        {
            psi.ArgumentList.Add("--cleanup");
            psi.ArgumentList.Add(cleanup);
        }
        if (!PathsEqual(context.DataDirectory, InstallPaths.DefaultDataDir))
            psi.Environment[UserEnvironment.DataRootVariable] = context.DataDirectory;

        Process.Start(psi);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // The Installed-apps version comes from the payload itself — install mode
    // has no release tag in hand, the stub already resolved and verified it.
    // ProductVersion may carry a +commit suffix (informational version); the
    // registry entry keeps the bare number the next update compares against.
    private static string PayloadVersion(string appExe)
    {
        string? version = FileVersionInfo.GetVersionInfo(appExe).ProductVersion;
        if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
        int plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private void SetStatus(string text) =>
        _dispatcher?.TryEnqueue(() => StatusText.Text = text);

    private void ShowBlocked(string message)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            DeployProgress.Visibility = Visibility.Collapsed;
            ErrorBar.Message = message;
            ErrorBar.IsOpen  = true;
        });
    }
}
