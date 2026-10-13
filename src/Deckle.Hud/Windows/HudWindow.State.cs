using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// HudWindow — public state API, SetState transition dispatcher, feedback
// durations, message-hide timer, and the show-alpha decision helpers.
public sealed partial class HudWindow : Window
{
    // ── Public API (thread-safe) ──────────────────────────────────────────────

    private bool _fileTranscriptionClockActive;

    public void ShowPreparing() => EnqueueUI(() =>
    {
        _fileTranscriptionClockActive = false;
        SetState(HudState.Charging, reason: "show_preparing");
    });
    public void ShowFilePreparing() => EnqueueUI(() =>
    {
        _fileTranscriptionClockActive = true;
        SetState(HudState.Charging, reason: "show_file_preparing");
    });
    public void ShowRecording()        => EnqueueUI(() => SetState(HudState.Recording,   reason: "show_recording"));
    public void SwitchToTranscribing() => EnqueueUI(() => SetState(HudState.Transcribing, reason: "switch_transcribing"));
    public void SwitchToRewriting()    => EnqueueUI(() => SetState(HudState.Rewriting,    reason: "switch_rewriting"));

    public void ShowError(string title, string body) =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Critical, title, body, FeedbackDuration(2)),
            reason: "show_error"));

    public void ShowPasted() =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Success, Loc.Get("Hud_Pasted_Title"), string.Empty,
                SuccessDuration),
            reason: "show_pasted"));

    // "Copied to clipboard" is a *success* outcome: the transcription landed
    // on the clipboard, the default flow when AutoPasteEnabled is off. "Ctrl+V
    // where you want it" is a next-step hint, not a failure notice.
    public void ShowCopied() =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Success,
                Loc.Get("Hud_Copied_Title"), Loc.Get("Hud_Copied_Hint"),
                SuccessDuration),
            reason: "show_copied"));

    // The saved .txt file is the primary outcome, so this is a *success*
    // message; the clipboard copy is a next-step hint, as in ShowCopied.
    public void ShowFileSaved() =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Success,
                Loc.Get("Hud_FileSaved_Title"), Loc.Get("Hud_FileSaved_Hint"),
                SuccessDuration),
            reason: "show_file_saved"));

    // ─── Feedback routing ───────────────────────────────────────────────────
    //
    // Feedback arrives through the `UserFeedbackEmitted(severity, title, body,
    // role)` EventSource event each module provider exposes; the host sink
    // (`AppHudFeedbackSink`) routes it here or to `HudOverlayManager.Enqueue`
    // by `role`. Severity convention 0=Info, 1=Warning, 2+=Error is a
    // contract shared with every provider.
    public void ShowUserFeedback(int severity, string title, string body)
    {
        MessageKind kind = severity switch
        {
            0 => MessageKind.Informational,
            1 => MessageKind.Warning,
            _ => MessageKind.Critical,
        };
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(kind, title, body, FeedbackDuration(severity)),
            reason: "user_feedback"));
    }

    // ─── Feedback durations ─────────────────────────────────────────────────
    //
    // Warnings and errors linger so the user can read the actionable body;
    // info clears quickly. Hardcoded: a Settings knob would add complexity for
    // a value a user never changes.

    internal static TimeSpan FeedbackDuration(int severity) => severity switch
    {
        0 => TimeSpan.FromSeconds(4),  // Info
        1 => TimeSpan.FromSeconds(8),  // Warning
        _ => TimeSpan.FromSeconds(8),  // Error
    };

    public void Hide() => EnqueueUI(() => SetState(HudState.Hidden, reason: "hide"));

    // Blocking variant: explicit rendezvous between the transcribe thread
    // and the UI thread. Called just before PasteFromClipboard so SW_HIDE
    // is effective before SendInput queues the Ctrl+V — otherwise the
    // hide can redistribute activation while the keystrokes are in flight
    // and the paste lands in the wrong target.
    public void HideSync()
    {
        if (DispatcherQueue.HasThreadAccess) { SetState(HudState.Hidden, reason: "hide_sync"); return; }
        var done = new ManualResetEventSlim();
        bool enqueued = DispatcherQueue.TryEnqueueObserved(
            "window-show", "hud-window-hide-sync",
            () =>
            {
                try { SetState(HudState.Hidden, reason: "hide_sync"); } finally { done.Set(); }
            },
            "HUD", "HideSync");

        // If enqueue failed (queue closed during teardown), avoid an infinite
        // Wait by releasing immediately. The HUD will not be hidden, but the
        // caller (transcribe thread) must continue so the paste can proceed.
        if (!enqueued) { done.Set(); return; }

        // Defensive timeout: SetState takes microseconds under normal
        // conditions, but if the UI thread is blocked (composition glitch,
        // external deadlock), release the caller instead of hanging the
        // pipeline. Paste will be emitted without the Hide rendezvous, creating
        // a race risk, accepted in pathological cases.
        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            DeckleHudSource.Log.HudWarning();
            DeckleHudSource.Log.HudWarningDetail("HideSync timeout — UI thread didn't process within 5s, paste proceeding without Hide rendezvous");
        }
    }

    // ── State dispatcher ──────────────────────────────────────────────────────
    //
    // Single entry point for every HUD transition.

    // `reason` labels the trigger in the StateChanged event, so the trace says
    // why the HUD changed state.
    private void SetState(HudState next, MessagePayload? msg = null, string reason = "unspecified")
    {
        // Overlay disabled in Settings → no-op for any *visible* state. Hidden
        // still runs so an in-flight HUD gets cleared if the user toggles.
        if (next != HudState.Hidden && !Settings.SettingsService.Instance.Current.Overlay.Enabled)
        {
            return;
        }

        HudState from = _state;
        bool wasShown = _state != HudState.Hidden;
        _state = next;
        bool isShown = _state != HudState.Hidden;

        _messageHideTimer?.Stop();

        // Emitted before the dispatch, with the pre-transition alpha, so the
        // trace reads each transition first.
        int dpiNow = (int)NativeMethods.GetDpiForWindow(_hwnd);
        DeckleHudSource.Log.StateChanged(from.ToString(), next.ToString(), reason, _currentAlpha, dpiNow);

        if (wasShown != isShown)
            MainHudVisibilityChanged?.Invoke(this, isShown);

        if (isShown && !wasShown)
            BeginProximitySession();
        else if (!isShown && wasShown)
            EndProximitySessionAndFlush();

        // Clock lifecycle, driven from this dispatcher rather than the Apply*
        // paint methods, so the painted look and the elapsed value stay
        // independent. It sits after the overlay-disabled return, so a disabled
        // overlay never arms the vsync tick on a hidden control.
        switch (next)
        {
            case HudState.Charging:
                if (_fileTranscriptionClockActive) Chrono.StartClock();
                else Chrono.ResetClock();
                break;
            case HudState.Recording:
                _fileTranscriptionClockActive = false;
                Chrono.StartClock();
                break;
            case HudState.Transcribing:
                if (!_fileTranscriptionClockActive) Chrono.StopClock();
                break;
            case HudState.Rewriting:
            case HudState.Message:
            case HudState.Hidden:
                Chrono.StopClock();
                _fileTranscriptionClockActive = false;
                break;
        }

        switch (next)
        {
            case HudState.Hidden:
                CancelFadeIn();
                Chrono.ApplyState(HudState.Hidden);
                Chrono.Visibility  = Visibility.Visible;
                Message.Visibility = Visibility.Collapsed;
                DisableProximity();
                SetAlphaImmediate(MAX_ALPHA);
                IconAssets.ApplyToWindow(AppWindow, recording: false);
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
                return;

            case HudState.Message:
                if (msg is null)
                    throw new ArgumentNullException(nameof(msg), "Message state requires a payload");
                Chrono.ApplyState(HudState.Hidden);
                Chrono.Visibility  = Visibility.Collapsed;
                Message.Visibility = Visibility.Visible;
                Message.Show(msg);
                IconAssets.ApplyToWindow(AppWindow, recording: false);
                bool shouldFadeMessage = ShouldFadeIn(wasShown);
                if (shouldFadeMessage) SetAlphaImmediate(0);
                ShowNoActivate();
                ApplyShowAlpha(targetAlpha: MAX_ALPHA, shouldFadeIn: shouldFadeMessage);
                ArmMessageHideTimer(msg.Duration);
                return;

            case HudState.Charging:
            case HudState.Recording:
            case HudState.Transcribing:
            case HudState.Rewriting:
                Chrono.ApplyState(next);
                Message.Visibility = Visibility.Collapsed;
                Chrono.Visibility  = Visibility.Visible;
                IconAssets.ApplyToWindow(AppWindow, recording: next == HudState.Recording);
                bool shouldFadeChrono = ShouldFadeIn(wasShown);
                if (shouldFadeChrono) SetAlphaImmediate(0);
                ShowNoActivate();
                ApplyShowAlpha(targetAlpha: MAX_ALPHA, shouldFadeIn: shouldFadeChrono);
                return;
        }
    }

    private static bool ShouldFadeIn(bool wasShown) =>
        !wasShown && SystemAnimationPreference.Instance.AnimationsEnabled;

    private void ApplyShowAlpha(byte targetAlpha, bool shouldFadeIn)
    {
        if (shouldFadeIn)
        {
            StartFadeIn(targetAlpha, activateProximityOnComplete: true);
            return;
        }

        CancelFadeIn();
        SetAlphaImmediate(targetAlpha);
        if (Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity)
            EnableProximity();
        else
            DisableProximity();
    }

    private void ArmMessageHideTimer(TimeSpan duration)
    {
        _messageHideTimer ??= DispatcherQueue.CreateTimer();
        _messageHideTimer.Stop();
        _messageHideTimer.Interval = duration;
        _messageHideTimer.IsRepeating = false;
        _messageHideTimer.Tick -= OnMessageHideTick;
        _messageHideTimer.Tick += OnMessageHideTick;
        _messageHideTimer.Start();
    }

    private void OnMessageHideTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        SetState(HudState.Hidden, reason: "message_timeout");
    }
}
