using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Modules;
using Deckle.Playground;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;
using Deckle.Speech;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

public partial class App
{
    // Module names in the journal: the ModuleIds vocabulary for the pieces a
    // person can leave out, plain names for the shell pieces always composed.
    private const string SpeechModule            = "speech";
    private const string ForegroundWatcherModule = "foreground_watcher";
    private const string TaskbarCoverModule      = "taskbar_cover";
    private const string InputHostModule         = "input_host";
    private const string OrchestrationModule     = "orchestration";
    private const string MouseWheelModule        = "mouse_wheel";

    private const string SkipNotPresent          = "not_present";
    private const string SkipNativeRuntimeMissing = "native_runtime_missing";
    private const string SkipSpeechModelMissing  = "speech_model_missing";

    // ForegroundWatcher.Start returns false only when its foreground hook
    // could not be set; same spelling as the watcher's own reason.
    private const string RefuseForegroundHookFailed = "foreground_hook_failed";

    private void InitializeStartupModules(StartupContext context)
    {
        // Without the native runtime and a speech model the engine is not
        // composed and the rest of the app runs normally: provisioning happens
        // on demand from Settings, never as a hard gate that quits the app. The
        // WhisperBackend constructor installs the native log hook and would throw
        // DllNotFoundException without the runtime.
        string? engineSkip =
            !context.TranscriptionPresent         ? SkipNotPresent
            : !NativeRuntime.IsInstalled()        ? SkipNativeRuntimeMissing
            : !SpeechModels.IsAnyModelInstalled() ? SkipSpeechModelMissing
            : null;
        if (engineSkip is null)
        {
            context.Compose(ModuleIds.Transcription, () =>
            {
                var host = new AppTranscriptionEngineHost();
                var backend = new WhisperBackend(host);
                _engine = new TranscriptionEngine(
                    host,
                    backend,
                    recovery: new DictationRecoveryStore(AppPaths.DictationRecoveryFilePath));
            });
        }
        else
        {
            context.Skip(ModuleIds.Transcription, engineSkip);
        }

        // Constructed dormant: nothing calls Speak yet.
        context.Compose(SpeechModule, () =>
            _speechEngine = new SpeechEngine(new ChatterboxSpeechBackend()));

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
        // is logged by the watcher with its Win32 error and journaled here as a
        // refusal; the app runs on without foreground changes.
        context.ComposeOrRefuse(ForegroundWatcherModule, RefuseForegroundHookFailed, () =>
        {
            _foregroundWatcher = new ForegroundWatcher();
            return _foregroundWatcher.Start();
        });

        context.Compose(TaskbarCoverModule, InitializeTaskbarCover);

        // Created before its consumers, which reference-count it.
        context.Compose(InputHostModule, InitializeInputHost);

        // The native device and worker exist only while the master switch is on.
        if (context.PrecisionScrollPresent)
            context.Compose(ModuleIds.PrecisionScroll, InitializePrecisionScroll);
        else
            context.Skip(ModuleIds.PrecisionScroll, SkipNotPresent);

        // Always composed; idle (no WMI subscription, no enumeration) while no
        // entry is enabled.
        context.Compose(OrchestrationModule, InitializeOrchestration);

        if (context.RewritePresent)
            context.Compose(ModuleIds.Rewrite, InitializeParagraphRewrite);
        else
            context.Skip(ModuleIds.Rewrite, SkipNotPresent);

        context.Compose(MouseWheelModule, InitializeMouseWheel);

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
    }
}
