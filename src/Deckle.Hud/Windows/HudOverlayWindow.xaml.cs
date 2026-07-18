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
// impossible (RIDEV_INPUTSINK is per-process-per-usage), and the former 60 Hz
// GetCursorPos poll is gone now that every surface shares the one sink owned by
// the message-only host.
//
// Unlike HudWindow, no Closing intercept: the manager calls FadeOut, which
// ends with ForceClose → Window.Close() after the alpha animator reaches 0.
public sealed partial class HudOverlayWindow : Window
{
    public const int HUD_WIDTH  = 272;
    public const int HUD_HEIGHT = 78;

    // Proximity smoothstep constants. NEAR / MIN / MAX match HudWindow so
    // the endpoints behave the same, but FAR_RADIUS is deliberately wider
    // than HudWindow's 128 dip — overlays sit above the main HUD so any
    // cursor approach toward the main HUD naturally passes through the
    // overlay region too, and the user wants those cards to start clearing
    // earlier to stay out of the way.
    private const double NEAR_RADIUS_DIP = 10;
    private const double FAR_RADIUS_DIP  = 256;
    private const byte   MAX_ALPHA       = 255;
    private const byte   MIN_ALPHA       = 40;

    private readonly IntPtr _hwnd;
    private LayeredAlphaAnimator? _fade;

