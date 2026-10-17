using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// Live transcript panel: shows the dictated text of the current take as each
// utterance arrives, beside the HUD, bottom-aligned with it on the side chosen
// by Overlay.TranscriptPanelSide. Created once at boot and never destroyed,
// like HudWindow.
//
// Lifecycle, driven by the App composition root:
//   BeginTake → a take opens, the history is cleared, nothing is shown yet;
//   Append    → text joins the history; the first text of a take shows the
//               window (an empty box would say nothing the HUD does not);
//   EndTake   → later Append calls are ignored, the window stays;
//   Hide      → fade out, SW_HIDE, the take closes.
// Every public method is thread-safe: engine events arrive on background
// threads and each call marshals to the UI thread through EnqueueUI.
//
// The HWND shell (DWM round corners and default stroke, layered click-through
// no-activate ex-styles, WM_NCCALCSIZE subclass) is a deliberate copy of
// HudOverlayWindow's, pending one shared extraction for the three surfaces.
public sealed partial class TranscriptPanelWindow : Window
{
    // Wider than the HUD's 272 dip: its 240-dip text column is too narrow for
    // running prose.
    private const int PANEL_WIDTH_DIP = 360;

    // One margin for the bottom, the side and the top of the work area: the
    // HUD's own bottom margin, so the panel's bottom edge lines up with the HUD.
    private const int EDGE_MARGIN_DIP = HudWindow.HUD_BOTTOM_MARGIN;

    // HudMessage's padding (16,12), symmetric here since there is no badge.
    private const int HORIZONTAL_PADDING_DIP = 2 * 16;
    private const int VERTICAL_PADDING_DIP = 2 * 12;

    // Bounds memory on a long take only; the viewport does the visible
    // trimming. Large enough to fill any screen column.
    private const int HISTORY_BUDGET_CHARS = 8000;

    private static readonly UIntPtr SubclassId = new(0x5452504E); // "TRPN"

    private readonly IntPtr _hwnd;
    private readonly CursorMovementSignal _cursorSignal;
    private readonly LayeredAlphaAnimator _fade;
    private readonly TranscriptHistory _history = new(HISTORY_BUDGET_CHARS);

    // Cached window scale (DPI/96): seeded in the ctor, refreshed on show and
    // from WM_DPICHANGED. Read by the proximity handler on every mouse move.
    // UI-thread only.
    private double _dpiScale = 1.0;

    private NativeMethods.SubclassProc? _subclassDelegate;

    // UI-thread state.
    private bool _takeOpen;
    private bool _shown;
    private bool _rightSide = true;
    private double _lineHeightDip; // 0 until measured on first render

