using Deckle.Catalog;
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
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;
using Deckle.Localization;

namespace Deckle.App;

public partial class App
{
    private void InitializeStartupShell(StartupContext context)
    {
        // Before the HUD: the cursor signal's Raw Input sink must target this
        // always-alive window, not the HUD's HWND, which is hidden between uses.
        _messageHost = new MessageOnlyHost();

        // Fans the cursor signal out to every proximity surface. Held in a field
        // so the rooted subclass delegate is not collected.
        _cursorSignal = new CursorMovementSignal(_messageHost.Hwnd);

        // Created once, never destroyed. No initial Show: the constructor sets the
        // subclass and extended styles on the native handle directly.
        _hudWindow = new HudWindow(_cursorSignal);

        _overlayManager = new HudOverlayManager(_hudWindow, _hudWindow.DispatcherQueue, _cursorSignal);

        // Needs the HUD, the cursor signal and the engine.
        InitializeTranscriptPanel();

        AppDiagnosticsBootstrap.AttachHudFeedbackSink(
            new AppHudFeedbackSink(
                onReplacement: (sev, title, body) => _hudWindow.ShowUserFeedback(sev, title, body),
                onOverlay:     (sev, title, body) => _overlayManager.Enqueue(sev, title, body)));

        context.Milestone("hudwindow");

        _tray = new TrayIconManager();
        _tray.TaskbarCreated += ReapplyTaskbarTrayAfterExplorerRestart;
        // A window's icon sits on a tile made for the Windows mode: applying
        // the recording state again makes each window pick its tile again.
        // Raised from the tray's WndProc, where an exception must not escape.
        _tray.WindowsModeChanged += () =>
        {
            try { ApplyRecordingStateToWindows(_lastRecordingState); }
            catch (Exception ex)
            {
                DeckleAppSource.Log.WindowOperationFailed(
                    "all", "icon_reapply", "windows_mode_changed", ex.GetType().Name, ex.Message);
            }
        };
        context.Milestone("tray");

        // Lives as long as the process, so it is never disposed: the process
        // end destroys its window. A Windows session end can kill the process
        // right after the message returns, so the log is drained here.
        _sessionEndWatcher = new SessionEndWatcher(() => AppDiagnosticsBootstrap.Flush(ExitFlushDeadline));

        // Engine events arrive on background threads: LogWindow and HudWindow
        // marshal through their DispatcherQueue, and UpdateStatus only calls
        // Shell_NotifyIcon, which is thread-safe. No engine when speech is not
        // provisioned.
        if (_engine is not null)
        {
            _engine.FileTranscriptionStarted += () => _hudWindow.ShowFilePreparing();
            _engine.StatusChanged += status =>
            {
                // The recording state comes from the engine, never from the
                // localized status text.
                bool isRecording = _engine.IsRecording;
                DeckleAppSource.Log.StatusChanged(status, isRecording);
                // A quit waiting for the work keeps saying so in the tooltip.
                _tray.UpdateStatus(
                    _quitPending ? QuitPendingStatus(status) : status, isRecording);
                _lastRecordingState = isRecording;
                ApplyRecordingStateToWindows(isRecording);

                if (isRecording)
                    _hudWindow.ShowRecording();
            };
            // The HUD follows the phase the engine names, never its sentence.
            _engine.StatusPhaseChanged += phase =>
            {
                // Every phase passes here, so the one after the rewrite dims
                // the blade again.
                _tray.SetRewritingIndicator(phase == StatusPhase.Rewriting);
                if (_engine.IsRecording) return;
                switch (phase)
                {
                    case StatusPhase.Transcribing:
                        _hudWindow.SwitchToTranscribing();
                        break;
                    case StatusPhase.Rewriting:
                        _hudWindow.SwitchToRewriting();
                        break;
                }
            };
            _engine.TranscriptionFinished += outcome =>
            {
                switch (outcome)
                {
                    case TranscriptionOutcome.ClipboardOnly:
                        _hudWindow.ShowCopied();
                        break;
                    case TranscriptionOutcome.SavedToFile:
                        // Nothing opens: the HUD message is the only completion signal.
                        _hudWindow.ShowFileSaved();
                        break;
                    default:
                        _hudWindow.Hide();
                        break;
                }
            };
            // Raised on the audio thread. OnAudioLevel only writes a
            // CompositionPropertySet, which is thread-safe, so no dispatcher.
            _engine.AudioLevel += _hudWindow.OnAudioLevel;
            _engine.UtteranceProgressChanged += progress =>
                _hudWindow.OnUtteranceProgress(
                    progress.IsActive, progress.Fraction, progress.CompletedUtterances);
        }

        // A present module without an engine means speech is not provisioned. A
        // module absent by choice is not a missing setup: the tooltip stays quiet.
        string initialStatus = _engine is not null || !context.TranscriptionPresent
            ? Deckle.Localization.Loc.Get("Status_Ready")
            : Deckle.Localization.Loc.Get("Feedback_SpeechNotSetUp_Title");
        _tray.UpdateStatus(initialStatus, isRecording: false);

        // Ambient starts OFF at every boot: the app never starts screen capture
        // and Hue traffic on its own. The observer subscribes after the force-off
        // so its Changed does not bounce a Start just suppressed.
        if (context.AmbientPresent)
        {
            if (AmbientSettingsService.Instance.Current.Enabled)
            {
                AmbientSettingsService.Instance.Current.Enabled = false;
                AmbientSettingsService.Instance.Save();
                DeckleAppSource.Log.AmbientMasterForcedOff("boot_policy");
            }

            AmbientSettingsService.Instance.Changed += OnAmbientSettingsChanged;

            // The performance mode lives in the shell's settings and lowers
            // the capture rate's ceiling; a change there restarts a running
            // engine, which the start settings' comparison decides.
            Settings.SettingsService.Instance.Changed += OnAmbientSettingsChanged;
        }

        // Tray menus — two native MenuFlyouts, each on an invisible anchor
        // window placed on the tray icon and owned by the message-only HWND
        // (TrayMenuHost). The App composes their entries here; the module
        // renders them and knows no label and no module. Right click: the
        // developer menu, which opens only while Developer mode is on
        // (App.DeveloperAccess.cs).
        _trayDeveloperMenu = new TrayMenuHost(_messageHost.Hwnd, "tray-popup")
        {
            GetIconRect = () => _tray.GetIconRect(),
            Entries = ComposeDeveloperTray(),
        };
        _tray.RightClickRequested += ShowRightClickTray;
        WireDeveloperAccess(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        // Left click, the menu of every day, four groups behind separators,
        // read from the top: the background switches (set once, left on or
        // off), then Ambient Light on its own (a visual thing one actually
        // launches), then the actions (Record or Stop recording, Transcribe
        // audio files…), then the app's own commands at the very bottom
        // (Settings, Logs, Restart, Quit), where a tray menu keeps them
        // (Louis, 2026-10-14 and 2026-11-11). A switch is a row with a pill
        // (TraySwitchMenuItem); a click flips its module and closes the menu,
        // like any menu item. Every module entry follows module presence: an
        // absent module has no switch, no Record item, no file action.
        //
        // Module switches (App.TrayToggles.cs). Built once: the hotkey actions
        // below derive from the same lists, so a toggle's hotkey runs exactly
        // what its menu item runs.
        IReadOnlyList<TrayToggle> trayToggles = BuildTrayToggles(context);
        IReadOnlyList<TrayToggle> trayFooterToggles = BuildTrayFooterToggles(context);
        var actions = new List<TrayMenuEntry>();
        foreach (IReadOnlyList<TrayToggle> group in new[] { trayToggles, trayFooterToggles })
        {
            if (group.Count == 0) continue;
            if (actions.Count > 0) actions.Add(new TrayMenuSeparator());
            actions.AddRange(group);
        }
        if (context.TranscriptionPresent)
        {
            // Record runs the exact path of the transcription hotkey; the menu
            // closes first, as for any command. Its label follows the engine
            // state at each open. File transcription opens the system file
            // picker and runs each file through the dictation pipeline (see
            // App.FileTranscription.cs).
            if (actions.Count > 0) actions.Add(new TrayMenuSeparator());
            actions.Add(new TrayMenuCommand(
                "record",
                () => Loc.Get(_engine?.IsRecording == true ? "TrayPanel_StopRecording" : "TrayPanel_Record"),
                () => OnHotkey(RecordHotkeyId)));
            actions.Add(new TrayMenuCommand(
                "transcribe-files",
                () => Loc.Get("TrayMenu_TranscribeFile"),
                () => TranscribeFilesFromTray()));
        }
        // The app's own commands, always present.
        if (actions.Count > 0) actions.Add(new TrayMenuSeparator());
        actions.Add(new TrayMenuCommand("settings", () => Loc.Get("TrayMenu_Settings"), () => ShowSettingsWindowLazy()));
        actions.Add(new TrayMenuCommand("logs", () => Loc.Get("TrayMenu_Logs"), () => ShowLogWindowLazy()));
        actions.Add(new TrayMenuCommand("restart", () => Loc.Get("TrayMenu_Restart"), () => RestartAppFromTray()));
        actions.Add(new TrayMenuCommand("quit", () => Loc.Get("TrayMenu_Quit"), () => QuitApp()));
        _trayActions = new TrayMenuHost(_messageHost.Hwnd, "tray-actions")
        {
            GetIconRect = () => _tray.GetIconRect(),
            Entries = actions,
        };
        // The user incident lines go above the switches (App.TrayIncidents.cs);
        // the menu is recomposed at its next open after the registry changed.
        _tray.LeftClickRequested += ShowTrayActions;

        // Double click opens Settings. The shell delivers the first click's
        // WM_LBUTTONUP before WM_LBUTTONDBLCLK, so the menu opens, then the
        // Settings window takes activation and the menu hides; the trailing
        // WM_LBUTTONUP lands inside the menu's reopen guard and is ignored.
        _tray.DoubleClickRequested += () => ShowSettingsWindowLazy();

        // Before the icon goes live, so its first mark already carries the dot
        // of an incident opened during composition.
        InitializeTrayIncidents(actions);

        // Last: the icon goes live here, so every tray callback is wired first.
        // The manager creates the icon's own owner window, a hidden top-level
        // one: it has to receive TaskbarCreated, which never reaches the
        // message-only host.
        _tray.Register();

        // An absent module has no action, so its chord is never registered and
        // stays free for other apps.
        _hotkeyActions = BuildHotkeyActions(context, [.. trayToggles, .. trayFooterToggles]);
        // A profile shortcut starts a dictation, so it needs both modules.
        _profileHotkeysAvailable = context.TranscriptionPresent && context.RewritePresent;
        _profileHotkeyActions = BuildProfileHotkeyActions();
        _hotkeyManager = new HotkeyManager(_messageHost.Hwnd, OnHotkey, BuildHotkeyBindings());
        // Register throws only when the WM_HOTKEY subclass cannot be installed;
        // uncaught, that throw would stop the app from starting. Deckle-Shell
        // logs that failure with its Win32 error before throwing, so only
        // the person's feedback is left to give here.
        try
        {
            ReportHotkeyFailures(_hotkeyManager.Register());
            _hotkeysInstalled = true;
        }
        catch (Exception)
        {
            DeckleAppSource.Log.UserFeedbackEmitted(
                2, // Error
                Deckle.Localization.Loc.Get("Feedback_HotkeysUnavailable_Title"),
                Deckle.Localization.Loc.Get("Feedback_HotkeysUnavailable_Body"),
                1); // Overlay
        }
        Settings.SettingsService.Instance.Changed += OnShellSettingsChangedForHotkeys;
        // The profile shortcuts live in the Rewriting settings, saved apart.
        if (_profileHotkeysAvailable)
            Llm.Rewrite.LlmSettingsService.Instance.Changed += OnShellSettingsChangedForHotkeys;
        context.Milestone("hotkeys");

        // After the HUD exists: the restore shows its message there.
        RecoverInterruptedDictation();

        // No model warmup at boot: nothing sits in VRAM while the app is idle.
        // The engine primes the model on the first hotkey press. The one
        // exception is a take that died with its audio on disk: the recovery
        // above then loads the model at launch, because that audio is the only
        // copy of what was said.
    }
}

