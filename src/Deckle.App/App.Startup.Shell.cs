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
        // sub-wave 6b). `HudFeedbackSink` (Deckle.Diagnostics)
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

        context.Milestone("hudwindow");

        // Tray icon: raises LeftClickRequested and RightClickRequested; the
        // two surfaces (quick-actions panel, context menu) are created further
        // down and subscribe there.
        _tray = new TrayIconManager();
        context.Milestone("tray");

        // Engine events → UI. StatusChanged, TranscriptionFinished, etc. are
        // called from background threads; LogWindow and HudWindow marshal
        // internally via DispatcherQueue, UpdateStatus only calls
        // Shell_NotifyIcon (thread-safe). Engine logs are emitted by
        // module EventSource providers. Guarded: when speech isn't provisioned
        // the engine was never composed, so there is nothing to wire.
        if (_engine is not null)
        {
            _engine.FileTranscriptionStarted += () => _hudWindow.ShowFilePreparing();
            _engine.StatusChanged += status =>
            {
                // Beacon app icon in LogWindow + PlaygroundWindow: red =
                // recording, grey = idle. The engine state is the semantic
                // source of truth; the localized status remains display-only.
                bool isRecording = _engine.IsRecording;
                _tray.UpdateStatus(status, isRecording);
                _lastRecordingState = isRecording;
                // Both nullable now: LogWindow and PlaygroundWindow are lazy-
                // created on first user open, so they're absent until then.
                _logWindow?.SetRecordingState(isRecording);
                _playgroundWindow?.SetRecordingState(isRecording);

                // HUD: driven by status transition. Background thread → HudWindow
                // marshals internally via DispatcherQueue. Named transient states
                // use StartsWith so their ellipsis variants route correctly.
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
                    case TranscriptionOutcome.SavedToFile:
                        // File transcription: the transcript was written to disk
                        // (and copied to the clipboard). Nothing opens — the HUD
                        // message is the only completion signal.
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

            // Mic RMS → HUD recording outline. Fires ~20 Hz from the recording
            // audio thread; OnAudioLevel pushes into a CompositionPropertySet,
            // thread-safe per Composition's contract — no dispatcher needed.
            // Method group so it can be unsubscribed symmetrically later if
            // needed; no-op when the outline isn't attached (any non-Recording
            // state), so permanent subscription is fine.
            _engine.AudioLevel += _hudWindow.OnAudioLevel;
        }

        // Initial status — model loads on-demand at first hotkey, not at
        // startup. With speech unprovisioned the engine is absent, so the
        // tooltip says so rather than implying dictation is ready. A module
        // absent by choice is not a missing setup — the tooltip stays quiet.
        string initialStatus = _engine is not null || !context.TranscriptionPresent
            ? "Ready" : "Dictation not set up";
        _tray.UpdateStatus(initialStatus, isRecording: false);

        // Force Ambient master toggle OFF at boot; explicit user action via
        // Settings / tray re-enables the pipeline. Louis's explicit
        // preference: the app should not start screen capture + Hue traffic on
        // its own at launch. Subscribe the observer AFTER the force-off so the
        // Changed event does not bounce a Start call we just suppressed.
        // Presence-gated with the engine above: absent module, no observer.
        if (context.AmbientPresent)
        {
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
        }

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
            OnRestart        = () => RestartAppFromTray(),
            OnQuit           = () => QuitApp(),
            // Tray icon screen rect — the menu host uses it as the
            // CalculatePopupWindowPosition exclude rect so the popup anchors
            // tangent to the icon regardless of taskbar orientation.
            GetIconRect      = () => _tray.GetIconRect(),
        };

        _tray.RightClickRequested += () => _trayMenu.Show();

        // Tray quick-actions panel — left click. Same carrier pattern and
        // owner as the menu. Every entry follows module presence: an absent
        // module has no Record button, no file action, no switch.
        _trayPanel = new TrayQuickActionsHost(_messageHost.Hwnd)
        {
            GetIconRect = () => _tray.GetIconRect(),
            IsRecording = () => _engine?.IsRecording ?? false,
        };
        if (context.TranscriptionPresent)
        {
            // Record runs the exact path of the transcription hotkey; the
            // panel closes first so the paste at Stop reaches the window the
            // user was in. File transcription opens the system file picker
            // and runs each file through the dictation pipeline (see
            // App.FileTranscription.cs).
            _trayPanel.OnRecord = () => OnHotkey(NativeMethods.HOTKEY_ID_TRANSCRIBE);
            _trayPanel.OnTranscribeFiles = () => TranscribeFilesFromTray();
        }
        // Module switches (App.TrayToggles.cs); Ambient Light in the footer.
        _trayPanel.Toggles = BuildTrayToggles(context);
        _trayPanel.FooterToggles = BuildTrayFooterToggles(context);
        _tray.LeftClickRequested += () => _trayPanel.Show();

        _tray.Register(_messageHost.Hwnd);
        // Presence gates the chord itself: absent Dictation means no
        // transcription chord, leaving the OS-wide combo free for other apps,
        // symmetric with its settings page never registering.
        IReadOnlyList<GlobalHotkeyBinding> hotkeyBindings = context.TranscriptionPresent
            ? [new(NativeMethods.HOTKEY_ID_TRANSCRIBE,
                   NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT,
                   NativeMethods.SC_LEFT_OF_ONE)]
            : [];
        _hotkeyManager = new HotkeyManager(_messageHost.Hwnd, OnHotkey, hotkeyBindings);
        // Try/catch required: RegisterHotKey can fail with err 1409
        // (ERROR_HOTKEY_ALREADY_REGISTERED) when another process already owns
        // the chord; typically WhispInteropTest still running through the
        // scheduled Whisp task, or PowerToys / a third-party app that took
        // Win+1. Without this safety net, HotkeyManager's throw bubbles to
        // OnLaunched and the app refuses to start.
        //
        // Compromise: the app continues to start but without hotkeys. The tray
        // remains operational (Settings, Quit, Record in the left-click panel), so
        // the user is not locked out. UserFeedback Overlay informs visually
        // through the HUD at boot.
        try
        {
            if (_hotkeyManager.Register().Count > 0)
                throw new InvalidOperationException("The transcription chord is owned by another app.");
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"Hotkey registration failed: {ex.Message}");
            DeckleAppSource.Log.UserFeedbackEmitted(
                2, // Error
                "Hotkeys unavailable",
                "Another app owns the chord (often WhispInteropTest still running). Use the tray panel to record.",
                1); // Overlay
        }
        context.Milestone("hotkeys");

        // No model warmup at boot: nothing is loaded into VRAM while the app
        // is idle. The model is loaded + its kernels compiled on demand on the
        // first hotkey press, inside the engine worker (BeginPrime), now
        // concurrently with the capture — so the HUD goes straight to Recording
        // and the chrono ticks while the model warms behind it; it is freed again
        // after the idle timeout. There is no separate HUD composition warm at
        // boot anymore: the first real visible transition pays that cost through
        // the normal ShowNoActivate + fade-in path.
    }
}

