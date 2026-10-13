using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// Overlay card Window used by HudOverlayManager. Each enqueued overlay creates
// one HudOverlayWindow; the manager owns its position and life timer, the
// window owns its own alpha (fade-in / fade-out / proximity modulation).
//
// Same technical shell as HudWindow — fixed HUD_WIDTH x HUD_HEIGHT in dips,
// opaque LayerFillColorDefaultBrush, DWM round + default accent stroke,
// WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT, WM_NCCALCSIZE erased
// via subclass. Also mirrors HudWindow's proximity-fade behaviour: when the
// cursor approaches, alpha smooth-ramps from MAX down toward MIN. The cursor
// signal is the shared CursorMovementSignal (Deckle.Shell): the card subscribes
// on fade-in and unsubscribes on fade-out. A per-window Raw Input sink is
// impossible (RIDEV_INPUTSINK is per-process-per-usage), so every surface
// shares the one sink owned by the message-only host.
//
// Unlike HudWindow, no Closing intercept: the manager calls FadeOut, which
// ends with ForceClose → Window.Close() after the alpha animator reaches 0.
public sealed partial class HudOverlayWindow : Window
{
    public const int HUD_WIDTH  = 272;
    public const int HUD_HEIGHT = 78;

    // NEAR / MIN / MAX match HudWindow, but FAR_RADIUS is deliberately wider
    // than HudWindow's: overlays sit above the main HUD, so any cursor approach
    // toward the HUD passes through the overlay region too, and the cards
    // start clearing earlier to stay out of the way.
    private const double NEAR_RADIUS_DIP = 10;
    private const double FAR_RADIUS_DIP  = 256;
    private const byte   MAX_ALPHA       = 255;
    private const byte   MIN_ALPHA       = 40;

    private readonly IntPtr _hwnd;
    private LayeredAlphaAnimator? _fade;