    public TranscriptPanelWindow(CursorMovementSignal cursorSignal)
    {
        _cursorSignal = cursorSignal;
        _onCursorMoved = UpdateProximity;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));

        // Same shell as HudOverlayWindow, in the same order. Title is not
        // visible (no title bar) but surfaces in Task View and debuggers.
        SystemBackdrop = null;
        Title = Loc.Get("TranscriptPanel_WindowTitle");
        IconAssets.ApplyToWindow(AppWindow);

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable   = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // DWM: rounded silhouette, the system 1-dip frame stroke the HUD cards
        // wear, no Mica/Acrylic. DWMWA_NCRENDERING_POLICY is left alone:
        // disabling it also kills the frame stroke.
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

        // LAYERED carries the alpha, TOOLWINDOW keeps it out of alt-tab,
        // NOACTIVATE makes the no-focus contract native, TRANSPARENT lets every
        // click fall through (and so no user scrolling can reach the text).
        var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(
                ex
                | NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT));
        // Fully transparent until the first show fades it in.
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        // The subclass must be in place before SWP_FRAMECHANGED: the
        // WM_NCCALCSIZE it triggers is what erases the non-client area.
        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        _fade = new LayeredAlphaAnimator(_hwnd, DispatcherQueue, initialAlpha: 0);
        SystemAnimationPreference.Instance.Changed += OnSystemAnimationsChanged;

        // Keep the newest line at the bottom whenever the viewport (window
        // resize) or the extent (new text) changes size.
        TranscriptScroller.SizeChanged += (_, _) => ScrollToEnd();
        TranscriptText.SizeChanged += (_, _) => ScrollToEnd();

        // Never destroyed: closing only hides it.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            Hide();
        };
    }

    // ── Public API (any thread) ──────────────────────────────────────────────

    public void BeginTake() => EnqueueUI(() =>
    {
        // Same gate as HudWindow.SetState: with the overlay off, no take opens
        // and every Append is ignored.
        if (!Settings.SettingsService.Instance.Current.Overlay.Enabled) return;

        // A take can start while the previous one is still on screen (the HUD
        // goes from "Copied" straight back to Recording without hiding): the
        // old text leaves, the new take shows on its own first text.
        HideCore();
        _history.Clear();
        _takeOpen = true;
    });

    public void Append(string text, bool endedOnSilence) => EnqueueUI(() =>
    {
        if (!_takeOpen) return;
        _history.Append(text, endedOnSilence);

        if (_shown)
        {
            Render();
            return;
        }
        ShowFirst();
    });

    public void EndTake() => EnqueueUI(() => _takeOpen = false);

    public void Hide() => EnqueueUI(() =>
    {
        _takeOpen = false;
        HideCore();
    });

    // ── Show / hide (UI thread) ──────────────────────────────────────────────

    private void ShowFirst()
    {
        // The side is read once per show, so a settings change applies to the
        // next appearance and never moves a panel being read.
        _rightSide = Settings.SettingsService.Instance.Current.Overlay.TranscriptPanelSide != "Left";
        _dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));

        Render();

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW);
        _shown = true;

        // Fade in to wherever the cursor puts the panel right now, then follow
        // the cursor. FadeTo cancels a fade-out still in flight, and with it
        // that fade-out's SW_HIDE.
        byte target = ComputeProximityAlpha();
        _proximityAlpha = target;
        _fade.FadeTo(target, onComplete: BeginProximity);

        DeckleHudSource.Log.TranscriptPanelVisibilityChanged(true, SideName, _history.UtteranceCount);
    }

    private void HideCore()
    {
        if (!_shown) return;
        _shown = false;
        EndProximity();
        _fade.FadeTo(0, onComplete: () => NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE));

        DeckleHudSource.Log.TranscriptPanelVisibilityChanged(false, SideName, _history.UtteranceCount);
    }

    private string SideName => _rightSide ? "Right" : "Left";

    // ── Text layout (UI thread) ──────────────────────────────────────────────

    // Text → whole-line count → window rect. The window height is padding plus
    // a whole number of lines, its bottom edge anchored, so each new line
    // pushes the top edge up until the work area is full; past that the
    // ScrollViewer keeps its end in view (ScrollToEnd).
    private void Render()
    {
        EnsureLineHeight();
        TranscriptText.Text = _history.Text;

        // The text column width, set on the TextBlock so the layout wraps at
        // exactly the width the line count below is measured at. Redundant
        // with HorizontalScrollBarVisibility="Disabled" (same width) on
        // purpose: the window height must not depend on that ScrollViewer
        // rule alone.
        const int TEXT_WIDTH_DIP = PANEL_WIDTH_DIP - HORIZONTAL_PADDING_DIP;
        TranscriptText.Width = TEXT_WIDTH_DIP;
        TranscriptText.Measure(new Size(TEXT_WIDTH_DIP, double.PositiveInfinity));
        int contentLines = (int)Math.Round(TranscriptText.DesiredSize.Height / _lineHeightDip);

        var workArea = DisplayArea.Primary.WorkArea;
        int workAreaHeightDip = (int)Math.Floor(workArea.Height / _dpiScale);
        int maxLines = TranscriptPanelLayout.MaxVisibleLines(
            workAreaHeightDip, EDGE_MARGIN_DIP, VERTICAL_PADDING_DIP, _lineHeightDip);
        int visibleLines = TranscriptPanelLayout.VisibleLines(contentLines, maxLines);

        AppWindow.MoveAndResize(TranscriptPanelLayout.ComputeRectPx(
            workArea, _dpiScale, _rightSide,
            PANEL_WIDTH_DIP, EDGE_MARGIN_DIP, VERTICAL_PADDING_DIP, _lineHeightDip, visibleLines));
    }

    // Measured once from a one-line TextBlock of the same style, then pinned
    // with BlockLineHeight so every line, blank paragraph lines included, has
    // exactly this height and the line count is a plain division.
    private void EnsureLineHeight()
    {
        if (_lineHeightDip > 0) return;

        var probe = new TextBlock { Style = TranscriptText.Style, Text = "Ag" };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double measured = probe.DesiredSize.Height;
        // A detached measure should never return 0; if it does, the font size
        // is the closest value that still keeps lines whole.
        _lineHeightDip = measured > 0 ? measured : TranscriptText.FontSize;

        TranscriptText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TranscriptText.LineHeight = _lineHeightDip;
    }

    private void ScrollToEnd()
    {
        double scrollable = TranscriptScroller.ScrollableHeight;
        TranscriptScroller.ChangeView(null, scrollable, null, disableAnimation: true);

        // The fade ramp spans the first visible line, which starts at the
        // scroll offset in TextBlock coordinates. Half a line, not 0: px
        // rounding of the window height can leave a sub-pixel overflow that is
        // not hidden text. With nothing above, the ramp sits above the text
        // and every line pads with the solid end colour.
        bool moreAbove = scrollable > _lineHeightDip / 2;
        double rampTop = moreAbove ? scrollable : -_lineHeightDip;
        MoreAboveFade.StartPoint = new Point(0, rampTop);
        MoreAboveFade.EndPoint = new Point(0, rampTop + _lineHeightDip);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private void EnqueueUI(Action a)
    {
        if (DispatcherQueue.HasThreadAccess) a();
        else
        {
            DispatcherQueue.TryEnqueueObserved(
                "ui-update", "transcript-panel",
                () => a(),
                "HUD", "transcript panel action");
        }
    }

    private void OnSystemAnimationsChanged(bool enabled) =>
        EnqueueUI(() => _fade.SetAnimationsEnabled(enabled));

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        // LOWORD(wParam) is the new DPI. Not consumed: WinUI 3 runs its own
        // DPI layout through DefSubclassProc.
        if (uMsg == NativeMethods.WM_DPICHANGED)
        {
            _dpiScale = Dpi.ScaleOf((uint)(wParam.ToInt32() & 0xFFFF));
        }

        // Zero non-client area: the WS_DLGFRAME / WS_EX_WINDOWEDGE bits WinUI 3
        // reapplies have nothing to paint into.
        if (uMsg == NativeMethods.WM_NCCALCSIZE)
        {
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
