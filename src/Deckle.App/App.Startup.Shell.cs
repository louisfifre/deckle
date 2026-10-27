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
using Deckle.Speech;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

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
        context.Milestone("tray");

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
                _tray.UpdateStatus(status, isRecording);
                _lastRecordingState = isRecording;
                _logWindow?.SetRecordingState(isRecording);
                _playgroundWindow?.SetRecordingState(isRecording);
                _settingsWindow?.SetRecordingState(isRecording);

                // StartsWith matches the en-US resource text, the only one
                // shipped, and its ellipsis variants; a translation breaks it.
                if (isRecording)
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
                        _hudWindow.ShowPasted();
                        break;
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
            // Synchronous rendezvous just before paste: hide the HUD and wait for
            // SW_HIDE to be effective on the UI thread before the engine sends
            // SendInput. Avoids the race where Hide() (triggered async after paste)
            // redistributes activation while Ctrl+V is still in the target's input queue.
            _engine.OnReadyToPaste = () => _hudWindow.HideSync();

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
            ? "Ready" : "Dictation not set up";
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
        }

        // Tray menus — two native MenuFlyouts, each on an invisible anchor
        // window placed on the tray icon and owned by the message-only HWND
        // (TrayMenuHost). The App composes their entries here; the module
        // renders them and knows no label and no module. Right click: the rare
        // items, the window-opening commands then the lifecycle commands.
        _trayMenu = new TrayMenuHost(_messageHost.Hwnd, "tray-popup")
        {
            GetIconRect = () => _tray.GetIconRect(),
            Entries =
            [
                new TrayMenuCommand("logs",       () => Loc.Get("TrayMenu_Logs"),       () => ShowLogWindowLazy()),
                new TrayMenuCommand("settings",   () => Loc.Get("TrayMenu_Settings"),   () => ShowSettingsWindowLazy()),
                new TrayMenuCommand("playground", () => Loc.Get("TrayMenu_Playground"), () => ShowPlaygroundLazy()),
                new TrayMenuSeparator(),
                new TrayMenuCommand("restart",    () => Loc.Get("TrayMenu_Restart"),    () => RestartAppFromTray()),
                new TrayMenuCommand("quit",       () => Loc.Get("TrayMenu_Quit"),       () => QuitApp()),
            ],
        };
        _tray.RightClickRequested += () => _trayMenu.Show();

        // Left click, three groups behind separators, read from the top: the
        // background switches (set once, left on or off), then Ambient Light
        // on its own (a visual thing one actually launches), then the actions
        // (Record or Stop recording, Transcribe audio files…) at the bottom,
        // nearest the icon on a taskbar at the bottom, so a command is one
        // short move away (Louis, 2026-10-14). A switch is a row with a pill
        // (TraySwitchMenuItem); a click flips its module and closes the menu,
        // like any menu item. Every entry follows module presence: an absent
        // module has no switch, no Record item, no file action.
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
            // closes first so the paste at Stop reaches the window the user
            // was in. Its label follows the engine state at each open. File
            // transcription opens the system file picker and runs each file
            // through the dictation pipeline (see App.FileTranscription.cs).
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
                Deckle.Catalog.Loc.Get("Feedback_HotkeysUnavailable_Title"),
                Deckle.Catalog.Loc.Get("Feedback_HotkeysUnavailable_Body"),
                1); // Overlay
        }
        Settings.SettingsService.Instance.Changed += OnShellSettingsChangedForHotkeys;
        context.Milestone("hotkeys");

        // After the HUD exists: the restore shows its message there.
        RestoreInterruptedDictation();

        // No model warmup at boot: nothing sits in VRAM while the app is idle.
        // The engine primes the model on the first hotkey press.
    }
}

