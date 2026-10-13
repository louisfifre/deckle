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
    private void InitializeStartupModules(StartupContext context)
    {
        // Without the native runtime and a speech model the engine is not
        // composed and the rest of the app runs normally: provisioning happens
        // on demand from Settings, never as a hard gate that quits the app. The
        // WhisperBackend constructor installs the native log hook and would throw
        // DllNotFoundException without the runtime.
        bool speechReady = context.TranscriptionPresent
            && NativeRuntime.IsInstalled() && SpeechModels.IsAnyModelInstalled();
        if (speechReady)
        {
            var host = new AppTranscriptionEngineHost();
            var backend = new WhisperBackend(host);
            _engine = new TranscriptionEngine(host, backend);
            context.Milestone("engine");
        }
        else
        {
            if (context.TraceEnabled)
                context.Milestone($"engine_skipped present={context.TranscriptionPresent} native={NativeRuntime.IsInstalled()} model={SpeechModels.IsAnyModelInstalled()}");
        }

        // Constructed dormant: nothing calls Speak yet.
        _speechEngine = new SpeechEngine(new ChatterboxSpeechBackend());
        context.Milestone("speech_engine");

        // Construction does no I/O; the Ambient settings observer starts and
        // stops the engine.
        if (context.AmbientPresent)
        {
            _ambientEngine = new AmbientEngine(new AppAmbientEngineHost());
            context.Milestone("ambient_engine");
        }
        else
        {
            context.Milestone("ambient_engine_skipped");
        }

        if (context.TrackpadPresent)
        {
            InitializeTrackpad();
            context.Milestone("trackpad");
        }
        else
        {
            context.Milestone("trackpad_skipped");
        }

        // Read by the taskbar cover and the apps and games runtime, so it must
        // exist before them. Its WinEvent hooks deliver on the thread that starts
        // it: the UI thread, which pumps once OnLaunched returns. A failed start
        // is logged by the watcher; the app runs on without foreground changes.
        _foregroundWatcher = new ForegroundWatcher();
        _foregroundWatcher.Start();
        context.Milestone("foreground_watcher");

        InitializeTaskbarCover();
        context.Milestone("taskbar_cover");

        // Created before its consumers, which reference-count it.
        InitializeInputHost();
        context.Milestone("input_host");

        // The native device and worker exist only while the master switch is on.
        InitializePrecisionScroll();
        context.Milestone("precision_scroll");

        // Always composed; idle (no WMI subscription, no enumeration) while no
        // entry is enabled.
        InitializeOrchestration();
        context.Milestone("orchestration");

        if (context.RewritePresent)
        {
            InitializeParagraphRewrite();
            context.Milestone("paragraph_rewrite");
        }
        else
        {
            context.Milestone("paragraph_rewrite_skipped");
        }

        InitializeMouseWheel();
        context.Milestone("mouse_wheel");

        if (context.AutocorrectPresent)
        {
            InitializeAutocorrect();
            context.Milestone("autocorrect");
        }
        else
        {
            context.Milestone("autocorrect_skipped");
        }

        // Fire-and-forget: readiness runs off the boot path, so the milestone
        // marks dispatch, not readiness.
        if (context.AnytypePresent)
        {
            StartAnytypeRuntime();
            context.Milestone("anytype_backend");
        }
        else
        {
            context.Milestone("anytype_backend_skipped");
        }
    }
}

