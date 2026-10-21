using Deckle.Core;
using Deckle.Install;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

// Install mode, the web-installer pattern. The stub launches the downloaded
// payload from a temp folder with --install; that process runs the wizard's
// front half, and its Deploy step places the binaries, integrates, and spawns
// the installed Deckle.exe with --install-continue, which provisions. The split
// exists because AppPaths freezes its data root at first touch: only the
// installed process, launched with DECKLE_DATA_ROOT in its environment, can
// provision into a custom root. Both phases skip the normal boot.
public partial class App
{
    // The flags of the modes that coordinate overlapping processes by design.
    // Program's crash-dump relaunch checks them: the process that spawned one
    // of these follows its PID, so it must not hand over to another.
    internal static readonly string[] TransactionalModeFlags =
        { "--install", "--install-continue", "--update-apply", "--relocate-data" };

    internal static bool IsTransactionalMode(string[] args)
        => Array.Exists(args, a => Array.IndexOf(TransactionalModeFlags, a) >= 0);

    // Mode names in the journal, the phases InstallModeEnteredDetail prints.
    private const string WizardMode       = "wizard";
    private const string ContinuationMode = "continuation";
    private const string UpdateApplyMode  = "update-apply";
    private const string RelocationMode   = "relocate";

    private static long _transactionalModeStarted;

    // True when one of these modes owns the process and OnLaunched must stop.
    private bool TryEnterInstallMode()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "--install") >= 0)
        {
            _ = RunTransactionalModeAsync(WizardMode, () => RunInstallWizardAsync(args));
            return true;
        }
        if (Array.IndexOf(args, "--install-continue") >= 0)
        {
            _ = RunTransactionalModeAsync(ContinuationMode, () => RunInstallContinuationAsync(args));
            return true;
        }
        if (Array.IndexOf(args, "--update-apply") >= 0)
        {
            // The updater's second leg: the new payload placing itself over the
            // live install.
            _ = RunTransactionalModeAsync(UpdateApplyMode, () => RunUpdateApplyAsync(args));
            return true;
        }
        if (Array.IndexOf(args, "--relocate-data") >= 0)
        {
            // The data-root move: a restarted process can copy the root it no
            // longer holds open.
            _ = RunTransactionalModeAsync(RelocationMode, () => RunDataRelocationAsync(args));
            return true;
        }
        return false;
    }

    // OnLaunched cannot await a mode, so a throw inside one would stay
    // unobserved and leave a windowless process that never exits. The net
    // logs it and ends the process: there is no resident app to fall back on.
    private static async Task RunTransactionalModeAsync(string mode, Func<Task> run)
    {
        _transactionalModeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.TransactionalModeFailed(
                mode, ex.GetType().Name, ex.Message, TransactionalModeElapsedMs());
            DeckleAppSource.Log.CrashStackTrace(ex.ToString());
            Environment.Exit(1);
        }
    }

    private static long TransactionalModeElapsedMs()
        => (long)System.Diagnostics.Stopwatch.GetElapsedTime(_transactionalModeStarted).TotalMilliseconds;

    // Written before Environment.Exit or the relaunch that ends the mode:
    // neither returns, so no finally would run after them.
    private static void ReportTransactionalModeExit(string mode, string outcome, string reason)
        => DeckleAppSource.Log.TransactionalModeExited(mode, outcome, reason, TransactionalModeElapsedMs());

    // Phase 1, the temp process. Accepted edge: the probes behind the estimate
    // bars touch AppPaths, which creates the default-root folders even when the
    // user then picks a custom root. Cheap, empty, harmless.
    private async Task RunInstallWizardAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        AppModules.RegisterAll();

        string? stub    = ArgumentValue(args, "--stub");
        string? cleanup = ArgumentValue(args, "--cleanup");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail(WizardMode, stub ?? "", cleanup ?? "", "");

        // Prefilled from a previous install when there is one. DECKLE_DATA_ROOT
        // is a User variable, so this process, launched by the stub with the user
        // environment, already resolves a custom root through AppPaths.
        UninstallEntry.ExistingInstall? existing = UninstallEntry.Read();
        string installDir = existing is not null && Directory.Exists(existing.InstallDir)
            ? existing.InstallDir
            : InstallPaths.DefaultInstallDir;

        var context = new SetupContext
        {
            InstallMode      = true,
            StubPath         = stub,
            CleanupDirectory = cleanup,
            InstallDirectory = installDir,
            DataDirectory    = UserEnvironment.GetDataRoot() ?? InstallPaths.DefaultDataDir,
            SelectedModel    = SpeechModels.DefaultWhisperModel,
            SelectedModules  = ModulePresence.Choice
                ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
        };

        var setup = new SetupWindow(context);
        setup.Body.Navigate(typeof(ModulesPage), setup);
        setup.Activate();

        // Success or cancel, this process is done: Deploy already spawned the
        // installed copy on success, and a cancel means nothing was placed.
        bool ok = await setup.Completion;
        ReportTransactionalModeExit(WizardMode, ok ? "completed" : "cancelled", "none");
        Environment.Exit(0);
    }

    // Phase 2, the installed process spawned by Deploy.
    private async Task RunInstallContinuationAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        AppModules.RegisterAll();

        string? modelId = ArgumentValue(args, "--model");
        string? cleanup = ArgumentValue(args, "--cleanup");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail(ContinuationMode, "", cleanup ?? "", modelId ?? "");

        var context = new SetupContext
        {
            ResumesInstall = true,
            // Without --model (an update's continuation), the configured model
            // from settings wins over the catalog default: the plan must ask
            // for what this install actually uses, not schedule the default's
            // download onto an install that never chose it.
            SelectedModel = SpeechModelResolver.ResolveSelection(
                modelId,
                TranscriptionSettingsService.Instance.Current.Engine.Model),
            SelectedModules = ModulePresence.Choice
                ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
        };

        // Nothing left to provision skips straight to the first real run rather
        // than opening a wizard with no work to show.
        bool ok = true;
        bool pendingWork = InstallPlan.HasPendingWork(context);
        if (pendingWork)
        {
            var setup = new SetupWindow(context);
            setup.Body.Navigate(typeof(InstallingPage), setup);
            setup.Activate();
            ok = await setup.Completion;
        }

        // The temp process's extraction, deletable now that its wizard has
        // exited. Best-effort: a leftover temp folder is not worth failing an
        // otherwise complete install.
        if (cleanup is not null)
        {
            try { Directory.Delete(cleanup, recursive: true); }
            catch (Exception ex)
            {
                DeckleAppSource.Log.InstallTempCleanupFailed(cleanup, ex.GetType().Name, ex.Message);
            }
        }

        // A cancelled provisioning still leaves a complete install — the app
        // simply doesn't start now; the Start Menu entry is the way back in.
        ReportTransactionalModeExit(
            ContinuationMode,
            ok ? "completed" : "cancelled",
            pendingWork ? "none" : "nothing_to_provision");
        if (ok) RestartViaShellExecute();
        Environment.Exit(0);
    }

    private static string? ArgumentValue(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
