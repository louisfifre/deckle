using Deckle.App.Diagnostics;
using Deckle.Core;
using Deckle.Core.Interop;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Playground;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;
using Deckle.Transcription.Whisper.Setup;

namespace Deckle.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MessageOnlyHost? _messageHost;
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

    private static bool ShouldDropAmbientCaptureVerbose(
        string provider,
        System.Diagnostics.Tracing.EventLevel level,
        System.Diagnostics.Tracing.EventKeywords keywords)
    {
        // Seul le Verbose des providers de capture est concerné. Les jalons
        // (Info / Warning / Error) passent toujours — c'est par eux qu'on
        // sait, même toggle off, que ça tourne et que ça s'arrête.
        if (level != System.Diagnostics.Tracing.EventLevel.Verbose) return false;
        if (!AmbientCaptureGate.IsActive) return false;

        bool captureFamily =
            provider == "Deckle.Ambient"
            || provider == "Deckle.Vision"
            || provider == "Deckle.Lighting"
            // Sub-provider transverse, mais pendant une capture c'est le
            // firehose dominant : ResourceAcquired/Released par frame
            // (textures D3D11 capture-loop + frame-sampler). Hors capture
            // la gate est fermée, donc le Resource du HUD passe normalement.
            || provider == "Deckle.Diagnostics.Resource";
        if (!captureFamily) return false;

        // Toggle off : la capture est muette. Aucun Verbose, heartbeat
        // compris — on ne veut rien voir, juste savoir via les jalons si
        // ça marche. (Le filet de vitalité passera par un watchdog dédié,
        // pas par un battement permanent.)
        if (!LoggingSettingsService.Instance.Current.LogAmbientCaptureActivity) return true;

        // Toggle on : on montre le heartbeat (rollup 5 s) et les Verbose
        // occasionnels (détails start / stop), mais jamais le firehose
        // haute fréquence — push par-tick (keyword Push) et acquire/release
        // D3D11 par-frame (sous-provider Resource) restent tus, même opt-in.
        if ((keywords & (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Push) != 0) return true;
        if (provider == "Deckle.Diagnostics.Resource") return true;
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
            DeckleAppSource.Log.CrashUnhandled(e.Exception.GetType().Name, e.Exception.Message);
            DeckleAppSource.Log.CrashStackTrace(e.Exception.StackTrace ?? "(no stack)");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            DeckleAppSource.Log.CrashAppDomain(ex?.GetType().Name ?? "(unknown)", ex?.Message ?? "(no message)");
            DeckleAppSource.Log.CrashStackTrace(ex?.StackTrace ?? "(no stack)");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DeckleAppSource.Log.CrashTaskScheduler(e.Exception.GetType().Name, e.Exception.Message);
            e.SetObserved();
        };

        // Trace explicit du process-exit. Les listeners JSONL flushent à
        // chaque écriture, donc les events précédents sont déjà sur disque —
        // mais distinguer un shutdown propre d'un
        // crash brut dans les logs aide le post-mortem. Pas un dump, juste
        // un marqueur "on est sorti par cette voie".
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

        // Câblage `Deckle.Core.CorpusPaths` (relocalisé en sous-vague 6a) :
        // le helper de paths storage avait besoin d'un getter sur le
        // StorageDirectory utilisateur sans dépendre d'un module
        // observabilité. La dep est inversée par injection — l'App câble
        // le getter sur le nouveau TelemetrySettingsService.
        Deckle.Core.CorpusPaths.ConfigureStorageDirectoryOverride(() =>
        {
            string s = TelemetrySettingsService.Instance.Current.StorageDirectory;
            return string.IsNullOrWhiteSpace(s) ? null : s;
        });

        // EventSource observability pipeline. JsonlEventListeners écrivent
        // directement aux paths canoniques `<TelemetryDir>/{app,latency,
        // microphone,corpus}.jsonl`. Le LogWindow lazy s'attachera au
        // listener via `AttachLogWindowSink` à sa première ouverture.
        AppDiagnosticsBootstrap.Initialize(AppPaths.TelemetryDirectory);
        Milestone("diagnostics");

        // Câblage des gates utilisateur côté JsonlEventListeners
        // (Deckle.Diagnostics.Telemetry). Lecture directe sur le
        // TelemetrySettingsService canonique.
        Deckle.Diagnostics.Telemetry.TelemetryListenerBootstrap.ConfigureGates(name => name switch
        {
            "ApplicationLogToDisk" => TelemetrySettingsService.Instance.Current.ApplicationLogToDisk,
            "LatencyEnabled"       => TelemetrySettingsService.Instance.Current.LatencyEnabled,
            "MicrophoneTelemetry"  => TelemetrySettingsService.Instance.Current.MicrophoneTelemetry,
            "CorpusEnabled"        => TelemetrySettingsService.Instance.Current.CorpusEnabled,
            _                      => false,
        });

        // Drop filter ambient : silence les Verbose des providers
        // Ambient / Vision / Lighting quand une capture loop est
        // active ET que l'utilisateur n'a pas opt-in à LogAmbient-
        // CaptureActivity. La capture gate (AmbientCaptureGate) vit
        // dans Deckle.Diagnostics.Logging et est flippée par l'Ambient
        // engine au Start / Stop. Le toggle utilisateur est lu sur
        // LoggingSettingsService.
        AppDiagnosticsBootstrap.ConfigureLogWindowProviderLevelDropFilter(ShouldDropAmbientCaptureVerbose);
        TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter(ShouldDropAmbientCaptureVerbose);
        TelemetryListenerBootstrap.ConfigureApplicationLogDropFilter(ShouldDropApplicationLogEntry);

        // Boot-time sanity marker for the EventSource pipeline. It has no
        // product behaviour; it simply proves provider discovery, JSONL
        // routing, and LogWindow listener routing during startup.
        Deckle.Chrono.DeckleChronoSource.Log.PilotEmitted("wave 1 boot");

        // Sub-provider transverse Network — capter les transitions
        // d'état réseau de la machine pour corréler les échecs HTTP
        // métier (Hue REST, Ollama) avec une coupure ou bascule au
        // niveau OS. Émetteur unique, idempotent ; un event initial est
        // émis à `Start()` pour capter l'état au boot, puis sur chaque
        // broadcast `NetworkInformation.NetworkStatusChanged`.
        NetworkStatusEmitter.Start();

        // Resolved paths logged once at boot — useful for support: tells us
        // where the app is looking for settings, models, native DLLs, and
        // telemetry. Touching any AppPaths member triggers the static ctor
        // that resolves <UserDataRoot> and creates the writable directories.
        //
        // Doctrine logging : Info = jalon en phrase Capital courte ;
        // détails techniques (chemins résolus) en Verbose miroir, lisible
        // en filtre All sans polluer Activity.
        DeckleAppSource.Log.PathsInitialized();
        DeckleAppSource.Log.PathsDetail(
            AppPaths.UserDataRoot,
            AppPaths.SettingsFilePath,
            AppPaths.TelemetryDirectory,
            AppPaths.ModelsDirectory,
            AppPaths.NativeDirectory);

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
            DeckleSetupSource.Log.SetupInfo(
                $"first-run gate | natives_installed={NativeRuntime.IsInstalled()}" +
                $" | default_model_installed={SpeechModels.IsDefaultInstalled()}");
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ChoicesPage), setup);
            setup.Activate();
            bool success = await setup.Completion;
            if (!success)
            {
                DeckleSetupSource.Log.SetupInfo("wizard cancelled — exiting");
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
            DeckleAppSource.Log.AmbientPipelineState(s.ToString());
        // AmbientPage's NotPaired InfoBar action button needs to open
        // the Playground (where Hue pairing lives in V0). Lighting.
        // Ambient cannot reference Deckle, so the App fills the slot.
        AmbientEngine.OpenPlaygroundRequested = () => ShowPlaygroundLazy();
        Milestone("ambient_engine");

        // LogWindow lazy : instanciée à la première ouverture via
        // ShowLogWindowLazy(). Le sink ILogWindowSink est attaché à ce
        // moment-là via AppDiagnosticsBootstrap, qui rejoue dans
        // l'opération atomique le buffer ring du LogWindowEventListener
        // pour que le viewer soit complet dès l'ouverture. Évite de
        // payer un swap chain DComp + visual tree DWM au boot pour une
        // fenêtre dont l'utilisateur n'a pas systématiquement besoin.
        // Les events boot sont préservés dans app.jsonl par le listener
        // JSONL inscrit dès le boot.

        // SettingsWindow lazy : instanciée à la première ouverture via
        // ShowSettingsWindowLazy(). La branche --settings du boot
        // (restart depuis Settings) crée + show direct par le même
        // chemin lazy, donc le restart sur la bonne page reste fonctionnel.

        // PlaygroundWindow lazy: dev tool, instancied on first tray
        // open via ShowPlaygroundLazy(). Avoids paying a DComp swap
        // chain + DWM visual tree at boot for a window rarely used.
        // Same Closing→Hide contract once created.

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

        // HudWindow created once, never destroyed. No initial Show: the
        // constructor captures the HWND and sets up subclass / raw input /
        // extended styles directly on the native handle — no need to show the
        // window for this to work. The first ShowRecording triggers
        // ShowNoActivate, which positions bottom-center and calls SW_SHOWNOACTIVATE.
        _hudWindow = new HudWindow();

        // Manager for the transient overlay card stack (independent HWNDs
        // stacked 24 dip away from the main HUD). Owns per-card timers and
        // positions; reacts to main HUD show/hide via MainHudVisibilityChanged.
        _overlayManager = new HudOverlayManager(_hudWindow, _hudWindow.DispatcherQueue);

        // HUD feedback sink unique (canal EventSource direct depuis la
        // sous-vague 6b). `HudFeedbackEventListener` (Deckle.Diagnostics)
        // filtre les events `UserFeedbackEmitted` de tout provider Deckle.*
        // et passe une `FeedbackEntry(title, body, severity:int, role:int)`
        // à ce sink. Le sink route vers la surface principale (`ShowUser-
        // Feedback`) ou la stack (`HudOverlayManager.Enqueue`) selon role.
        // Le double-câblage legacy (HudFeedbackSink + LegacyHudFeedback-
        // Sink) a disparu — un seul pipeline.
        AppDiagnosticsBootstrap.AttachHudFeedbackSink(
            new AppHudFeedbackSink(
                onReplacement: (sev, title, body) => _hudWindow.ShowUserFeedback(sev, title, body),
                onOverlay:     (sev, title, body) => _overlayManager.Enqueue(sev, title, body)));

        // Warm pass: brief Show + Hide of the HUD at its real position so the
        // first composition (swap chain DComp + visual tree + Bitcount font
        // shaping) happens at boot rather than at first hotkey. The flash is
        // visible during boot — accepted, because the user isn't watching the
        // HUD position yet. First hotkey afterwards is cold-path-free.
        _hudWindow.PrimeAndHide();
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
            DeckleAppSource.Log.StatusChanged(status);
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
        DeckleAppSource.Log.StatusChanged("Ready");

        // Force Ambient master toggle OFF at boot — explicit user
        // action via Settings / tray re-enables the pipeline. Louis
        // explicit preference : the app should not start screen
        // capture + Hue traffic on its own at launch. Subscribe the
        // observer AFTER the force-off so the Changed event doesn't
        // bounce a Start call we just suppressed.
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

        // Message-only Win32 host — invisible by construction (HWND_MESSAGE
        // parent). Hosts the tray callback and global hotkeys without any
        // XAML window or off-screen trick.
        _messageHost = new MessageOnlyHost();

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
        // Try/catch obligatoire : RegisterHotKey peut échouer avec err 1409
        // (ERROR_HOTKEY_ALREADY_REGISTERED) quand un autre process possède
        // déjà la combinaison — typiquement WhispInteropTest qui tourne
        // encore via la tâche planifiée Whisp, ou PowerToys / une app
        // tierce qui a pris Win+1. Sans ce filet, le throw d'HotkeyManager
        // remonte à OnLaunched et l'app refuse de démarrer.
        //
        // Compromis : l'app continue de démarrer mais sans hotkeys. Le
        // tray reste opérationnel (Settings, Quit, toggle recording via
        // clic), donc l'utilisateur n'est pas verrouillé. UserFeedback
        // Overlay informe visuellement via le HUD au boot.
        try
        {
            _hotkeyManager.Register();
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning($"Hotkey registration failed: {ex.Message}");
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
        // timeout. The HUD's own composition warm (PrimeAndHide, no model)
        // stays — it pays the DComp/font cost only, never any VRAM.

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
            // Voie lazy : crée la fenêtre + l'affiche sur la page demandée.
            // Indistinct du chemin tray quand l'utilisateur ouvre Settings
            // pour la première fois.
            ShowSettingsWindowLazy(pageTag);
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
