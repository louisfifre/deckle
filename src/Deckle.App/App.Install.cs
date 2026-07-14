using Deckle.Core;
using Deckle.Install;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

// ── Install mode ─────────────────────────────────────────────────────────────
//
// The wizard as installer — the web-installer pattern. The stub downloads the
// app payload into a temp folder and launches it with --install; that temp
// process runs the wizard's front half (Modules → Folders → Choices) and ends
// in the Deploy step, which places the binaries, integrates, and spawns the
// INSTALLED Deckle.exe with --install-continue. That second process runs the
// provisioning half (Installing → Summary) with an AppPaths that resolved the
// chosen data root — the whole reason for the two-phase split: AppPaths
// freezes its root at first touch, so the temp process can never provision
// into a custom root.
//
// Both phases skip the entire normal boot: no engines, no tray, no hotkeys —
// diagnostics and the module catalogue only. The user's actual first run is
// the plain launch at the end of the chain.
public partial class App
{
    // Routes an installer-mode launch. Returns true when OnLaunched must stop
    // here — one of the two phases owns the process.
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
            // The in-app updater's second leg — the NEW payload placing itself
            // over the live install. Lives in App.Update.cs beside the check.
            _ = RunUpdateApplyAsync(args);
            return true;
        }
        return false;
    }

    // Phase 1 — temp process. Known accepted edge: the probes behind the
    // estimate bars touch AppPaths, which creates the default-root folders
    // even when the user then picks a custom root. Cheap, empty, harmless.
    private async Task RunInstallWizardAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        AppModules.RegisterAll();

        string? stub    = ArgumentValue(args, "--stub");
        string? cleanup = ArgumentValue(args, "--cleanup");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail("wizard", stub ?? "", cleanup ?? "", "");

        // Prefill from a previous install when there is one — the same update
        // ergonomics the console stub had: folders point at the live install,
        // modules at the recorded choice. DECKLE_DATA_ROOT is a User variable,
        // so this process (launched by the stub, which inherited the user
        // environment) already resolves a custom root through AppPaths.
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

    // Phase 2 — installed process, spawned by Deploy with DECKLE_DATA_ROOT in
    // its environment block. Runs provisioning (Installing → Summary) against
    // the chosen root, cleans the stub's temp folder, then relaunches plain.
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
            SelectedModel = SpeechModels.WhisperModels.FirstOrDefault(m => m.Id == modelId)
                ?? SpeechModels.WhisperModels.FirstOrDefault(m =>
                    m.FileName == TranscriptionSettingsService.Instance.Current.Engine.Model)
                ?? SpeechModels.DefaultWhisperModel,
            SelectedModules = ModulePresence.Choice
                ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
        };

        // Nothing left to provision (asset-free selection, or a reinstall over
        // a data root that already has everything) — skip straight to the
        // first real run instead of opening a wizard with no work to show.
        bool ok = true;
        if (InstallPlan.HasPendingWork(context))
        {
            var setup = new SetupWindow(context);
            setup.Body.Navigate(typeof(InstallingPage), setup);
            setup.Activate();
            ok = await setup.Completion;
        }

        // The extraction the temp process ran from — its wizard exited when it
        // spawned us, so the tree is deletable now. Best-effort: a leftover
        // temp folder is not worth failing an otherwise complete install.
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

    // `--flag value` argument extraction, null when the flag (or its value) is
    // absent.
    private static string? ArgumentValue(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
