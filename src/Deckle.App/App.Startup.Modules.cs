using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;
using Deckle.Transcription.Parakeet;

namespace Deckle.App;

public partial class App
{
    // Module names in the log: the ModuleIds vocabulary for the pieces a
    // person can leave out, plain names for the shell pieces always composed.
    private const string ForegroundWatcherModule = "foreground_watcher";
    private const string TaskbarCoverModule      = "taskbar_cover";
    private const string TaskbarModule           = "taskbar";
    private const string InputHostModule         = "input_host";
    private const string TaskbarWindowsKeyModule = "taskbar_windows_key";

    private const string SkipNotPresent          = "not_present";
    private const string SkipSpeechModelMissing  = "speech_model_missing";

    // ForegroundWatcher.Start returns false only when its foreground hook
    // could not be set; same spelling as the watcher's own reason.
    private const string RefuseForegroundHookFailed = "foreground_hook_failed";

    // Whether the model folder holds what the loader opens at the chosen precision; the folder
    // Deckle provisions is also held to its pinned file sizes (ParakeetModelCatalog.IsPresent).
    private static bool ParakeetModelPresent()
    {
        TranscriptionSettingsService settings = TranscriptionSettingsService.Instance;
        EngineSettings engine = settings.Current.Engine;
        return ParakeetModelCatalog.IsPresent(
            engine.ParakeetModelDirectory, settings.ResolveModelsDirectory(), engine.ParakeetEncoder);
    }

    private void InitializeStartupModules(StartupContext context)
    {
        // Without a speech model the engine is not composed and the rest of the
        // app runs normally: provisioning happens through the setup window or
        // from Settings, never as a hard gate that quits the app. Parakeet needs
        // no native runtime of ours (ONNX Runtime ships inside the app), only its
        // model folder.
        string? engineSkip =
            !context.TranscriptionPresent ? SkipNotPresent
            : ParakeetModelPresent()      ? null
            : SkipSpeechModelMissing;
        if (engineSkip is null)
        {
            context.Compose(ModuleIds.Transcription, () =>
            {
                var host = new AppTranscriptionEngineHost();
                _engine = new TranscriptionEngine(
                    host,
                    new ParakeetBackend(host),
                    recovery: new DictationRecoveryStore(AppPaths.DictationRecoveryFilePath),
                    takeAudio: new TakeAudioRecoveryStore(AppPaths.TakeAudioRecoveryFilePath));
            });
        }
        else
        {
            context.Skip(ModuleIds.Transcription, engineSkip);
            // A present module that cannot run is the person's to fix. The
            // incident opens the Transcription page, whose setup bar starts the
            // download, so startup opens no window of its own at each launch.
            // Nothing closes it in this process: the engine is composed only at
            // start, so what fixes it is the restart that follows provisioning.
            if (engineSkip != SkipNotPresent)
                UserIncidentRegistry.Instance.Open(UserIncidents.SpeechModel, engineSkip);
        }
        // Construction does no I/O; the Ambient settings observer starts and
        // stops the engine.
        if (context.AmbientPresent)
            context.Compose(ModuleIds.Ambient, () =>
                _ambientEngine = new AmbientEngine(new AppAmbientEngineHost()));
        else
            context.Skip(ModuleIds.Ambient, SkipNotPresent);

        if (context.TrackpadPresent)
            context.Compose(ModuleIds.Trackpad, InitializeTrackpad);
        else
            context.Skip(ModuleIds.Trackpad, SkipNotPresent);

        // Read by the taskbar cover and the apps and games runtime, so it must
        // exist before them. Its WinEvent hooks deliver on the thread that starts
        // it: the UI thread, which pumps once OnLaunched returns. A failed start
        // is logged by the watcher with its Win32 error and recorded here as a
        // refusal; the app runs on without foreground changes.
        context.ComposeOrRefuse(ForegroundWatcherModule, RefuseForegroundHookFailed, () =>
        {
            _foregroundWatcher = new ForegroundWatcher();
            return _foregroundWatcher.Start();
        });

        context.Compose(TaskbarCoverModule, InitializeTaskbarCover);

        // Writes the user's own notify icon keys (tray icons promoted when the
        // setting is on) and, when a customization is on, injects the native
        // engine into explorer and the Start menu host. Every setting is off by
        // default, so a fresh install reaches into neither.
        context.Compose(TaskbarModule, InitializeTaskbar);

        // Created before its consumers, which reference-count it.
        context.Compose(InputHostModule, InitializeInputHost);

        // The native device and worker exist only while the master switch is on.
        if (context.PrecisionScrollPresent)
            context.Compose(ModuleIds.PrecisionScroll, InitializePrecisionScroll);
        else
            context.Skip(ModuleIds.PrecisionScroll, SkipNotPresent);

        // Idle (no WMI subscription, no enumeration) while no entry is enabled.
        if (context.OrchestrationPresent)
            context.Compose(ModuleIds.Orchestration, InitializeOrchestration);
        else
            context.Skip(ModuleIds.Orchestration, SkipNotPresent);

        if (context.RewritePresent)
            context.Compose(ModuleIds.Rewrite, InitializeParagraphRewrite);
        else
            context.Skip(ModuleIds.Rewrite, SkipNotPresent);

        // Listens to the shared host only while its setting is on, which it is
        // not by default. Composed after the host it holds.
        context.Compose(TaskbarWindowsKeyModule, InitializeTaskbarWindowsKey);

        if (context.AutocorrectPresent)
            context.Compose(ModuleIds.Autocorrect, InitializeAutocorrect);
        else
            context.Skip(ModuleIds.Autocorrect, SkipNotPresent);

        // Fire-and-forget: readiness runs off the boot path, so "started" marks
        // the dispatch; the Anytype provider reports the backend's readiness.
        if (context.AnytypePresent)
            context.Compose(ModuleIds.Anytype, StartAnytypeRuntime);
        else
            context.Skip(ModuleIds.Anytype, SkipNotPresent);

        // The contributed modules (PublishedComposition), none today:
        // nothing runs at start, the tray builds the window the first time it
        // opens it. Logged so the boot names the module.
        foreach (Contributions.IContribution contribution in PublishedComposition.All)
        {
            if (contribution.Descriptor is not { } module) continue;
            if (ModulePresence.IsPresent(module.Id))
                context.Compose(module.Id, () => { });
            else
                context.Skip(module.Id, SkipNotPresent);
        }
    }
}
