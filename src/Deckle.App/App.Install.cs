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
    // True when one of these modes owns the process and OnLaunched must stop.
    private bool TryEnterInstallMode()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "--install") >= 0)
        {
            _ = RunInstallWizardAsync(args);
            return true;
        }
        if (Array.IndexOf(args, "--install-continue") >= 0)
        {
            _ = RunInstallContinuationAsync(args);
            return true;
        }
        if (Array.IndexOf(args, "--update-apply") >= 0)
        {
            // The updater's second leg: the new payload placing itself over the
            // live install.
            _ = RunUpdateApplyAsync(args);
            return true;
        }
        if (Array.IndexOf(args, "--relocate-data") >= 0)
        {
            // The data-root move: a restarted process can copy the root it no
            // longer holds open.
            _ = RunDataRelocationAsync(args);
            return true;
        }
        return false;
    }

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
        DeckleAppSource.Log.InstallModeEnteredDetail("wizard", stub ?? "", cleanup ?? "", "");

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
        await setup.Completion;
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
        DeckleAppSource.Log.InstallModeEnteredDetail("continuation", "", cleanup ?? "", modelId ?? "");

        var context = new SetupContext
        {
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
        if (InstallPlan.HasPendingWork(context))
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
                DeckleAppSource.Log.ShutdownWarning();
                DeckleAppSource.Log.ShutdownWarningDetail("install temp cleanup: " + ex.Message);
            }
        }

        // A cancelled provisioning still leaves a complete install — the app
        // simply doesn't start now; the Start Menu entry is the way back in.
        if (ok) RestartViaShellExecute();
        Environment.Exit(0);
    }

    private static string? ArgumentValue(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
