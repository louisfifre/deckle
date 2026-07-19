using Deckle.App;
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
    private SpeechEngine? _speechEngine;
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
    // Operational-detail admission is enforced by each producer before work begins.

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

        // Explicit trace for process-exit. JSONL writers run off the emitting
        // thread, so the final marker is followed by one deterministic drain
        // covering normal, install, update and relocation exits.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            DeckleAppSource.Log.ProcessExit();
            _ = AppDiagnosticsBootstrap.Flush();
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Installer launches are a separate application mode: no diagnostics,
        // engines, windows, tray or hotkeys are composed after this gate.
        if (TryEnterInstallMode()) return;

        // Load-bearing order. Each phase owns one contiguous slice of the former
        // OnLaunched body; keep calls in this order when adding startup work.
        var context = new StartupContext();
        InitializeStartupFoundation(context);
        InitializeStartupModules(context);
        WireStartupSettings(context);
        InitializeStartupShell(context);
        ApplyStartupPreferencesAndArguments(context);
    }

}
