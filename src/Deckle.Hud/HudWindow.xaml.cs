using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Deckle.Core.Interop;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// ─── HUD bottom-center ───────────────────────────────────────────────────────
//
// WinUI 3 transient window, never destroyed. Fixed 272x78 in dips for every
// state. Hosts two UserControls swapped via Visibility:
//   - HudChrono   — Charging / Recording / Transcribing / Rewriting
//   - HudMessage  — Pasted / Copied / Error / UserFeedback
//
// This window owns only the technical shell: HWND, layered alpha,
// proximity fade subclass, lifecycle. All visual logic lives in the
// controls.
//
// The HUD is fully opaque — no transparency, no SystemBackdrop. The card
// visual is the hosted UserControl's Border (fill + corner radius from
// theme resources). The HWND itself is rounded by DWM via
// DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND so the sliver between the
// card's rounded corners and the HWND rectangle is never visible — DWM
// clips the compositor output to a rounded shape. DWMWA_BORDER_COLOR is
// set to DWMWA_COLOR_NONE to suppress the 1-dip DWM accent stroke.
//
// No classical Win32 frame is painted because WM_NCCALCSIZE is intercepted
// in the subclass and returns a zero non-client area. WinUI 3 reapplies
// WS_DLGFRAME / WS_EX_WINDOWEDGE on the top-level HWND even after we strip
// them (diagnosed 2026-04-17), so stripping bits is a losing game — we
// leave them on and simply deny Windows any NC area to paint into.
//
// Mouse proximity:
//   - WS_EX_LAYERED + SetLayeredWindowAttributes(LWA_ALPHA) gives a global
//     alpha covering the whole window content. Updated on every WM_INPUT
//     through a smoothstep — no polling.
//   - Raw Input (RIDEV_INPUTSINK) ensures WM_INPUT arrives even though the
//     HUD never owns focus.
//   - Subclass delegate kept in an instance field to survive GC.
public sealed partial class HudWindow : Window
{
    private const int HUD_WIDTH         = 272;
    private const int HUD_HEIGHT        =  78;
    private const int HUD_BOTTOM_MARGIN =  96;

    // Continuous fade: alpha mapped to cursor/HUD distance through smoothstep.
    //   distance >= FAR_RADIUS → alpha MAX_ALPHA (full HUD)
    //   distance <= NEAR_RADIUS → alpha MIN_ALPHA (faded HUD)
    //   between the two → smoothstep (t²(3-2t)).
    // No animation: each WM_INPUT recalculates and applies the target alpha.
    private const double NEAR_RADIUS_DIP = 10;
    private const double FAR_RADIUS_DIP  = 128;
    private const byte   MAX_ALPHA       = 255;
    private const byte   MIN_ALPHA       = 40;

    private readonly IntPtr _hwnd;