    // Cached window scale (DPI/96), refreshed from WM_DPICHANGED so the
    // per-mouse-move proximity handler never calls GetDpiForWindow.
    // UI-thread only.
    private double _dpiScale = 1.0;

    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x48554F56); // "HUOV"

    private readonly CursorMovementSignal _cursorSignal;
    private bool _proximityActive;
    private byte _proximityAlpha = MAX_ALPHA;

    public HudOverlayWindow(CursorMovementSignal cursorSignal)
    {
        _cursorSignal = cursorSignal;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;

        SystemBackdrop = null;
        Title = Loc.Get("HudOverlay_WindowTitle");
        IconAssets.ApplyToWindow(AppWindow);

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable   = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // Same DWM triptych as HudWindow: DWMWCP_ROUND for the rounded HWND
        // silhouette, DWMWA_COLOR_DEFAULT for the 1-dip system accent stroke
        // that frames the card (without it the card reads as a naked colored
        // rectangle on the desktop), DWMSBT_NONE to keep Mica/Acrylic off.
        //
        // Overlay cards also set DWMWA_NCRENDERING_POLICY = DWMNCRP_DISABLED:
        // the Win11 Shell dropshadow of a card stacked above the HUD lands on
        // the HUD's top edge as a visible halo. Disabling NC rendering kills
        // that shadow; the stroke and the corner clip are independent paths
        // and keep working.
        uint cornerPref = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPref, sizeof(uint));
        uint borderColor = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_BORDER_COLOR,
            ref borderColor, sizeof(uint));
        uint ncPolicy = NativeMethods.DWMNCRP_DISABLED;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_NCRENDERING_POLICY,
            ref ncPolicy, sizeof(uint));
        uint backdropType = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdropType, sizeof(uint));

        var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(ex | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT));
        // Fully transparent until FadeIn.
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // The fade animator owns the layered alpha from here on; proximity goes
        // through it with instant FadeTo updates.
        _fade = new LayeredAlphaAnimator(_hwnd, DispatcherQueue, initialAlpha: 0);
        SystemAnimationPreference.Instance.Changed += OnSystemAnimationsChanged;

        // Theme tracing only: the event helps correlate a stroke glitch with a
        // theme switch while a card is floating.
        if (Content is Microsoft.UI.Xaml.FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
    }

    // ── Theme tracing ────────────────────────────────────────────────────────
    private Microsoft.UI.Xaml.ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(Microsoft.UI.Xaml.FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged(
            "hud-overlay", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    public IntPtr Hwnd => _hwnd;

    private void OnSystemAnimationsChanged(bool enabled)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            _fade?.SetAnimationsEnabled(enabled);
            return;
        }

        DispatcherQueue.TryEnqueueObserved(
            "ui-update", "hud-overlay-window",
            () => _fade?.SetAnimationsEnabled(enabled),
            "HUD", "system animation preference change");
    }

    // MessagePayload.Duration is unused here: the manager owns the per-card
    // timer. Severity convention 0=Info, 1=Warning, 2+=Error, aligned with the
    // `UserFeedbackEmitted(severity, ...)` EventSource event.
    public void ApplyPayload(int severity, string title, string body)
    {
        MessageKind kind = severity switch
        {
            0 => MessageKind.Informational,
            1 => MessageKind.Warning,
            _ => MessageKind.Critical,
        };
        Message.Show(new MessagePayload(kind, title, body, TimeSpan.Zero));
    }

    public (int Width, int Height) GetSizePx()
    {
        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi / 96.0;
        return (
            (int)Math.Round(HUD_WIDTH  * scale),
            (int)Math.Round(HUD_HEIGHT * scale));
    }

    // Shows without activation and reasserts HWND_TOPMOST so the newest
    // overlay stays in the topmost band without stealing focus.
    public void ShowAt(int xPx, int yPx)
    {
        var (w, h) = GetSizePx();
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(xPx, yPx, w, h));
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);

        // Anchor "absolute": the slot geometry lives in HudOverlayManager,
        // which emits the OverlaySlotAssigned specialization.
        WindowingProbe.EmitWindowPositioned(_hwnd, "hud-overlay", "absolute");
    }

    public void SetPositionPx(int xPx, int yPx)
    {
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero,
            xPx, yPx, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // ── Fade: public entry points driven by HudOverlayManager ────────────────

    // Enters proximity mode once the fade completes; the callback fires after
    // both.
    public void FadeIn(Action? onComplete = null)
    {
        // 150 mirrors LayeredAlphaAnimator's private Duration.
        byte fromAlpha = _fade?.CurrentAlpha ?? 0;
        DeckleHudSource.Log.FadeInStarted("overlay", 150, fromAlpha, MAX_ALPHA);

        _fade?.FadeTo(MAX_ALPHA, onComplete: () =>
        {
            _proximityAlpha = MAX_ALPHA;
            BeginProximityMode();
            onComplete?.Invoke();
        });
    }

    // Leaves proximity mode first so it cannot fight the fade.
    public void FadeOut(Action? onComplete = null)
    {
        EndProximityMode();
        _fade?.FadeTo(0, onComplete: onComplete);
    }

    // ── Proximity: cursor-distance → alpha smoothstep (the curve lives in ProximityFade) ──
    //
    // Proximity only modulates between MAX_ALPHA and MIN_ALPHA; fade-in and
    // fade-out own the 0..MAX_ALPHA range.

    private void BeginProximityMode()
    {
        if (_proximityActive) return;
        if (!Settings.SettingsService.Instance.Current.Overlay.FadeOnProximity) return;

        _proximityActive = true;
        _cursorSignal.Moved += UpdateProximity;

        // Seed with the current cursor distance so the first frame after
        // fade-in already reflects reality rather than snapping one tick later.
        UpdateProximity();
    }

    private void EndProximityMode()
    {
        if (!_proximityActive) return;
        _cursorSignal.Moved -= UpdateProximity;
        _proximityActive = false;
    }

    private void UpdateProximity()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var pos  = AppWindow.Position;
        var size = AppWindow.Size;
        int left   = pos.X;
        int top    = pos.Y;
        int right  = pos.X + size.Width;
        int bottom = pos.Y + size.Height;

        double distancePx = ProximityFade.DistanceToRectPx(cursor.X, cursor.Y, left, top, right, bottom);
        byte alpha = ProximityFade.Alpha(distancePx, _dpiScale, NEAR_RADIUS_DIP, FAR_RADIUS_DIP, MIN_ALPHA, MAX_ALPHA);
        if (alpha == _proximityAlpha) return;
        _proximityAlpha = alpha;
        _fade?.FadeTo(alpha, instant: true);
    }

    public void ForceClose()
    {
        SystemAnimationPreference.Instance.Changed -= OnSystemAnimationsChanged;
        EndProximityMode();
        _fade?.Cancel();

        if (_subclassDelegate is not null)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
            _subclassDelegate = null;
        }
        Close();
    }

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        // LOWORD(wParam) is the new DPI. Not consumed: WinUI 3 runs its own DPI
        // layout through DefSubclassProc.
        if (uMsg == NativeMethods.WM_DPICHANGED)
        {
            _dpiScale = (wParam.ToInt32() & 0xFFFF) / 96.0;
        }

        // WinUI 3 reapplies WS_DLGFRAME / WS_EX_WINDOWEDGE on the top-level;
        // leave those bits on and deny Windows any non-client area to paint
        // into.
        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