    // Cached window scale (DPI/96); see HudWindow for the rationale. Seeded in
    // the ctor, refreshed from WM_DPICHANGED in SubclassCallback. Replaces the
    // per-WM_INPUT-tick GetDpiForWindow call in UpdateProximity. UI-thread only.
    private double _dpiScale = 1.0;

    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x48554F56); // "HUOV"

    // Shared mouse-move source (Deckle.Shell). Subscribed while the card is in
    // proximity mode; see BeginProximityMode / EndProximityMode.
    private readonly CursorMovementSignal _cursorSignal;
    private bool _proximityActive;
    private byte _proximityAlpha = MAX_ALPHA;

    public HudOverlayWindow(CursorMovementSignal cursorSignal)
    {
        _cursorSignal = cursorSignal;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        // Seed the cached scale from the HWND's current monitor; WM_DPICHANGED
        // refreshes it thereafter (see SubclassCallback).
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

        // Same DWM triptych as HudWindow — DWMWCP_ROUND for the rounded HWND
        // silhouette, DWMWA_COLOR_DEFAULT for the 1-dip system accent stroke
        // that frames the card (without it the card reads as a naked colored
        // rectangle on the desktop), DWMSBT_NONE to keep Mica/Acrylic off.
        //
        // Additional tweak on overlay cards vs the main HUD:
        // DWMWA_NCRENDERING_POLICY = DWMNCRP_DISABLED. The main HUD's Win11
        // Shell dropshadow is fine against the desktop, but when an overlay
        // card sits 12 dip above the HUD that same shadow lands on the HUD's
        // top edge as a visible halo. Disabling NC rendering kills the
        // shadow on overlays; the stroke + corner-preference compositor clip
        // are independent paths, they keep doing their job.
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
        // Start fully transparent — LayeredAlphaAnimator ramps up to 255 when
        // the manager invokes FadeTo after ShowAt.
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // Fade animator owns the layered alpha from here on. Manager calls
        // FadeIn / FadeOut; proximity mixes in at runtime via instant FadeTo
        // updates driven by the shared cursor signal (no animation overhead).
        _fade = new LayeredAlphaAnimator(_hwnd, DispatcherQueue, initialAlpha: 0);
        SystemAnimationPreference.Instance.Changed += OnSystemAnimationsChanged;

        // Theme: wires ActualThemeChanged for transient overlays. An overlay
        // lives 2-8 s; a theme change during display is rare but possible (the
        // user swaps while a notification is floating). The event remains
        // useful to correlate a colored stroke glitch with a transition.
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

    // Pushes the feedback payload into the embedded HudMessage control. The
    // Duration field of MessagePayload is unused here (HudMessage ignores it);
    // the manager owns the per-card timer.
    //
    // Sub-wave 6b: signature in primitives rather than a `UserFeedback`
    // record. Severity convention 0=Info, 1=Warning, 2+=Error, aligned with
    // the `UserFeedbackEmitted(severity, ...)` EventSource event.
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

    // Card pixel size for the current DPI — manager uses this to compute slot
    // positions (stride = gap + card height).
    public (int Width, int Height) GetSizePx()
    {
        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi / 96.0;
        return (
            (int)Math.Round(HUD_WIDTH  * scale),
            (int)Math.Round(HUD_HEIGHT * scale));
    }

    // Initial placement: size the window at current DPI, move to target pixel
    // position, then show without activation and reassert HWND_TOPMOST so the
    // newest overlay stays in the topmost band without stealing focus.
    public void ShowAt(int xPx, int yPx)
    {
        var (w, h) = GetSizePx();
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(xPx, yPx, w, h));
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);

        // Windowing: common trunk emitted here after MoveAndResize. The anchor
        // is "absolute": the position is computed by
        // HudOverlayManager.ComputeSlotPositionPx from the main HUD + slot, but
        // that calculation is invisible on the window side. We capture the
        // effective position without inventing a logical anchor that belongs
        // to the manager. The OverlaySlotAssigned specialization (which
        // carries the slot) is emitted on the manager side, where the slot
        // index is known.
        WindowingProbe.EmitWindowPositioned(_hwnd, "hud-overlay", "absolute");
    }

    // Used for instant repositioning (reduced-motion path, or to bypass the
    // animator if the manager needs to snap). Under normal conditions,
    // WindowSlideAnimator.SlideTo owns the live position of this HWND.
    public void SetPositionPx(int xPx, int yPx)
    {
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero,
            xPx, yPx, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // ── Fade: public entry points driven by HudOverlayManager ────────────────

    // Ramps alpha from current (0 on first call after ShowAt) up to MAX_ALPHA
    // over 150 ms, then enters proximity mode (subscribes to the cursor
    // signal). Callback fires once both the fade and the arming are complete.
    public void FadeIn(Action? onComplete = null)
    {
        // Axis 2: FadeInStarted (scope="overlay"). from_alpha comes from the
        // ctor, which sets alpha=0 through SetLayeredWindowAttributes; read
        // _fade?.CurrentAlpha to stay exact if a subsequent FadeIn is
        // triggered (the current manager does not do that, but a future
        // evolution could). Duration 150 ms = private
        // LayeredAlphaAnimator.Duration of WindowSlideAnimator (constant
        // Duration, mirror of HudWindow's FADE_IN_MS).
        byte fromAlpha = _fade?.CurrentAlpha ?? 0;
        DeckleHudSource.Log.FadeInStarted("overlay", 150, fromAlpha, MAX_ALPHA);

        _fade?.FadeTo(MAX_ALPHA, onComplete: () =>
        {
            _proximityAlpha = MAX_ALPHA;
            BeginProximityMode();
            onComplete?.Invoke();
        });
    }

    // Leaves proximity mode immediately (so it can't fight the fade),
    // then ramps alpha down to 0 over 150 ms. Manager uses the completion
    // callback to schedule ForceClose.
    public void FadeOut(Action? onComplete = null)
    {
        EndProximityMode();
        _fade?.FadeTo(0, onComplete: onComplete);
    }

    // ── Proximity: cursor-distance → alpha smoothstep ────────────────────────
    //
    // Same profile as HudWindow.UpdateProximity, driven by the shared cursor
    // signal. The proximity-requested alpha only modulates downward from
    // MAX_ALPHA to MIN_ALPHA — fade-in / fade-out own the 0..MAX_ALPHA range.

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

        int dx = cursor.X < left ? left - cursor.X : (cursor.X > right  ? cursor.X - right  : 0);
        int dy = cursor.Y < top  ? top  - cursor.Y : (cursor.Y > bottom ? cursor.Y - bottom : 0);
        double distancePx = Math.Sqrt(dx * dx + dy * dy);

        double scale  = _dpiScale;
        double nearPx = NEAR_RADIUS_DIP * scale;
        double farPx  = FAR_RADIUS_DIP  * scale;

        double t = (distancePx - nearPx) / (farPx - nearPx);
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;

        double eased = t * t * (3.0 - 2.0 * t);

        byte alpha = (byte)Math.Round(MIN_ALPHA + eased * (MAX_ALPHA - MIN_ALPHA));
        if (alpha == _proximityAlpha) return;
        _proximityAlpha = alpha;
        _fade?.FadeTo(alpha, instant: true);
    }

    // Actually destroys the window (no Closing intercept on this transient).
    // The manager calls this after the fade-out animator completes.
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
        // Refresh the cached scale from the new DPI (LOWORD of wParam) instead
        // of calling GetDpiForWindow per WM_INPUT tick in UpdateProximity. Not
        // consumed — DefSubclassProc lets WinUI 3 run its own DPI layout.
        if (uMsg == NativeMethods.WM_DPICHANGED)
        {
            _dpiScale = (wParam.ToInt32() & 0xFFFF) / 96.0;
        }

        // Same zero-NC-area trick as HudWindow. WinUI 3 reapplies
        // WS_DLGFRAME / WS_EX_WINDOWEDGE on the top-level; we leave those bits
        // on and deny Windows any non-client area to paint into.
        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