    private byte _currentAlpha = MAX_ALPHA;
    private bool _proximityActive;
    private bool _rawInputRegistered;

    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x48554450); // "HUDP"

    private HudState _state = HudState.Hidden;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _messageHideTimer;
    private int _zOrderProbeGeneration;

    // Proximity rollup: sample-by-sample collection through UpdateProximity
    // (WM_INPUT, ~125 Hz) over the full HUD visibility window. A summary is
    // emitted only once on the shown → hidden transition through
    // EndProximitySessionAndFlush. The IsEnabled gate is tested twice: at
    // session start (to decide whether to collect) and at flush time (to
    // confirm a listener is still attached). The aggregator is isolated for
    // unit testability; HudWindow only wires the lifecycle. Stopwatch provides
    // the session's actual duration_ms.
    private readonly ProximityRollupAggregator _proximityRollup = new();
    private bool _proximityRollupEnabled;
    private System.Diagnostics.Stopwatch? _proximitySessionStopwatch;

    // Fade-in on first show (Hidden → visible), 150ms cubic ease-out matching
    // LayeredAlphaAnimator. Proximity update is suspended during the fade and
    // re-activated on completion.
    private const int FADE_IN_MS = 150;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _fadeInTimer;
    private DateTime _fadeInStartUtc;
    private byte _fadeInTarget;
    private bool _fadeInActivateProximityOnComplete;

    // Raised when the HUD transitions between visible and hidden. Used by
    // HudOverlayManager to slide cards into / out of the main HUD's slot
    // (slot 0 drops onto the HUD's position while the HUD is hidden).
    public event EventHandler<bool>? MainHudVisibilityChanged;

    public IntPtr Hwnd             => _hwnd;
    public bool   IsMainHudShown   => _state != HudState.Hidden;

    public HudWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        // Explicit null defeats any auto-applied Mica / Acrylic backdrop on
        // recent WindowsAppSDK versions. Paired with DWMWA_SYSTEMBACKDROP_TYPE
        // = DWMSBT_NONE below as belt-and-suspenders — one is the WinUI API
        // surface, the other is the DWM Win32 guarantee.
        SystemBackdrop = null;

        // Title + icon kept consistent with the other windows. Title is not
        // visible (no title bar) but surfaces in alt-tab / Task View / debug.
        Title = Loc.Get("Hud_WindowTitle");
        IconAssets.ApplyToWindow(AppWindow);

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable   = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // Round the HWND at the DWM level, request the system default accent
        // border stroke, and force DWMSBT_NONE on the backdrop so DWM paints
        // nothing behind our opaque content. DWMWCP_ROUND matches Windows 11's
        // standard radius; DWMWA_COLOR_DEFAULT on DWMWA_BORDER_COLOR tells DWM
        // to paint the 1-dip system-native frame stroke around the rounded
        // HWND silhouette (tracks theme/accent) — this is the "Windows default
        // frame" visible on every first-party Win11 app. DWMSBT_NONE
        // explicitly disables Mica / Acrylic (belt-and-suspenders with
        // SystemBackdrop = null above).
        uint cornerPref = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPref, sizeof(uint));
        uint borderColor = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_BORDER_COLOR,
            ref borderColor, sizeof(uint));
        uint backdropType = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdropType, sizeof(uint));

        // WS_EX_LAYERED is the precondition for SetLayeredWindowAttributes.
        // WS_EX_TOOLWINDOW keeps the HUD out of alt-tab. WS_EX_NOACTIVATE
        // makes the no-focus-steal contract native rather than only relying
        // on SW_SHOWNOACTIVATE. WS_EX_TRANSPARENT forwards mouse hits beneath
        // the window; proximity fade still runs through Raw Input.
        var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(
                ex
                | NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT));
        NativeMethods.SetLayeredWindowAttributes(
            _hwnd, 0, MAX_ALPHA, NativeMethods.LWA_ALPHA);

        // Subclass MUST be installed before SWP_FRAMECHANGED — Windows sends
        // WM_NCCALCSIZE in response to FRAMECHANGED, and the subclass's
        // WM_NCCALCSIZE handler is what erases the non-client area. Installed
        // after the layered ex-style so LAYERED is in place when NC calc runs.
        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);

        // SWP_FRAMECHANGED triggers WM_NCCALCSIZE, which now routes through
        // the subclass and returns a zero NC area. Net effect: the remaining
        // WS_DLGFRAME / WS_EX_WINDOWEDGE bits on the top-level HWND (which
        // WinUI 3 reapplies behind our back whenever we try to strip them)
        // have no NC area to paint into — they become inert.
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        RegisterMouseRawInput();

        // Theme: wires ActualThemeChanged on the XAML root to trace
        // light/dark/HC transitions. `RequestedTheme` is set by App.ApplyTheme
        // through Push("user"/"app-init") on the probe; a system change
        // (Personalization) arrives without a pending value and is labeled
        // "system". The HUD does not manually reapply brushes on theme change
        // (HudChrono does that for its chronometer; see its own subscription
        // site), so this event is purely observational here.
        if (Content is Microsoft.UI.Xaml.FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }

        // Never destroyed — only path out is the tray Quit menu.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            Hide();
        };
    }

    // ── Theme tracing ────────────────────────────────────────────────────────
    //
    // Stores the last known ActualTheme value to build the (from, to) pair
    // expected by DeckleThemeSource.ThemeChanged. Initialized in the ctor from
    // Content.ActualTheme and updated on each event.
    private Microsoft.UI.Xaml.ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(Microsoft.UI.Xaml.FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged(
            "hud", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    private void RegisterMouseRawInput()
    {
        var rid = new RAWINPUTDEVICE[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage     = 0x02,
                dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget  = _hwnd,
            }
        };
        _rawInputRegistered = NativeMethods.RegisterRawInputDevices(
            rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

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
    internal static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(2);

    internal static TimeSpan FeedbackDuration(int severity) => severity switch
    {
        0 => TimeSpan.FromSeconds(4),  // Info
        1 => TimeSpan.FromSeconds(8),  // Warning
        _ => TimeSpan.FromSeconds(8),  // Error
    };

    public void Hide() => EnqueueUI(() => SetState(HudState.Hidden, reason: "hide"));

    // Forward mic RMS samples (20 Hz, engine recording thread) to the chrono
    // control without marshalling. HudChrono.UpdateAudioLevel writes to a
    // CompositionPropertySet scalar, which is thread-safe by Composition's
    // contract — going through the dispatcher would add latency for no gain.
    // Safe to call at any state: UpdateAudioLevel is a no-op when the
    // recording outline isn't attached.
    public void OnAudioLevel(float rms) => Chrono.UpdateAudioLevel(rms);

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
                _proximityActive   = false;
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
        _proximityActive = Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity;
        if (_proximityActive) UpdateProximity();
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

    // ── Implementation ────────────────────────────────────────────────────────

    private void EnqueueUI(Action a)
    {
        if (DispatcherQueue.HasThreadAccess) a();
        else
        {
            // Threading: central point for the HUD's cross-thread marshalling
            // (engine StatusChanged from worker, composition callbacks outside
            // UI). TryEnqueueObserved instruments MarshalQueued →
            // wait_ms/run_ms → MarshalCompleted; rejection goes through
            // DispatcherEnqueueRejected on DeckleThreadingSource if the queue
            // is closed.
            DispatcherQueue.TryEnqueueObserved(
                "ui-update", "hud-window",
                () => a(),
                "HUD", "ui action");
        }
    }

    // Pixel rect the HUD would occupy at the current DPI + work area +
    // Overlay.Position setting, regardless of visibility. HudOverlayManager
    // reads this to lay out the stack even when the HUD itself is hidden.
    public Windows.Graphics.RectInt32 GetRectPx()
    {
        var wa = DisplayArea.Primary.WorkArea;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi / 96.0;

        int w = (int)Math.Round(HUD_WIDTH  * scale);
        int h = (int)Math.Round(HUD_HEIGHT * scale);
        int margin = (int)Math.Round(HUD_BOTTOM_MARGIN * scale);

        // HUD centered horizontally by design (mirrors native Win11 HUDs —
        // volume, brightness, screen capture). Only vertical anchor is user-
        // configurable. StartsWith covers legacy corner values from older
        // settings.json files.
        string position = Settings.SettingsService.Instance.Current.Overlay.Position ?? "";
        int x = wa.X + (wa.Width - w) / 2;
        int y = position.StartsWith("Top")
            ? wa.Y + margin
            : wa.Y + wa.Height - h - margin;

        return new Windows.Graphics.RectInt32(x, y, w, h);
    }

    private void ShowNoActivate()
    {
        // Recomputed on every show: a Windows DPI scale change between two
        // dictations (125% → 150%) is reflected immediately.
        var rect = GetRectPx();
        AppWindow.MoveAndResize(rect);

        WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", "before_show_noactivate");
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", "after_show_noactivate");
        bool setposOk = NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW);
        int setposError = setposOk ? 0 : Marshal.GetLastWin32Error();
        WindowingProbe.EmitWindowZOrderState(
            _hwnd, "hud", "after_setwindowpos_topmost",
            setposOk, setposError);
        int zOrderProbeGeneration = ++_zOrderProbeGeneration;
        EmitDelayedZOrderState("after_setwindowpos_topmost_50ms", 50, zOrderProbeGeneration, setposOk, setposError);
        EmitDelayedZOrderState("after_setwindowpos_topmost_250ms", 250, zOrderProbeGeneration, setposOk, setposError);

        // Windowing: emitted after MoveAndResize + ShowWindow to capture the
        // effective post-DWM rect. `anchor` reflects the
        // Settings.Overlay.Position setting (BottomCenter default, TopCenter
        // alternative); DPI/work area/horizontal centering wrapping lives in
        // GetRectPx, but we capture the result rather than the intent to allow
        // reversal through dpi.
        string position = Settings.SettingsService.Instance.Current.Overlay.Position ?? "";
        string anchor = position.StartsWith("Top") ? "TopCenter" : "BottomCenter";
        WindowingProbe.EmitWindowPositioned(_hwnd, "hud", anchor);
    }

    private void EmitDelayedZOrderState(string stage, int delayMs, int generation, bool setposOk, int setposError)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        timer.IsRepeating = false;
        timer.Tick += (sender, _) =>
        {
            sender.Stop();
            if (generation != _zOrderProbeGeneration) return;
            WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", stage, setposOk, setposError);
        };
        timer.Start();
    }

    // ── Subclass: WM_NCCALCSIZE (no-frame) + WM_INPUT (proximity) ─────────────

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        // Erase the non-client area. With wParam=TRUE, lParam points to a
        // NCCALCSIZE_PARAMS whose rgrc[0] holds the proposed window rect;
        // returning 0 and leaving that rect unchanged tells Windows "the
        // client area covers the full window". No caption, no frame, no
        // 3D edge. wParam=FALSE hits here with a plain RECT — same contract,
        // we leave it as-is. Neutralizes WS_DLGFRAME + WS_EX_WINDOWEDGE
        // that WinUI 3 reapplies to the top-level behind our back.
        //
        // Verified 2026-04-17: removing this handler (even with
        // ExtendsContentIntoTitleBar already off) brings the rectangular
        // outline back immediately. The handler is load-bearing.
        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }

        if (uMsg == NativeMethods.WM_INPUT && _proximityActive)
        {
            UpdateProximity();
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Proximity: distance → alpha via smoothstep ────────────────────────────

    private void UpdateProximity()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var pos  = AppWindow.Position;
        var size = AppWindow.Size;
        int left   = pos.X;
        int top    = pos.Y;
        int right  = pos.X + size.Width;
        int bottom = pos.Y + size.Height;

        int dx = cursor.X < left ? left - cursor.X : (cursor.X > right  ? cursor.X - right  : 0);
        int dy = cursor.Y < top  ? top  - cursor.Y : (cursor.Y > bottom ? cursor.Y - bottom : 0);
        double distancePx = Math.Sqrt(dx * dx + dy * dy);

        double scale  = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        double nearPx = NEAR_RADIUS_DIP * scale;
        double farPx  = FAR_RADIUS_DIP  * scale;

        double t = (distancePx - nearPx) / (farPx - nearPx);
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;

        double eased = t * t * (3.0 - 2.0 * t);

        byte alpha = (byte)Math.Round(MIN_ALPHA + eased * (MAX_ALPHA - MIN_ALPHA));
        if (alpha != _currentAlpha) SetAlphaImmediate(alpha);

        // Axis 5: sample collection for ProximityRollup. The
        // _proximityRollupEnabled flag short-circuits collection when no
        // listener is attached to Verbose+Heartbeat on Deckle.Hud; this is the
        // strict gate required by the deckle-logging doctrine for
        // high-frequency WM_INPUT loops (~125 Hz). Re-evaluated at the start
        // of each visibility session to absorb a listener live toggle between
        // two shows.
        if (_proximityRollupEnabled)
        {
            int distDip = (int)Math.Round(distancePx / scale);
            _proximityRollup.Add(distDip, alpha);
        }
    }

    // ── Proximity rollup — per-session HUD visibility summary ──────────
    //
    // WM_INPUT arrives at ~125 Hz when the mouse moves; deckle-logging
    // doctrine forbids emitting one event per tick. A previous 1 s periodic
    // variant produced up to ~10 events per HUD session (50 sessions ×
    // ~10 s/day = ~500 events/day in LogWindow) with no diagnostic value on
    // sessions where the mouse did not approach. The current pattern
    // aggregates the full visibility window (shown → hidden) and emits one
    // summary under two cumulative conditions: at least one sample collected
    // AND min_alpha != max_alpha (otherwise smoothstep stayed flat and there
    // is no proximity trajectory to diagnose).

    private void BeginProximitySession()
    {
        // Evaluates the gate at session start; when closed, collection is
        // short-circuited in UpdateProximity (_proximityRollupEnabled test).
        // If a listener attaches during the session, nothing is recorded late;
        // the next show will capture the new gate.
        _proximityRollupEnabled = DeckleHudSource.Log.IsEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat);
        if (!_proximityRollupEnabled) return;

        _proximityRollup.Reset();
        _proximitySessionStopwatch = System.Diagnostics.Stopwatch.StartNew();
    }

    private void EndProximitySessionAndFlush()
    {
        if (!_proximityRollupEnabled) return;

        var sw = _proximitySessionStopwatch;
        _proximitySessionStopwatch = null;
        _proximityRollupEnabled = false;

        int samples = _proximityRollup.TotalSamples;
        if (samples == 0) return;

        byte minAlpha = _proximityRollup.MinAlpha;
        byte maxAlpha = _proximityRollup.MaxAlpha;

        // Skip if min == max: the mouse did not enter the proximity radius,
        // smoothstep stayed flat, and there is no trajectory to diagnose. The
        // "every emission carries diagnostic value" doctrine requires this
        // gate, otherwise the LogWindow is drowned in "nothing happened"
        // summaries on typical HUD sessions where the user does not approach
        // the HUD.
        if (minAlpha == maxAlpha) return;

        // Re-test the gate at flush time; a listener may have detached during
        // the session. Matches the double-test semantics of the previous
        // periodic design.
        if (!DeckleHudSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;

        int durationMs = sw is null ? 0 : (int)sw.ElapsedMilliseconds;
        var (p50, p95) = _proximityRollup.ComputePercentiles();

        DeckleHudSource.Log.ProximityRollup(
            durationMs, samples, minAlpha, maxAlpha, p50, p95);
    }

    private void SetAlphaImmediate(byte alpha)
    {
        _currentAlpha = alpha;
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }

    // ── Fade-in: Hidden → visible transition ──────────────────────────────────
    //
    // 150ms cubic ease-out, matches WindowSlideAnimator / LayeredAlphaAnimator
    // to keep the HUD subsystem visually consistent. Proximity is suspended for
    // the duration so a WM_INPUT mid-fade cannot snap alpha to a smoothstep
    // value while the fade-in is still ramping up.

    private void StartFadeIn(byte target, bool activateProximityOnComplete)
    {
        _fadeInTimer?.Stop();
        _proximityActive = false;
        byte fromAlpha = _currentAlpha;
        SetAlphaImmediate(0);
        _fadeInTarget = target;
        _fadeInActivateProximityOnComplete = activateProximityOnComplete;
        _fadeInStartUtc = DateTime.UtcNow;
        _fadeInTimer ??= DispatcherQueue.CreateTimer();
        _fadeInTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeInTimer.IsRepeating = true;
        _fadeInTimer.Tick -= OnFadeInTick;
        _fadeInTimer.Tick += OnFadeInTick;
        _fadeInTimer.Start();

        // Axis 2: FadeInStarted. scope="hud" because this is the main window's
        // fade-in (HudOverlayWindow has its own emission site with
        // scope="overlay"). fromAlpha is captured before the reset to 0 to
        // trace a possible transition from an in-progress proximity alpha.
        DeckleHudSource.Log.FadeInStarted("hud", FADE_IN_MS, fromAlpha, target);
    }

    private void OnFadeInTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var elapsed = (DateTime.UtcNow - _fadeInStartUtc).TotalMilliseconds;
        var t = Math.Clamp(elapsed / FADE_IN_MS, 0.0, 1.0);

        var oneMinusT = 1.0 - t;
        var eased = 1.0 - (oneMinusT * oneMinusT * oneMinusT);

        var alpha = (byte)Math.Clamp(Math.Round(_fadeInTarget * eased), 0, 255);
        SetAlphaImmediate(alpha);

        if (t >= 1.0)
        {
            sender.Stop();
            SetAlphaImmediate(_fadeInTarget);
            if (_fadeInActivateProximityOnComplete)
            {
                _proximityActive = Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity;
                if (_proximityActive) UpdateProximity();
            }
        }
    }

    private void CancelFadeIn()
    {
        _fadeInTimer?.Stop();
    }
}
