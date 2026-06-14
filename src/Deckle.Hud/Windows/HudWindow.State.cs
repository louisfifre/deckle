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

    public void ShowPreparing()        => EnqueueUI(() => SetState(HudState.Charging,    reason: "show_preparing"));
    public void ShowRecording()        => EnqueueUI(() => SetState(HudState.Recording,   reason: "show_recording"));
    public void SwitchToTranscribing() => EnqueueUI(() => SetState(HudState.Transcribing, reason: "switch_transcribing"));
    public void SwitchToRewriting()    => EnqueueUI(() => SetState(HudState.Rewriting,    reason: "switch_rewriting"));

    // Durations are severity-driven (see FeedbackDuration / SuccessDuration
    // below). Success and Informational clear fast, warnings and errors
    // linger so the user has time to read the actionable body.

    public void ShowError(string title, string body) =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Critical, title, body, FeedbackDuration(2)),
            reason: "show_error"));

    public void ShowPasted() =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Success, Loc.Get("Hud_Pasted_Title"), string.Empty,
                SuccessDuration),
            reason: "show_pasted"));

    // "Copied to clipboard" is a *success* outcome — the transcription
    // landed on the clipboard, which is the default flow when
    // AutoPasteEnabled is off (ship default). The green checkmark matches
    // the user's model: the operation succeeded. "Ctrl+V where you want it"
    // is a next-step hint, not a failure notice.
    public void ShowCopied() =>
        EnqueueUI(() => SetState(HudState.Message,
            new MessagePayload(MessageKind.Success,
                Loc.Get("Hud_Copied_Title"), Loc.Get("Hud_Copied_Hint"),
                SuccessDuration),
            reason: "show_copied"));

    // ─── Feedback routing ───────────────────────────────────────────────────
    //
    // Severity and duration arrive as primitives rather than as a
    // `UserFeedback` record: since sub-wave 6b, emission goes through the
    // `UserFeedbackEmitted(severity:int, title, body, role:int)` EventSource
    // channel exposed by each module provider (DeckleWhispSource, etc.). The
    // host sink (`AppHudFeedbackSink`) routes to the replica surface
    // (`ShowUserFeedback`) or stack (`HudOverlayManager.Enqueue`) according to
    // `role`.
    //
    // Severity convention: 0=Info, 1=Warning, 2+=Error (same ordinals as the
    // former `UserFeedbackSeverity` enum). Preserved exactly to avoid changing
    // this contract on the provider side.
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
    // Tuned by severity: warn/error linger, info clears quickly. Hardcoded
    // constants; a Settings knob here would add complexity for a value a user
    // never changes. `SuccessDuration` is shared with the HUD's internal
    // Success messages (ShowCopied / ShowPasted).

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
        // Threading: real and critical cross-thread site (transcribe thread →
        // UI rendezvous just before SendInput Ctrl+V). An abnormal wait_ms
        // here indicates a blocked UI thread that will trigger the defensive
        // timeout and propagate a paste race.
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
        // the race risk documented in src/Deckle.Transcription/CLAUDE.md
        // (Paste section), accepted in pathological cases.
        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            DeckleHudSource.Log.HudWarning();
            DeckleHudSource.Log.HudWarningDetail("HideSync timeout — UI thread didn't process within 5s, paste proceeding without Hide rendezvous");
        }
    }

    // ── State dispatcher ──────────────────────────────────────────────────────
    //
    // Single entry point for all UI transitions. Marshals control visibility,
    // forwards to the control's ApplyState / Show, shows the (fixed-size)
    // window, and arms the auto-hide timer for messages.

    // `reason` is a semantic trigger label propagated to
    // DeckleHudSource.StateChanged to distinguish "hide_sync",
    // "message_timeout", "show_recording", etc.; without it, reading the
    // LogWindow trace would mean guessing why the HUD just changed state.
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

        // Axis 1: StateChanged. Emitted before the concrete dispatch so the
        // sequence in the LogWindow reads the transition at the head of each
        // change. alpha is read before ApplyShowAlpha (therefore the "current"
        // pre-transition alpha); dpi is recalculated through GetDpiForWindow to
        // track a runtime DPI change between two shows.
        int dpiNow = (int)NativeMethods.GetDpiForWindow(_hwnd);
        DeckleHudSource.Log.StateChanged(from.ToString(), next.ToString(), reason, _currentAlpha, dpiNow);

        if (wasShown != isShown)
            MainHudVisibilityChanged?.Invoke(this, isShown);

        // Proximity rollup: Begin when becoming visible (initializes the
        // IsEnabled gate and arms the session stopwatch), End when becoming
        // Hidden (flushes the visibility window's single summary). No emission
        // between the two; the rollup is strictly per-session, not periodic.
        if (isShown && !wasShown)
            BeginProximitySession();
        else if (!isShown && wasShown)
            EndProximitySessionAndFlush();

        // Clock lifecycle — owned by the chrono control, driven from here, the
        // single transition dispatcher, NOT by the Apply* paint methods. A new
        // session zeroes the face, recording starts ticking, the Stop (the
        // instant-ack on the hotkey thread, or the later engine status) freezes
        // it. Kept out of ApplyState so the painted visual and the elapsed value
        // stay independent — see HudChrono's clock lifecycle remark. Gated with
        // the visible states (above the overlay-disabled early return), so a
        // disabled overlay never arms the vsync tick on a hidden control.
        switch (next)
        {
            case HudState.Charging:     Chrono.ResetClock(); break;
            case HudState.Recording:    Chrono.StartClock(); break;
            case HudState.Transcribing: Chrono.StopClock();  break;
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

    // Centralised alpha application for visible states. Two branches:
    //   - Hidden → visible transition with animations enabled → fade-in
    //     150ms cubic ease-out, proximity re-activated on completion.
    //   - Other cases (state switch while visible, animations off) →
    //     instant alpha, proximity activated immediately.
    private static bool ShouldFadeIn(bool wasShown) =>
        !wasShown && AnimationSystemSetting.AreClientAreaAnimationsEnabled();

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
