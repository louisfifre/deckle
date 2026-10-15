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
// clips the compositor output to a rounded shape.
//
// No classical Win32 frame is painted because WM_NCCALCSIZE is intercepted
// in the subclass and returns a zero non-client area. WinUI 3 reapplies
// WS_DLGFRAME / WS_EX_WINDOWEDGE on the top-level HWND even after they are
// stripped, so stripping bits is a losing game: leave them on and deny
// Windows any NC area to paint into.
//
// Mouse proximity:
//   - WS_EX_LAYERED + SetLayeredWindowAttributes(LWA_ALPHA) gives a global
//     alpha covering the whole window content. Recomputed through a smoothstep
//     each time the cursor moves — no polling.
//   - The cursor signal is the shared CursorMovementSignal (Deckle.Shell): the
//     HUD subscribes while proximity is active and unsubscribes otherwise.
//   - WM_NCCALCSIZE subclass delegate kept in an instance field to survive GC.
public sealed partial class HudWindow : Window
{
    private const int HUD_WIDTH         = 272;
    private const int HUD_HEIGHT        =  78;
    internal const int HUD_BOTTOM_MARGIN =  96;

    // Proximity fade endpoints; the curve lives in ProximityFade. No
    // animation: each cursor move applies the target alpha directly.
    private const double NEAR_RADIUS_DIP = 10;
    private const double FAR_RADIUS_DIP  = 128;
    private const byte   MAX_ALPHA       = 255;
    private const byte   MIN_ALPHA       = 40;

    private readonly IntPtr _hwnd;

    private readonly CursorMovementSignal _cursorSignal;

    // Cached window scale (DPI/96): UpdateProximity runs on every cursor move
    // and the scale only changes on WM_DPICHANGED. UI-thread only (the ctor
    // seed and the message pump both run there), so no synchronization.
    private double _dpiScale = 1.0;

    private byte _currentAlpha = MAX_ALPHA;
    private bool _proximityActive;

    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x48554450); // "HUDP"

    private HudState _state = HudState.Hidden;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _messageHideTimer;
    private int _zOrderProbeGeneration;

    private readonly ProximityRollupAggregator _proximityRollup = new();
    private bool _proximityRollupEnabled;
    private System.Diagnostics.Stopwatch? _proximitySessionStopwatch;

    // Matches LayeredAlphaAnimator's duration and cubic ease-out.
    private const int FADE_IN_MS = 150;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _fadeInTimer;
    private DateTime _fadeInStartUtc;
    private byte _fadeInTarget;
    private bool _fadeInActivateProximityOnComplete;

    public event EventHandler<bool>? MainHudVisibilityChanged;

    public IntPtr Hwnd             => _hwnd;
    public bool   IsMainHudShown   => _state != HudState.Hidden;

    public HudWindow(CursorMovementSignal cursorSignal)
    {
        _cursorSignal = cursorSignal;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        SystemAnimationPreference.Instance.Changed += OnSystemAnimationsChanged;

        _dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));

        // Explicit null defeats any auto-applied Mica / Acrylic backdrop on
        // recent WindowsAppSDK versions. Paired with DWMWA_SYSTEMBACKDROP_TYPE
        // = DWMSBT_NONE below as belt-and-suspenders — one is the WinUI API
        // surface, the other is the DWM Win32 guarantee.
        SystemBackdrop = null;

        // Title is not visible (no title bar) but surfaces in Task View and
        // debuggers.
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
        // frame" visible on every first-party Win11 app.
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

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // Theme tracing only: HudChrono re-pushes its own code-set brushes.
        // App.ApplyTheme pushes "user"/"app-init" on the probe; a system
        // change arrives with no pending value and is labelled "system".
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

    internal static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(2);

    // Forwards mic RMS samples from the engine recording thread without
    // marshalling: HudChrono.UpdateAudioLevel is thread-safe by Composition's
    // contract, and the dispatcher would only add latency. A no-op when the
    // recording outline isn't attached.
    public void OnAudioLevel(float rms) => Chrono.UpdateAudioLevel(rms);

    // Capture-thread mailbox only; the chrono renders the snapshot on the UI thread.
    public void OnUtteranceProgress(bool active, float progress, int completed)
        => Chrono.UpdateUtteranceProgress(active, progress, completed);

    private void OnSystemAnimationsChanged(bool enabled)
    {
        EnqueueUI(() =>
        {
            if (!enabled)
                CompleteFadeInImmediately();
        });
    }

    // ── Implementation ────────────────────────────────────────────────────────

    private void EnqueueUI(Action a)
    {
        if (DispatcherQueue.HasThreadAccess) a();
        else
        {
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
        // LOWORD(wParam) is the new X-axis DPI. Not consumed: DefSubclassProc
        // lets WinUI 3 run its own DPI-change layout.
        if (uMsg == NativeMethods.WM_DPICHANGED)
        {
            _dpiScale = Dpi.ScaleOf((uint)(wParam.ToInt32() & 0xFFFF));
        }

        // Erase the non-client area. With wParam=TRUE, lParam points to a
        // NCCALCSIZE_PARAMS whose rgrc[0] holds the proposed window rect;
        // returning 0 and leaving that rect unchanged tells Windows "the
        // client area covers the full window". wParam=FALSE hits here with a
        // plain RECT, same contract. Removing this handler (even with
        // ExtendsContentIntoTitleBar off) brings the rectangular outline back.
        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Shared alpha mutator ──────────────────────────────────────────────────
    //
    // Proximity and fade-in both write the alpha here, so _currentAlpha always
    // matches the layered alpha.
    private void SetAlphaImmediate(byte alpha)
    {
        _currentAlpha = alpha;
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }
}
