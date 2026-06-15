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
//     alpha covering the whole window content. Recomputed through a smoothstep
//     each time the cursor moves — no polling.
//   - The cursor signal is the shared CursorMovementSignal (Deckle.Shell): the
//     HUD subscribes while proximity is active and unsubscribes otherwise. It
//     no longer owns a Raw Input sink of its own.
//   - WM_NCCALCSIZE subclass delegate kept in an instance field to survive GC.
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

    // Shared source of mouse-move ticks (one Raw Input sink for the whole
    // process, owned by Deckle.Shell). The HUD subscribes while proximity is
    // active; see EnableProximity / DisableProximity.
    private readonly CursorMovementSignal _cursorSignal;

    // Window scale (DPI/96), cached. GetDpiForWindow was called on every
    // WM_INPUT tick (~125 Hz) inside UpdateProximity — the HUD's hottest path —
    // although the scale only ever changes on WM_DPICHANGED. Seeded from the
    // HWND's current DPI in the ctor and refreshed from WM_DPICHANGED's wParam
    // in the subclass. UI-thread only (both the seed and the message pump run
    // there), so no synchronization is needed.
    private double _dpiScale = 1.0;

    private byte _currentAlpha = MAX_ALPHA;
    private bool _proximityActive;

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

    public HudWindow(CursorMovementSignal cursorSignal)
    {
        _cursorSignal = cursorSignal;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        // Seed the cached scale from the HWND's current monitor; WM_DPICHANGED
        // refreshes it thereafter (see SubclassCallback).
        _dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;

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
        // the window; proximity fade still runs through the shared cursor signal.
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

    // `SuccessDuration` is shared with the HUD's internal Success messages
    // (ShowCopied / ShowPasted) in HudWindow.State.cs. A field, so it lives
    // here with the rest of the partial class's state.
    internal static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(2);

    // Forward mic RMS samples (20 Hz, engine recording thread) to the chrono
    // control without marshalling. HudChrono.UpdateAudioLevel writes to a
    // CompositionPropertySet scalar, which is thread-safe by Composition's
    // contract — going through the dispatcher would add latency for no gain.
    // Safe to call at any state: UpdateAudioLevel is a no-op when the
    // recording outline isn't attached.
    public void OnAudioLevel(float rms) => Chrono.UpdateAudioLevel(rms);

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

    // ── Subclass: WM_NCCALCSIZE (no-frame) ───────────────────────────────────

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
        //
        // Refresh the cached scale from the new DPI Windows just announced —
        // LOWORD(wParam) is the new X-axis DPI — instead of calling
        // GetDpiForWindow per WM_INPUT tick in UpdateProximity. We do not consume
        // the message (no reposition): DefSubclassProc lets WinUI 3 run its own
        // DPI-change layout.
        if (uMsg == NativeMethods.WM_DPICHANGED)
        {
            _dpiScale = (wParam.ToInt32() & 0xFFFF) / 96.0;
        }

        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Shared alpha mutator ──────────────────────────────────────────────────
    //
    // Central low-level alpha write, used by both the proximity fade
    // (HudWindow.Proximity.cs) and the show fade-in (HudWindow.Fade.cs). Kept
    // here so the single SetLayeredWindowAttributes site and the _currentAlpha
    // field update stay co-located and uncontested.
    private void SetAlphaImmediate(byte alpha)
    {
        _currentAlpha = alpha;
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }
}
