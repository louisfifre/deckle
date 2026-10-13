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
                _tray.UpdateStatus(status, isRecording);
                _lastRecordingState = isRecording;
                _logWindow?.SetRecordingState(isRecording);
                _playgroundWindow?.SetRecordingState(isRecording);

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
                DeckleAppSource.Log.AmbientMasterForcedOff();
            }

            AmbientSettingsService.Instance.Changed += OnAmbientSettingsChanged;
        }

        // Owned by the message-only HWND so its popup inherits the tray's z-order
        // and activation stack.
        _trayMenu = new TrayContextMenuHost(_messageHost.Hwnd)
        {
            OnShowLogs       = () => ShowLogWindowLazy(),
            OnShowSettings   = () => ShowSettingsWindowLazy(),
            OnShowPlayground = () => ShowPlaygroundLazy(),
            OnRestart        = () => RestartAppFromTray(),
            OnQuit           = () => QuitApp(),
            GetIconRect      = () => _tray.GetIconRect(),
        };

        _tray.RightClickRequested += () => _trayMenu.Show();

        _trayPanel = new TrayQuickActionsHost(_messageHost.Hwnd)
        {
            GetIconRect = () => _tray.GetIconRect(),
            IsRecording = () => _engine?.IsRecording ?? false,
        };
        if (context.TranscriptionPresent)
        {
            _trayPanel.OnRecord = () => OnHotkey(RecordHotkeyId);
            _trayPanel.OnTranscribeFiles = () => TranscribeFilesFromTray();
        }
        // Built once: the hotkey actions below derive from the same lists, so a
        // toggle's hotkey runs exactly what its panel switch runs.
        IReadOnlyList<TrayToggle> trayToggles = BuildTrayToggles(context);
        IReadOnlyList<TrayToggle> trayFooterToggles = BuildTrayFooterToggles(context);
        _trayPanel.Toggles = trayToggles;
        _trayPanel.FooterToggles = trayFooterToggles;
        _tray.LeftClickRequested += () => _trayPanel.Show();

        // Double click opens Settings. The shell delivers the first click's
        // WM_LBUTTONUP before WM_LBUTTONDBLCLK, so the panel opens, then the
        // Settings window takes activation and the panel hides; the trailing
        // WM_LBUTTONUP lands inside the panel's reopen guard and is ignored.
        _tray.DoubleClickRequested += () => ShowSettingsWindowLazy();

        // Last: the icon goes live here, so every tray callback is wired first.
        _tray.Register(_messageHost.Hwnd);

        // An absent module has no action, so its chord is never registered and
        // stays free for other apps.
        _hotkeyActions = BuildHotkeyActions(context, [.. trayToggles, .. trayFooterToggles]);
        _hotkeyManager = new HotkeyManager(_messageHost.Hwnd, OnHotkey, BuildHotkeyBindings());
        // Register throws only when the WM_HOTKEY subclass cannot be installed;
        // uncaught, that throw would stop the app from starting.
        try
        {
            ReportHotkeyFailures(_hotkeyManager.Register());
            _hotkeysInstalled = true;
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"Hotkey registration failed: {ex.Message}");
            DeckleAppSource.Log.UserFeedbackEmitted(
                2, // Error
                "Hotkeys unavailable",
                "Deckle could not listen for its shortcuts. Use the tray panel to record.",
                1); // Overlay
        }
        Settings.SettingsService.Instance.Changed += OnShellSettingsChangedForHotkeys;
        context.Milestone("hotkeys");

        // No model warmup at boot: nothing sits in VRAM while the app is idle.
        // The engine primes the model on the first hotkey press.
    }
}

