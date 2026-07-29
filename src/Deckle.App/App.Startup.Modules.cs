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
        // Speech provisioning is decoupled from boot. Whisper is one module
        // among several: when its native runtime + a speech model aren't yet on
        // disk, the transcription engine is simply not composed and the rest of
        // the app runs its other modules normally. Provisioning happens on
        // demand from Settings › Dictation (SettingsHost.OpenSetupWizard, wired
        // below) — never a hard gate that quits the app on a failed download.
        //
        // The engine ctor loads the model immediately and would throw
        // DllNotFoundException without the native runtime, so its construction
        // is guarded by this check; the event wiring further down carries the
        // same guard.
        bool speechReady = context.TranscriptionPresent
            && NativeRuntime.IsInstalled() && SpeechModels.IsAnyModelInstalled();
        if (speechReady)
        {
            // Compose the engine with the Whisper backend — the App is the
            // composition root that knows which IAsrBackend to instantiate.
            // When a second backend ships (Voxtral), the choice surfaces in
            // Settings and gates the construction here.
            var host = new AppTranscriptionEngineHost();
            var backend = new WhisperBackend(host);
            _engine = new TranscriptionEngine(host, backend);
            context.Milestone("engine");
        }
        else
        {
            // Module unchecked, or no native runtime and/or model yet — the
            // dictation module stays dormant. Recorded as a boot milestone
            // (with the three readiness flags) so a support trace shows
            // exactly which part is missing.
            if (context.TraceEnabled)
                context.Milestone($"engine_skipped present={context.TranscriptionPresent} native={NativeRuntime.IsInstalled()} model={SpeechModels.IsAnyModelInstalled()}");
        }

        // Read-aloud (TTS) engine with the placeholder Chatterbox backend —
        // same composition-root posture as the transcription engine above.
        // Constructed dormant: no trigger is wired yet (the clipboard-read
        // demonstrator was removed). The real ONNX synthesis backend lands at
        // the next palier; the voice-assistant loop will drive it via Speak.
        _speechEngine = new SpeechEngine(new ChatterboxSpeechBackend());
        context.Milestone("speech_engine");

        // Canonical Ambient engine — owns its own ScreenCaptureService,
        // FrameSampler, HueBridgeClient and HueRestLightOutput at
        // StartAsync time. Construct is cheap, no I/O. Started / Stopped
        // by the AmbientSettings.Changed observer wired below. If the
        // user persisted Enabled=true from a previous session, the
        // pipeline boots automatically when the engine starts (fire-and-
        // forget Task so OnLaunched stays non-blocking).
        if (context.AmbientPresent)
        {
            _ambientEngine = new AmbientEngine(new AppAmbientEngineHost());
            // AmbientPage's NotPaired InfoBar action button needs to open
            // the Playground (where Hue pairing lives in V0). Lighting.
            // Ambient cannot reference Deckle, so the App fills the slot.
            AmbientEngine.OpenPlaygroundRequested = () => ShowPlaygroundLazy();
            context.Milestone("ambient_engine");
        }
        else
        {
            context.Milestone("ambient_engine_skipped");
        }

        // Trackpad module — Raw Input host + three-finger drag engine +
        // frame recorder, reconciled with the persisted module settings
        // (off by default: the host thread only spins up when the master
        // switch or the frame-recording diagnostic is on).
        if (context.TrackpadPresent)
        {
            InitializeTrackpad();
            context.Milestone("trackpad");
        }
        else
        {
            context.Milestone("trackpad_skipped");
        }

        // Taskbar cover module — dedicated band thread reconciled with the
        // persisted module settings (off by default; the thread only spins
        // up when the master switch is on).
        InitializeTaskbarCover();
        context.Milestone("taskbar_cover");

        // Shared keyboard/mouse Raw Input host — the single per-process owner
        // of the mouse Raw Input resource. Created before its consumers
        // (autocorrect, wheel capture), which reference-count it.
        InitializeInputHost();
        context.Milestone("input_host");

        // Optional wheel-to-touchpad translation. The native device and worker
        // exist only while its persisted master switch is on.
        InitializePrecisionScroll();
        context.Milestone("precision_scroll");

        // Paragraph retaille — observes the shared keyboard stream, requests a
        // gated local rewrite on Shift+Enter, and surfaces it in a non-activating
        // interactive inset. The module is entirely absent when Rewrite was not
        // selected at install.
        if (context.RewritePresent)
        {
            InitializeParagraphRewrite();
            context.Milestone("paragraph_rewrite");
        }
        else
        {
            context.Milestone("paragraph_rewrite_skipped");
        }

        // Mouse-wheel capture — attaches the JSONL recorder to the shared
        // host and reconciles it with the persisted settings (off by default;
        // the host only spins up when a capture is on or autocorrect is).
        InitializeMouseWheel();
        context.Milestone("mouse_wheel");

        // Autocorrect module — keyboard Raw Input + diacritics restorer +
        // injector, reconciled with the persisted module settings (Enabled by
        // default; corrections land only on enrolled processes, Notepad out of
        // the box).
        // Boot wires the settings edge only. The heavy lexicons and optional
        // reranker are loaded off-thread when the persisted switch is on, and
        // remain entirely absent while the module is disabled.
        if (context.AutocorrectPresent)
        {
            InitializeAutocorrect();
            context.Milestone("autocorrect");
        }
        else
        {
            context.Milestone("autocorrect_skipped");
        }

        // Anytype headless backend — start/adopt a windowless serve process
        // and supervise it from App.Anytype.cs. Fire-and-forget: readiness
        // runs off the boot path, and shutdown stops supervision rather than
        // killing the warm backend; the milestone marks dispatch, not readiness.
        if (context.AnytypePresent)
        {
            _ = InitializeAnytypeBackendAsync();
            context.Milestone("anytype_backend");
        }
        else
        {
            context.Milestone("anytype_backend_skipped");
        }

        // Lazy LogWindow: instantiated on first open via ShowLogWindowLazy().
        // The ILogWindowSink is attached at that point via
        // AppDiagnosticsBootstrap, which replays the LogWindowSink
        // ring buffer in the atomic operation so the viewer is complete as
        // soon as it opens. Avoids paying for a DComp swap chain + DWM visual
        // tree at boot for a window the user does not always need. Boot events
        // are preserved in app.jsonl by the JSONL listener registered at boot.

        // Lazy SettingsWindow: instantiated on first open via
        // ShowSettingsWindowLazy(). The boot --settings branch (restart from
        // Settings) creates + shows directly through the same lazy path, so
        // restart on the right page stays functional.

        // PlaygroundWindow lazy: dev tool, instancied on first tray
        // open via ShowPlaygroundLazy(). Avoids paying a DComp swap
        // chain + DWM visual tree at boot for a window rarely used.
        // Close destroys it; native Win32 placement persistence keeps
        // the next lazy instance at the user's last size and position.
    }
}

