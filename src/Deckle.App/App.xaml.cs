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
using Deckle.Shell.Foreground;
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
    private ForegroundWatcher? _foregroundWatcher;
    private HotkeyManager? _hotkeyManager;
    private LogWindow? _logWindow;

    internal static Settings.SettingsWindow? SettingsWin => (Current as App)?._settingsWindow;
    private Settings.SettingsWindow? _settingsWindow;
    private PlaygroundWindow? _playgroundWindow;
    private HudWindow? _hudWindow;
    private HudOverlayManager? _overlayManager;
    private TrayIconManager? _tray;
    private TrayMenuHost? _trayMenu;
    private TrayMenuHost? _trayActions;
    private TranscriptionEngine? _engine;
    private SpeechEngine? _speechEngine;
    private AmbientEngine? _ambientEngine;

    // Null until the engine is composed; consumers render an empty state.
    internal static AmbientEngine? AmbientEngine => (Current as App)?._ambientEngine;

    // Seeds the beacon of a window created mid-recording, before the next
    // status transition.
    private bool _lastRecordingState;

    // Long enough for a healthy disk to drain a full queue, short enough that
    // a crash never looks like a hang.
    private static readonly TimeSpan CrashFlushDeadline = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitFlushDeadline = TimeSpan.FromSeconds(5);

    public App()
    {
        InitializeComponent();

        // Without these nets, an exception in a background TranscriptionEngine
        // handler vanishes silently. Events raised before OnLaunched registers
        // the sinks are dropped.
        //
        // The nets that end the process drain the JSONL writers with a deadline:
        // the process may die right after the handler returns, and a crash line
        // that stays in a queue is the line a reader needed most. The deadline
        // keeps a stuck disk from turning a crash into a hang.
        //
        // The two nets that mark the exception handled or observed let the app
        // live, so they report a survived exception (one incident per exception
        // type and site) instead of a crash. The UI one still drains when it
        // opens an incident, to keep the cause on disk if the app dies right
        // after; a repeat does not, or a fault on every frame would stall the
        // UI thread for the deadline each time.
        this.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (SurvivedExceptionReporter.Report(SurvivedExceptionOrigin.UiThread, e.Exception))
                AppDiagnosticsBootstrap.Flush(CrashFlushDeadline);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            string exType = ex?.GetType().Name ?? "(unknown)";
            string exMessage = ex?.Message ?? "(no message)";
            DeckleAppSource.Log.CrashAppDomain(exType, exMessage);
            DeckleAppSource.Log.CrashAppDomainDetail(exType, exMessage);
            DeckleAppSource.Log.CrashStackTrace(ex?.StackTrace ?? "(no stack)");
            AppDiagnosticsBootstrap.Flush(CrashFlushDeadline);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // The AggregateException's own type and message say nothing: each
            // inner exception is its own incident, by its own type and site.
            var inner = e.Exception.Flatten().InnerExceptions;
            if (inner.Count == 0)
                SurvivedExceptionReporter.Report(SurvivedExceptionOrigin.UnobservedTask, e.Exception);
            foreach (Exception ex in inner)
                SurvivedExceptionReporter.Report(SurvivedExceptionOrigin.UnobservedTask, ex);
            e.SetObserved();
        };

        // JSONL writers run off the emitting thread, so the final marker is
        // followed by one drain; it covers install, update and relocation exits.
        // A drain that fails leaves its own marker file (see the bootstrap).
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            DeckleAppSource.Log.ProcessExit();
            // Catch-all for an exit that did not go through QuitApp. A process
            // killed from outside never gets here, which is what the mark is for.
            MarkRunEnded();
            AppDiagnosticsBootstrap.Flush(ExitFlushDeadline);
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Installer launches are a separate application mode: no diagnostics,
        // engines, windows, tray or hotkeys are composed after this gate.
        if (TryEnterInstallMode()) return;

        // Deckle is a resident utility. A second resident would install a second
        // global keyboard hook and could inject the same correction twice. The
        // transactional install/update/relocation modes above remain deliberately
        // exempt because they coordinate overlapping processes by design.
        if (!TryAcquireResidentOwnership())
        {
            NoteSecondInstanceRejected();
            Exit();
            return;
        }

        // Load-bearing order: the phases compile in any order but break silently
        // when reordered.
        var context = new StartupContext();
        InitializeStartupFoundation(context);
        InitializeStartupModules(context);
        WireStartupSettings(context);
        InitializeStartupShell(context);
        ApplyStartupPreferencesAndArguments(context);
    }

}
