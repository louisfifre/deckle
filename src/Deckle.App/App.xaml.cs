using Deckle.App;
using Deckle.Core;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Playground;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MessageOnlyHost? _messageHost;
    private CursorMovementSignal? _cursorSignal;
    private HotkeyManager? _hotkeyManager;
    private LogWindow? _logWindow;

    internal static Settings.SettingsWindow? SettingsWin => (Current as App)?._settingsWindow;
    private Settings.SettingsWindow? _settingsWindow;
    private PlaygroundWindow? _playgroundWindow;
    private HudWindow? _hudWindow;
    private HudOverlayManager? _overlayManager;
    private TrayIconManager? _tray;
    private TrayContextMenuHost? _trayMenu;
    private TranscriptionEngine? _engine;
    private AmbientEngine? _ambientEngine;

    // Canonical engine accessor for surfaces that observe the running
    // pipeline (Playground preview, future AmbientPage live readouts).
    // Returns null when the engine hasn't been built yet (e.g. before
    // OnLaunched completes) ; consumers should null-check and render an
    // empty state when missing.
    internal static AmbientEngine? AmbientEngine => (Current as App)?._ambientEngine;

    // Last engine recording state, captured on every StatusChanged. Used
    // to seed PlaygroundWindow's beacon on lazy creation: when the user
    // opens Playground for the first time mid-recording, the beacon
    // reflects the current state without waiting for the next status
    // transition.
    private bool _lastRecordingState;

    private static bool ShouldDropCaptureVerbose(
        string provider,
        System.Diagnostics.Tracing.EventLevel level,
        System.Diagnostics.Tracing.EventKeywords keywords)
    {
        // Only Verbose events are affected. Milestones (Info / Warning / Error)
        // always pass through; they are how we know, even with both toggles off,
        // that an activity is running and stopping.
        if (level != System.Diagnostics.Tracing.EventLevel.Verbose) return false;

        // ── Ambient capture family ─────────────────────────────────────────
        if (AmbientCaptureGate.IsActive)
        {
            bool ambientFamily =
                provider == "Deckle-Ambient"
                || provider == "Deckle-Vision"
                || provider == "Deckle-Lighting"
                // Cross-cutting sub-provider, but during capture it is the
                // dominant firehose: ResourceAcquired/Released per frame
                // (capture-loop D3D11 textures + frame-sampler). Outside
                // capture the gate is closed, so HUD Resource events pass.
                || provider == "Deckle-Resource";
            if (ambientFamily)
            {
                // Toggle off: capture is silent. No Verbose events, including
                // the heartbeat; only milestones tell whether it works.
                if (!LoggingSettingsService.Instance.Current.LogAmbientCaptureActivity) return true;

                // Toggle on: show the 5 s heartbeat and occasional Verbose
                // events, but never the high-frequency firehose (per-tick push,
                // per-frame D3D11 acquire/release).
                if ((keywords & (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Push) != 0) return true;
                if (provider == "Deckle-Resource") return true;
                return false;
            }
        }

        // ── Streaming transcription family ─────────────────────────────────
        // Two Verbose streams the streaming pipeline emits per utterance:
        // Deckle-Whisp (decode firehose) and Deckle-Vad (speech-trim activity,
        // split out of Whisp when the VAD became its own module). Both ride the
        // same toggle so the gate stays whole after the split.
        if (StreamingCaptureGate.IsActive && (provider == "Deckle-Whisp" || provider == "Deckle-Vad"))
        {
            // Toggle off: the chatty stream is silent — the 1 Hz heartbeat
            // and per-utterance details are dropped — but the recognized
            // transcript text (KwTranscript) always surfaces. It is the
            // signal, not the firehose. Milestones still pass on their own
            // (StreamingPipelineStarted, StreamingDrained).
            if (!LoggingSettingsService.Instance.Current.LogStreamingTranscriptionActivity)
                return (keywords & DeckleWhispSource.KwTranscript) == 0;
            return false;
        }

        // ── Autocorrect ────────────────────────────────────────────────────
        // No capture gate: the autocorrect engine runs continuously, so the
        // toggle filters whenever it is off. Off: only the edits survive — an
        // applied correction's Verbose detail (Push keyword: reason and lengths,
        // never the word), alongside its Info milestone and any revert /
        // injection failure (Info / Warning, which pass on their own above). The
        // per-focus SurfaceChanged probe, the learning signals and the 30 s
        // activity rollup are dropped: a heartbeat is meaningless for a
        // keystroke-driven subsystem, only the corrections are. On: everything
        // passes, SurfaceChanged and rollup included, for a debug deep-dive.
        if (provider == "Deckle-Autocorrect")
        {
            if (LoggingSettingsService.Instance.Current.LogAutocorrectActivity) return false;
            return (keywords & (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Push) == 0;
        }

        return false;
    }

    private static bool ShouldDropApplicationLogEntry(Deckle.Diagnostics.EventEntry entry)
    {
        var mode = LoggingSettingsService.Instance.Current.LogWindowVisibilityMode;
        return !Deckle.Diagnostics.Logging.LogWindowFilter.IsVisible(entry, mode);
    }

    public App()
    {
        InitializeComponent();

        // Diagnostic safety net — without this, a crash in a TranscriptionEngine
        // event disappears silently. Any listener registered later in OnLaunched
        // picks these up for LogWindow/app.jsonl. Events
        // raised before OnLaunched have no sinks yet and are dropped — there
        // are none of those in practice.
        this.UnhandledException += (_, e) =>
        {
            DeckleAppSource.Log.CrashUnhandled();
            DeckleAppSource.Log.CrashUnhandledDetail(e.Exception.GetType().Name, e.Exception.Message);
            DeckleAppSource.Log.CrashStackTrace(e.Exception.StackTrace ?? "(no stack)");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            DeckleAppSource.Log.CrashAppDomain();
            DeckleAppSource.Log.CrashAppDomainDetail(ex?.GetType().Name ?? "(unknown)", ex?.Message ?? "(no message)");
            DeckleAppSource.Log.CrashStackTrace(ex?.StackTrace ?? "(no stack)");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DeckleAppSource.Log.CrashTaskScheduler();
            DeckleAppSource.Log.CrashTaskSchedulerDetail(e.Exception.GetType().Name, e.Exception.Message);
            e.SetObserved();
        };

        // Explicit trace for process-exit. JSONL listeners flush on every
        // write, so previous events are already on disk, but distinguishing a
        // clean shutdown from a raw crash in the logs helps post-mortems. Not
        // a dump, just a marker that says "we exited through this path".
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            DeckleAppSource.Log.ProcessExit();
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Cold-start instrumentation. Milestones accumulate into a local
        // list during construction and get flushed as a single aggregate
        // EventSource line at the end of OnLaunched — LogWindow receives
        // it under [APP]. A naive "one event per milestone" approach
        // would make early boot noisier without improving diagnosis.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var milestones = new List<string>();
        void Milestone(string name) => milestones.Add($"{name} +{sw.ElapsedMilliseconds}ms");

        // Always-on local diagnostic sinks (setup.jsonl + errors.jsonl) come
        // FIRST — before settings migration — so an Error in the very first boot
        // step still lands in errors.jsonl. An EventListener only captures events
        // emitted after it subscribes; these sinks read no settings and are
        // ungated, so registering them here is what makes the local trace cover
        // the riskiest, un-opted-in moment. The opt-in telemetry listeners are
        // wired later, after migration — see InitializeTelemetry below.
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        Milestone("diagnostics-local");

        // Per-module persistence migration runs FIRST — before any module
        // SettingsService.Instance is touched. Detects the legacy combined
        // settings.json and dispatches each module section to its own
        // modules/<id>/settings.json file. Idempotent across launches: on
        // subsequent boots the legacy file no longer carries module
        // sections, so the bootstrap is a no-op.
        //
        // Why first? Because module SettingsService singletons load their
        // file in their constructor — if a module service initialized before
        // the dispatch ran, it would see a missing module file and write
        // defaults, defeating the migration. Telemetry gate wiring below
        // touches TelemetrySettingsService, so we must dispatch before that.
        Settings.SettingsBootstrap.MigrateLegacyToPerModule();
        Milestone("settings-bootstrap");

        // Wiring for `Deckle.Core.CorpusPaths` (relocated in sub-wave 6a):
        // the storage paths helper needed a getter for the user
        // StorageDirectory without depending on an observability module. The
        // dependency is inverted through injection: App wires the getter to the
        // new TelemetrySettingsService.
        Deckle.Core.CorpusPaths.ConfigureStorageDirectoryOverride(() =>
        {
            string s = TelemetrySettingsService.Instance.Current.StorageDirectory;
            return string.IsNullOrWhiteSpace(s) ? null : s;
        });

        // Opt-in telemetry pipeline + LogWindow ring buffer. JsonlEventListeners
        // write directly to canonical paths `<TelemetryDir>/{app,latency,
        // microphone,corpus}.jsonl`; lazy LogWindow attaches via
        // `AttachLogWindowSink` on first open. The always-on local sinks were
        // wired earlier (top of OnLaunched) — see InitializeLocalSinks.
        AppDiagnosticsBootstrap.InitializeTelemetry(AppPaths.TelemetryDirectory);
        Milestone("diagnostics");

        // Wire user gates on the JsonlEventListeners side
        // (Deckle.Diagnostics.Telemetry). Direct read from the canonical
        // TelemetrySettingsService.
        Deckle.Diagnostics.Telemetry.TelemetryListenerBootstrap.ConfigureGates(name => name switch
        {
            "ApplicationLogToDisk" => TelemetrySettingsService.Instance.Current.ApplicationLogToDisk,
            "LatencyEnabled"       => TelemetrySettingsService.Instance.Current.LatencyEnabled,
            "MicrophoneTelemetry"  => TelemetrySettingsService.Instance.Current.MicrophoneTelemetry,
            "CorpusEnabled"        => TelemetrySettingsService.Instance.Current.CorpusEnabled,
            _                      => false,
        });

        // Capture drop filter: silence Verbose events from the two capture
        // families when their respective gates are active AND the user has
        // not opted into the matching toggle. Ambient family (Ambient /
        // Vision / Lighting / Resource sub-provider) gated by
        // AmbientCaptureGate + LogAmbientCaptureActivity; streaming
        // transcription gated by StreamingCaptureGate +
        // LogStreamingTranscriptionActivity. Both gates live in
        // Deckle.Diagnostics.Logging; the engines flip them on Start / Stop.
        AppDiagnosticsBootstrap.ConfigureLogWindowProviderLevelDropFilter(ShouldDropCaptureVerbose);
        TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter(ShouldDropCaptureVerbose);
        TelemetryListenerBootstrap.ConfigureApplicationLogDropFilter(ShouldDropApplicationLogEntry);

        // Boot-time sanity marker for the EventSource pipeline. It has no
        // product behaviour; it simply proves provider discovery, JSONL
        // routing, and LogWindow listener routing during startup.
        Deckle.Chrono.DeckleChronoSource.Log.PilotEmitted();
        Deckle.Chrono.DeckleChronoSource.Log.PilotEmittedDetail("wave 1 boot");

        // Cross-cutting Network sub-provider: capture machine network state
        // transitions to correlate business HTTP failures (Hue REST, Ollama)
        // with an OS-level outage or switch. Single idempotent emitter; an
        // initial event is emitted at `Start()` to capture boot state, then on
        // every `NetworkInformation.NetworkStatusChanged` broadcast.
        NetworkStatusEmitter.Start();

        // Resolved paths logged once at boot — useful for support: tells us
        // where the app is looking for settings, models, native DLLs, and
        // telemetry. Touching any AppPaths member triggers the static ctor
        // that resolves <UserDataRoot> and creates the writable directories.
        //
        // Logging doctrine: Info = short capitalized milestone sentence;
        // technical details (resolved paths) in mirrored Verbose, readable
        // under the All filter without polluting Activity.
        DeckleAppSource.Log.PathsInitialized();
        DeckleAppSource.Log.PathsDetail(
            AppPaths.UserDataRoot,
            AppPaths.SettingsFilePath,
            AppPaths.TelemetryDirectory,
            AppPaths.ModelsDirectory,
            AppPaths.NativeDirectory);

        // Notification dispatcher — composition root for user messages. Wired
        // here, right after the diagnostics listeners are live (above), so the
        // audit events the dispatcher emits route from the very first emission;
        // and before any surface that could raise a notification exists (the
        // first-run wizard, the engine, the windows below). The toast channel
        // is the only channel today (the autocorrect enrollment prompt needs
        // native Windows 11 interactive toasts). Each module's descriptors are
        // registered into the central catalogue at this point — Playground's
        // manual test surface among them; duplicate ids fail the boot fast.
        var toastChannel = new Deckle.Notifications.ToastChannel();
        var dispatcher = Deckle.Notifications.NotificationDispatcher.Initialize(toastChannel);
        dispatcher.Catalog.Register(PlaygroundNotifications.All);
        Milestone("notifications");

        // First-run gate — the engine ctor below loads the model immediately
        // and would throw DllNotFoundException without the native runtime
        // (libwhisper + ggml backends). There's no graceful degradation:
        // either the dependencies are in place, or we open the wizard, or
        // the user quits. The wizard provisions the natives via auto-download
        // (Deckle GitHub Release, NativeRuntime.CurrentBundle.Url) with a
        // local Browse... fallback, plus the chosen speech model from
        // HuggingFace.
        if (!NativeRuntime.IsInstalled() ||
            !SpeechModels.IsDefaultInstalled())
        {
            DeckleSetupSource.Log.WizardOpening();
            DeckleSetupSource.Log.WizardOpeningDetail(NativeRuntime.IsInstalled(), SpeechModels.IsDefaultInstalled());
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ChoicesPage), setup);
            setup.Activate();
            bool success = await setup.Completion;
            if (!success)
            {
                DeckleSetupSource.Log.WizardCancelled();
                Environment.Exit(0);
                return;
            }
            Milestone("wizard");
        }

        // Compose the engine with the Whisper backend — the App is the
        // composition root that knows which IAsrBackend to instantiate.
        // When a second backend ships (Voxtral), the choice surfaces in
        // Settings and gates the construction here.
        var host = new AppTranscriptionEngineHost();
        var backend = new WhisperBackend(host);
        _engine = new TranscriptionEngine(host, backend);
        Milestone("engine");

        // Canonical Ambient engine — owns its own ScreenCaptureService,
        // FrameSampler, HueBridgeClient and HueRestLightOutput at
        // StartAsync time. Construct is cheap, no I/O. Started / Stopped
        // by the AmbientSettings.Changed observer wired below. If the
        // user persisted Enabled=true from a previous session, the
        // pipeline boots automatically when the engine starts (fire-and-
        // forget Task so OnLaunched stays non-blocking).
        _ambientEngine = new AmbientEngine(new AppAmbientEngineHost());
        // Surface every state transition in the logs (Info level so it
        // lands in app.jsonl without requiring the LogAmbientCapture-
        // Activity toggle). Subscribers in the windows (AmbientPage
        // ProgressRing, Playground status) hook directly to the engine
        // event ; we don't relay through tray UpdateStatus to avoid
        // squatting the Whisp recording tooltip.
        _ambientEngine.StateChanged += s =>
        {
            DeckleAppSource.Log.AmbientPipelineState();
            DeckleAppSource.Log.AmbientPipelineStateDetail(s.ToString());
        };
        // AmbientPage's NotPaired InfoBar action button needs to open
        // the Playground (where Hue pairing lives in V0). Lighting.
        // Ambient cannot reference Deckle, so the App fills the slot.
        AmbientEngine.OpenPlaygroundRequested = () => ShowPlaygroundLazy();
        Milestone("ambient_engine");

        // Trackpad module — Raw Input host + three-finger drag engine +
        // frame recorder, reconciled with the persisted module settings
        // (off by default: the host thread only spins up when the master
        // switch or the frame-recording diagnostic is on).
        InitializeTrackpad();
        Milestone("trackpad");

        // Taskbar cover module — dedicated band thread reconciled with the
        // persisted module settings (off by default; the thread only spins
        // up when the master switch is on).
        InitializeTaskbarCover();
        Milestone("taskbar_cover");

        // Autocorrect module — keyboard Raw Input + diacritics restorer +
        // injector, reconciled with the persisted module settings (Enabled by
        // default; corrections land only on enrolled processes, Notepad out of
        // the box). Loads the two small gzip lexicons from Data/ beside the exe;
        // the live engine never touches the offline-only CamemBERT reranker.
        InitializeAutocorrect();
        Milestone("autocorrect");

        // Lazy LogWindow: instantiated on first open via ShowLogWindowLazy().
        // The ILogWindowSink is attached at that point via
        // AppDiagnosticsBootstrap, which replays the LogWindowEventListener
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

        // Wire the recording cap into the Hud lib. Deckle.Hud is
        // a Settings-agnostic module ; the App is the one that reads
        // Settings on every vsync to honour live edits to MaxRecordingDurationSeconds
        // (Capture page slider). Provider is invoked from UpdateClock at vsync.
        Deckle.Hud.HudChrono.MaxRecordingDurationSecondsProvider =
            () => Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds;

        // SettingsHost — App-side hooks the Deckle.Settings UI surface
        // calls back into to drive theme broadcast, level-window
        // propagation, restart, and the parent-window accessor for
        // dialogs. Must be wired before any Settings page is created.
        // Pattern aligned on HudChrono.MaxRecordingDurationSecondsProvider
        // above: lib exposes static delegates, App owns the contract.
        Settings.SettingsHost.ApplyTheme       = ApplyTheme;
        Settings.SettingsHost.ApplyLevelWindow = ApplyLevelWindow;
        Settings.SettingsHost.RestartApp       = RestartApp;
        Settings.SettingsHost.GetSettingsWindow = () => _settingsWindow;
        Settings.SettingsHost.OpenSetupWizard  = () =>
        {
            // Wizard XAML lives in the standalone Deckle.Setup module
            // (extracted out of Deckle.App/Shell/Setup/ for J3). Detached
            // from the Settings window — Settings stays open behind it.
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ChoicesPage), setup);
            setup.Activate();
        };

        // Message-only Win32 host — invisible by construction (HWND_MESSAGE
        // parent). Hosts the tray callback, global hotkeys, and the shared
        // cursor-movement signal created right after it. Built here, before the
        // HUD: the HUD and the overlay cards subscribe to that signal, and the
        // signal's Raw Input sink must target this always-alive, never-hidden
        // window rather than the HUD's HWND (hidden between uses). It has no
        // upstream dependency; its own consumers (tray context menu, tray
        // registration, hotkeys) are still wired further down in this method.
        _messageHost = new MessageOnlyHost();

        // The single process-wide source of "the cursor moved": it owns the one
        // mouse Raw Input sink and fans WM_INPUT out to every proximity surface
        // (the HUD and its overlay cards). Held in a field so the rooted
        // subclass delegate is not collected.
        _cursorSignal = new CursorMovementSignal(_messageHost.Hwnd);

        // HudWindow created once, never destroyed. No initial Show: the
        // constructor captures the HWND and sets up subclass / extended styles
        // directly on the native handle — no need to show the window for this
        // to work. The first ShowRecording triggers ShowNoActivate, which
        // positions bottom-center and calls SW_SHOWNOACTIVATE.
        _hudWindow = new HudWindow(_cursorSignal);

        // Manager for the transient overlay card stack (independent HWNDs
        // stacked 24 dip away from the main HUD). Owns per-card timers and
        // positions; reacts to main HUD show/hide via MainHudVisibilityChanged.
        _overlayManager = new HudOverlayManager(_hudWindow, _hudWindow.DispatcherQueue, _cursorSignal);

        // Unique HUD feedback sink (direct EventSource channel since
        // sub-wave 6b). `HudFeedbackEventListener` (Deckle.Diagnostics)
        // filters `UserFeedbackEmitted` events from any Deckle.* provider and
        // passes a `FeedbackEntry(title, body, severity:int, role:int)` to
        // this sink. The sink routes to the main surface (`ShowUserFeedback`)
        // or the stack (`HudOverlayManager.Enqueue`) by role. Legacy double
        // wiring (HudFeedbackSink + LegacyHudFeedbackSink) is gone; one
        // pipeline remains.
        AppDiagnosticsBootstrap.AttachHudFeedbackSink(
            new AppHudFeedbackSink(
                onReplacement: (sev, title, body) => _hudWindow.ShowUserFeedback(sev, title, body),
                onOverlay:     (sev, title, body) => _overlayManager.Enqueue(sev, title, body)));

        Milestone("hudwindow");

        // Tray icon : only left-click action lives here ; the right-click
        // context menu is rendered by TrayContextMenuHost (created later,
        // after the message-only host HWND is available to serve as owner).
        _tray = new TrayIconManager
        {
            // Left-click tray = toggle transcription via the same path as the
            // standard hotkey. Allows starting with the mouse one-handed.
            OnToggleRecording = () => OnHotkey(NativeMethods.HOTKEY_ID_TRANSCRIBE),
        };
        Milestone("tray");

        // Engine events → UI. StatusChanged, TranscriptionFinished, etc. are
        // called from background threads; LogWindow and HudWindow marshal
        // internally via DispatcherQueue, UpdateStatus only calls
        // Shell_NotifyIcon (thread-safe). Engine logs are emitted by
        // module EventSource providers.
        _engine.StatusChanged += status =>
        {
            _tray.UpdateStatus(status);
            DeckleAppSource.Log.StatusChanged();
            DeckleAppSource.Log.StatusChangedDetail(status);
            // Beacon app icon in LogWindow + PlaygroundWindow: red =
            // recording, grey = idle. Single source of truth driven
            // from the engine status transition. StartsWith covers the
            // "Recording…" ellipsis variant emitted by RaiseStatus to
            // signal a transient state visually in the tray tooltip.
            bool isRecording = status.StartsWith("Recording");
            _lastRecordingState = isRecording;
            // Both nullable now: LogWindow and PlaygroundWindow are lazy-
            // created on first user open, so they're absent until then.
            _logWindow?.SetRecordingState(isRecording);
            _playgroundWindow?.SetRecordingState(isRecording);

            // HUD: driven by status transition. Background thread → HudWindow
            // marshals internally via DispatcherQueue. StartsWith on every
            // branch so transient ellipsis variants ("Transcribing…",
            // "Rewriting (cleanup)…") all route correctly.
            if (status.StartsWith("Recording"))
                _hudWindow.ShowRecording();
            else if (status.StartsWith("Transcribing"))
                _hudWindow.SwitchToTranscribing();
            else if (status.StartsWith("Rewriting"))
                _hudWindow.SwitchToRewriting();
        };
        _engine.TranscriptionFinished += outcome =>
        {
            switch (outcome)
            {
                case TranscriptionOutcome.Pasted:
                    // UIA confirmed the focused element accepts text and the
                    // Ctrl+V was sent. Brief success flash, then auto-hide.
                    _hudWindow.ShowPasted();
                    break;
                case TranscriptionOutcome.ClipboardOnly:
                    // Paste skipped (UIA unsure, foreground = Deckle, no focus,
                    // SendInput partial…) — tell the user the text is on the
                    // clipboard and keep the HUD up long enough to read.
                    _hudWindow.ShowCopied();
                    break;
                default:
                    _hudWindow.Hide();
                    break;
            }
        };
        // Synchronous rendezvous just before paste: hide the HUD and wait for
        // SW_HIDE to be effective on the UI thread before the engine sends
        // SendInput. Avoids the race where Hide() (triggered async after paste)
        // redistributes activation while Ctrl+V is still in the target's input queue.
        _engine.OnReadyToPaste = () => _hudWindow.HideSync();

        // Mic RMS → HUD recording outline. Fires ~20 Hz from the recording
        // audio thread; OnAudioLevel pushes into a CompositionPropertySet,
        // thread-safe per Composition's contract — no dispatcher needed.
        // Method group so it can be unsubscribed symmetrically later if
        // needed; no-op when the outline isn't attached (any non-Recording
        // state), so permanent subscription is fine.
        _engine.AudioLevel += _hudWindow.OnAudioLevel;

        // Initial status — model loads on-demand at first hotkey, not at startup.
        _tray.UpdateStatus("Ready");
        DeckleAppSource.Log.StatusChanged();
        DeckleAppSource.Log.StatusChangedDetail("Ready");

        // Force Ambient master toggle OFF at boot; explicit user action via
        // Settings / tray re-enables the pipeline. Louis's explicit
        // preference: the app should not start screen capture + Hue traffic on
        // its own at launch. Subscribe the observer AFTER the force-off so the
        // Changed event does not bounce a Start call we just suppressed.
        if (AmbientSettingsService.Instance.Current.Enabled)
        {
            AmbientSettingsService.Instance.Current.Enabled = false;
            AmbientSettingsService.Instance.Save();
            DeckleAppSource.Log.AmbientMasterForcedOff();
        }

        // Ambient Light master toggle observer. Drives Start / Stop on
        // the canonical engine instantiated above. The engine owns its
        // own capture / sampler / Hue dependencies so the pipeline runs
        // without any window needing to be open.
        AmbientSettingsService.Instance.Changed += OnAmbientSettingsChanged;

        // Tray context menu — WinUI 3 SecondWindow pattern. The host needs
        // the message-only HWND as its owner so its popup inherits the tray's
        // z-order / activation stack. Right-click on the tray icon raises
        // RightClickRequested, which the menu host translates into a popup
        // shown at the cursor with a Win11-native MenuFlyout.
        _trayMenu = new TrayContextMenuHost(_messageHost.Hwnd)
        {
            OnShowLogs       = () => ShowLogWindowLazy(),
            OnShowSettings   = () => ShowSettingsWindowLazy(),
            OnShowPlayground = () => ShowPlaygroundLazy(),
            // Ambient Light tray entry — checkmark mirrors the persisted
            // AmbientSettings.Enabled, click flips it. The actual engine
            // start/stop reacts via the AmbientSettingsService.Changed
            // observer wired in phase I.
            IsAmbientOn      = () => AmbientSettingsService.Instance.Current.Enabled,
            OnToggleAmbient  = () =>
            {
                var s = AmbientSettingsService.Instance.Current;
                s.Enabled = !s.Enabled;
                AmbientSettingsService.Instance.Save();
            },
            // Taskbar cover tray entry — same posture as Ambient: the pill
            // mirrors the persisted TaskbarCoverSettings.Enabled, click flips
            // it, the host start/stop reacts via the settings observer wired
            // in InitializeTaskbarCover.
            IsTaskbarCoverOn      = () => TaskbarCoverSettingsService.Instance.Current.Enabled,
            OnToggleTaskbarCover  = () =>
            {
                var s = TaskbarCoverSettingsService.Instance.Current;
                s.Enabled = !s.Enabled;
                TaskbarCoverSettingsService.Instance.Save();
            },
            OnRestart        = () => RestartAppFromTray(),
            OnQuit           = () => QuitApp(),
            // Tray icon screen rect — the menu host uses it as the
            // CalculatePopupWindowPosition exclude rect so the popup anchors
            // tangent to the icon regardless of taskbar orientation.
            GetIconRect      = () => _tray.GetIconRect(),
        };
        _tray.RightClickRequested += () => _trayMenu.Show();

        _tray.Register(_messageHost.Hwnd);
        _hotkeyManager = new HotkeyManager(_messageHost.Hwnd, OnHotkey);
        // Try/catch required: RegisterHotKey can fail with err 1409
        // (ERROR_HOTKEY_ALREADY_REGISTERED) when another process already owns
        // the chord; typically WhispInteropTest still running through the
        // scheduled Whisp task, or PowerToys / a third-party app that took
        // Win+1. Without this safety net, HotkeyManager's throw bubbles to
        // OnLaunched and the app refuses to start.
        //
        // Compromise: the app continues to start but without hotkeys. The tray
        // remains operational (Settings, Quit, toggle recording by click), so
        // the user is not locked out. UserFeedback Overlay informs visually
        // through the HUD at boot.
        try
        {
            _hotkeyManager.Register();
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"Hotkey registration failed: {ex.Message}");
            DeckleAppSource.Log.UserFeedbackEmitted(
                2, // Error
                "Hotkeys unavailable",
                "Another app owns the chord (often WhispInteropTest still running). Use the tray icon to record.",
                1); // Overlay
        }
        Milestone("hotkeys");

        // No model warmup at boot: nothing is loaded into VRAM while the app
        // is idle. The model is loaded + its kernels compiled on demand on the
        // first hotkey press, inside the engine worker (EnsurePrimed), while
        // the HUD shows its Charging state; it is freed again after the idle
        // timeout. There is no separate HUD composition warm at boot anymore:
        // the first real visible transition pays that cost through the normal
        // ShowNoActivate + fade-in path.

        // Apply saved theme (System/Light/Dark).
        ApplyTheme(Settings.SettingsService.Instance.Current.Appearance.Theme);

        // Apply persisted level window (MinDbfs / MaxDbfs / DbfsCurveExponent)
        // into AudioLevelMapper so the first Recording reflects the user's
        // calibration without a restart-from-defaults round-trip.
        ApplyLevelWindow(Audio.CaptureSettingsService.Instance.Current.LevelWindow);

        // If launched with --settings (restart from Settings), automatically
        // reopen the Settings window on the right page.
        var cliArgs = Environment.GetCommandLineArgs();
        int settingsIdx = Array.IndexOf(cliArgs, "--settings");
        if (settingsIdx >= 0)
        {
            string? pageTag = settingsIdx + 1 < cliArgs.Length
                ? cliArgs[settingsIdx + 1]
                : null;
            DeckleAppSource.Log.CmdLineSettingsFlag(pageTag ?? "(default)");
            // Lazy path: creates the window + shows it on the requested page.
            // Indistinguishable from the tray path when the user opens
            // Settings for the first time.
            ShowSettingsWindowLazy(pageTag);
        }

        // Diagnostic-only repro path for the HUD z-order bug. It follows the
        // same shape as the post-build workaround, but relaunches into a
        // bounded self-test that triggers the first real HUD show, then quits.
        int postBuildHudZOrderSelfTestIdx = Array.IndexOf(cliArgs, "--post-build-hud-zorder-selftest");
        if (postBuildHudZOrderSelfTestIdx >= 0)
        {
            DeckleAppSource.Log.CmdLinePostBuildFlag();
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RestartViaShellExecute("--hud-zorder-selftest");
            };
            timer.Start();
        }

        int hudZOrderSelfTestIdx = Array.IndexOf(cliArgs, "--hud-zorder-selftest");
        if (hudZOrderSelfTestIdx >= 0)
        {
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var showTimer = dq.CreateTimer();
            showTimer.Interval = TimeSpan.FromMilliseconds(1200);
            showTimer.IsRepeating = false;
            showTimer.Tick += (s, e) =>
            {
                showTimer.Stop();
                _hudWindow.ShowRecording();

                var quitTimer = dq.CreateTimer();
                quitTimer.Interval = TimeSpan.FromMilliseconds(2500);
                quitTimer.IsRepeating = false;
                quitTimer.Tick += (s2, e2) =>
                {
                    quitTimer.Stop();
                    _hudWindow.Hide();
                    QuitApp();
                };
                quitTimer.Start();
            };
            showTimer.Start();
        }

        // If launched with --post-build (set by scripts/lib/build-run.ps1),
        // schedule a one-shot self-restart via ShellExecute. The first
        // launch right after MSBuild occasionally inherits a degraded
        // foreground state that makes Windows defer WS_EX_TOPMOST on the
        // HUD, so the first recording shows the HUD behind every other
        // window. Relaunching ourselves through cmd /c start gives the
        // new process a clean foreground state.
        int postBuildIdx = Array.IndexOf(cliArgs, "--post-build");
        if (postBuildIdx >= 0)
        {
            DeckleAppSource.Log.CmdLinePostBuildFlag();
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RestartViaShellExecute();
            };
            timer.Start();
        }

        sw.Stop();
        milestones.Add($"total {sw.ElapsedMilliseconds}ms");
        DeckleAppSource.Log.StartupMilestones(string.Join(" | ", milestones));
    }

}
