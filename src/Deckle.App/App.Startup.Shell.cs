using Deckle.Autocorrect;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Input.PrecisionScroll;
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

        // Tray icon : only left-click action lives here ; the right-click
        // context menu is rendered by TrayContextMenuHost (created later,
        // after the message-only host HWND is available to serve as owner).
        _tray = new TrayIconManager();
        if (context.TranscriptionPresent)
        {
            // Left-click tray = toggle transcription via the same path as the
            // standard hotkey. An absent Dictation module leaves the click
            // unbound instead of pointing to a Settings page that is absent too.
            _tray.OnToggleRecording = () => OnHotkey(NativeMethods.HOTKEY_ID_TRANSCRIBE);
        }
        context.Milestone("tray");

        // Engine events → UI. StatusChanged, TranscriptionFinished, etc. are
        // called from background threads; LogWindow and HudWindow marshal
        // internally via DispatcherQueue, UpdateStatus only calls
        // Shell_NotifyIcon (thread-safe). Engine logs are emitted by
        // module EventSource providers. Guarded: when speech isn't provisioned
        // the engine was never composed, so there is nothing to wire.
        if (_engine is not null)
        {
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
            // Taskbar cover tray entry — the pill
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
            IsPrecisionScrollOn = () => PrecisionScrollSettingsService.Instance.Current.Enabled,
            OnTogglePrecisionScroll = () =>
            {
                var s = PrecisionScrollSettingsService.Instance.Current;
                s.Enabled = !s.Enabled;
                PrecisionScrollSettingsService.Instance.Save();
            },
            OnRestart        = () => RestartAppFromTray(),
            OnQuit           = () => QuitApp(),
            // Tray icon screen rect — the menu host uses it as the
            // CalculatePopupWindowPosition exclude rect so the popup anchors
            // tangent to the icon regardless of taskbar orientation.
            GetIconRect      = () => _tray.GetIconRect(),
        };

        // Module-owned tray entries follow presence: the menu host shows an
        // item only when its delegate is wired, so an absent module's command
        // simply never appears (see TrayContextMenuHost.PrimeFlyout).
        if (context.TranscriptionPresent)
        {
            // File transcription — opens the system file picker and runs each
            // chosen audio file through the same pipeline as dictation (see
            // App.FileTranscription.cs). Delivered on the UI thread the tray
            // click arrives on.
            _trayMenu.OnTranscribeFiles = () => TranscribeFilesFromTray();
        }
        if (context.AmbientPresent)
        {
            // Ambient Light tray entry — checkmark mirrors the persisted
            // AmbientSettings.Enabled, click flips it. The actual engine
            // start/stop reacts via the AmbientSettingsService.Changed
            // observer wired above.
            _trayMenu.IsAmbientOn     = () => AmbientSettingsService.Instance.Current.Enabled;
            _trayMenu.OnToggleAmbient = () =>
            {
                var s = AmbientSettingsService.Instance.Current;
                s.Enabled = !s.Enabled;
                AmbientSettingsService.Instance.Save();
            };
        }
        if (context.AutocorrectPresent)
        {
            _trayMenu.IsAutocorrectOn = () => AutocorrectSettingsService.Instance.Current.Enabled;
            _trayMenu.OnToggleAutocorrect = () =>
            {
                bool enabled = AutocorrectSettingsService.Instance.Current.Enabled;
                AutocorrectSettingsService.Instance.SetEnabled(!enabled);
            };
        }
        _tray.RightClickRequested += () => _trayMenu.Show();

        _tray.Register(_messageHost.Hwnd);
        // Presence gates the chords themselves: absent Dictation means no
        // transcription chord; absent Rewrite leaves only plain transcription.
        // The unused OS-wide combos stay free for other apps, symmetric with
        // their settings pages never registering.
        IReadOnlyList<int> hotkeyIds = HotkeySelection.ForModulePresence(
            context.TranscriptionPresent,
            context.RewritePresent);
        _hotkeyManager = new HotkeyManager(_messageHost.Hwnd, OnHotkey, hotkeyIds);
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

