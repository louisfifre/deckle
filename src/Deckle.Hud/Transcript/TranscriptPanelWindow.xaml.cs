using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// Live transcript panel: shows the text of the current take as the engine
// delivers it, beside the HUD, bottom-aligned with it on the side chosen by
// Overlay.TranscriptPanelSide. Created once at boot and never destroyed, like
// HudWindow.
//
// The panel owns no text model: the engine's TranscriptChanged snapshot is
// the whole text as it will be delivered, rewrites included, and the panel
// shows it as is. So what the panel shows is what the clipboard gets, and
// the join rule of the take lives in one place.
//
// Lifecycle, driven by the App composition root:
//   BeginTake → a take opens, the text is cleared, nothing is shown yet;
//   SetText   → the snapshot replaces the text; the first text of a take
//               shows the window (an empty box would say nothing the HUD
//               does not); a revision cross-fades the content;
//   EndTake   → later SetText calls are ignored, the window stays;
//   Hide      → fade out, SW_HIDE, the take closes;
//   ShowAgain → after a Hide that the HUD caused mid-take (the paste hides
//               the HUD), the panel comes back with the HUD, so the final
//               text is read beside "Pasted" as it is beside "Copied".
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

    // Bounds the laid-out text on a long take only; the viewport does the
    // visible trimming. Large enough to fill any screen column.
    private const int TEXT_BUDGET_CHARS = 8000;

    // A revision dips the content, swaps the text, and brings it back: short
    // enough to read as one change, long enough to be seen. The return uses
    // the same 150 ms cubic ease-out as the HUD's own fades.
    private const int REVISION_DIP_MS = 80;
    private const int REVISION_RETURN_MS = 150;

    private static readonly UIntPtr SubclassId = new(0x5452504E); // "TRPN"

    private readonly IntPtr _hwnd;
    private readonly CursorMovementSignal _cursorSignal;
    private readonly LayeredAlphaAnimator _fade;

    // Cached window scale (DPI/96): seeded in the ctor, refreshed on show and
    // from WM_DPICHANGED. Read by the proximity handler on every mouse move.
    // UI-thread only.
    private double _dpiScale = 1.0;

    private NativeMethods.SubclassProc? _subclassDelegate;

    // UI-thread state.
    private string _text = "";
    private bool _takeOpen;
    private bool _shown;
    private bool _rightSide = true;
    private double _lineHeightDip; // 0 until measured on first render

    // True after a Hide while the take was open and text was on screen: the
    // HUD hid for the paste, and the panel comes back when the HUD does.
    private bool _hiddenWithText;

    // The revision cross-fade in flight, if any. A new text during the dip is
    // simply the text the return shows: the swap reads _text when it runs.
    private Storyboard? _revisionDip;

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
        if (!Settings.SettingsService.Instance.Current.Overlay.Enabled)
        {
            DeckleHudSource.Log.HudDisplaySuppressed(
                "transcript-panel", "BeginTake", -1, "overlay_disabled");
            return;
        }

        // A take can start while the previous one is still on screen (the HUD
        // goes from "Copied" straight back to Recording without hiding): the
        // old text leaves, the new take shows on its own first text.
        HideCore();
        _text = "";
        _hiddenWithText = false;
        _takeOpen = true;
    });

    // The whole text of the take as the engine will deliver it. revised says
    // that text already shown was replaced (a rewrite came back), which the
    // panel makes visible with a cross-fade; an append just lays out the
    // longer text. An identical text changes nothing on screen.
    public void SetText(string text, bool revised) => EnqueueUI(() =>
    {
        if (!_takeOpen) return;
        string next = TranscriptPanelLayout.TrimToBudget(text, TEXT_BUDGET_CHARS);
        if (next == _text) return;
        _text = next;

        if (!_shown)
        {
            if (_text.Length > 0) ShowFirst();
            return;
        }

        if (revised && SystemAnimationPreference.Instance.AnimationsEnabled)
            CrossFade();
        else
            Render();
    });

    public void EndTake() => EnqueueUI(() => _takeOpen = false);

    public void Hide() => EnqueueUI(() =>
    {
        _hiddenWithText = _takeOpen && _shown;
        _takeOpen = false;
        HideCore();
    });

    // The HUD came back after hiding mid-take: the panel comes back with it,
    // with the text it had, until the HUD hides again. A new take clears the
    // flag in BeginTake, so a stale text never reappears at the next start.
    public void ShowAgain() => EnqueueUI(() =>
    {
        if (!_hiddenWithText || _shown) return;
        _hiddenWithText = false;
        if (_text.Length > 0) ShowFirst();
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
        bool setposOk = NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW);
        WindowingProbe.ReportShow(
            "transcript-panel", "setwindowpos_topmost",
            setposOk, setposOk ? 0 : Marshal.GetLastWin32Error());
        _shown = true;

        // Fade in to wherever the cursor puts the panel right now, then follow
        // the cursor. FadeTo cancels a fade-out still in flight, and with it
        // that fade-out's SW_HIDE.
        byte target = ComputeProximityAlpha();
        _proximityAlpha = target;
        _fade.FadeTo(target, onComplete: BeginProximity);

        DeckleHudSource.Log.TranscriptPanelVisibilityChanged(true, SideName, _text.Length);
    }

    private void HideCore()
    {
        if (!_shown) return;
        _shown = false;
        EndProximity();
        _fade.FadeTo(0, onComplete: () => NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE));

        DeckleHudSource.Log.TranscriptPanelVisibilityChanged(false, SideName, _text.Length);
    }

    private string SideName => _rightSide ? "Right" : "Left";

    // ── Revision cross-fade (UI thread) ──────────────────────────────────────

    // Dip the content, swap the text at the bottom of the dip, bring it back.
    // The window rect follows the new text at the swap, inside the dip, so
    // the resize is not seen as a motion of its own. A dip already running
    // keeps going: its swap shows whatever _text holds by then.
    private void CrossFade()
    {
        if (_revisionDip is not null) return;

        var dip = new Storyboard();
        var toZero = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(REVISION_DIP_MS)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(toZero, TranscriptContent);
        Storyboard.SetTargetProperty(toZero, "Opacity");
        dip.Children.Add(toZero);
        dip.Completed += (_, _) =>
        {
            _revisionDip = null;
            Render();

            var back = new Storyboard();
            var toOne = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(REVISION_RETURN_MS)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(toOne, TranscriptContent);
            Storyboard.SetTargetProperty(toOne, "Opacity");
            back.Children.Add(toOne);
            back.Begin();
        };
        _revisionDip = dip;
        dip.Begin();
    }

    // ── Text layout (UI thread) ──────────────────────────────────────────────

    // Text → whole-line count → window rect. The window height is padding plus
    // a whole number of lines, its bottom edge anchored, so each new line
    // pushes the top edge up until the work area is full; past that the
    // ScrollViewer keeps its end in view (ScrollToEnd).
    private void Render()
    {
        EnsureLineHeight();
        TranscriptText.Text = _text;
        TranscriptFadeText.Text = _text;

        // The text column width, set on the TextBlock so the layout wraps at
        // exactly the width the line count below is measured at. Redundant
        // with HorizontalScrollBarVisibility="Disabled" (same width) on
        // purpose: the window height must not depend on that ScrollViewer
        // rule alone. The faded copy gets the same width so both wrap alike.
        const int TEXT_WIDTH_DIP = PANEL_WIDTH_DIP - HORIZONTAL_PADDING_DIP;
        TranscriptText.Width = TEXT_WIDTH_DIP;
        TranscriptFadeText.Width = TEXT_WIDTH_DIP;
        TranscriptText.Measure(new Size(TEXT_WIDTH_DIP, double.PositiveInfinity));
        int contentLines = (int)Math.Round(TranscriptText.DesiredSize.Height / _lineHeightDip);

        var workArea = DisplayArea.Primary.WorkArea;
        int workAreaHeightDip = (int)Math.Floor(workArea.Height / _dpiScale);
        int maxLines = TranscriptPanelLayout.MaxVisibleLines(
            workAreaHeightDip, EDGE_MARGIN_DIP, VERTICAL_PADDING_DIP, _lineHeightDip);
        int visibleLines = TranscriptPanelLayout.VisibleLines(contentLines, maxLines);

        try
        {
            AppWindow.MoveAndResize(TranscriptPanelLayout.ComputeRectPx(
                workArea, _dpiScale, _rightSide,
                PANEL_WIDTH_DIP, EDGE_MARGIN_DIP, VERTICAL_PADDING_DIP, _lineHeightDip, visibleLines));
            WindowingProbe.ReportPlacement("transcript-panel", "move_and_resize", ok: true);
        }
        catch (Exception ex)
        {
            // Render runs on every Append, inside the engine's UI marshalling:
            // a refused placement must not abort the take. The panel keeps its
            // previous rect and the incident carries the failure.
            WindowingProbe.ReportPlacementFailure("transcript-panel", "move_and_resize", ex);
        }
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
        TranscriptFadeText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TranscriptFadeText.LineHeight = _lineHeightDip;
    }

    private void ScrollToEnd()
    {
        double scrollable = TranscriptScroller.ScrollableHeight;
        TranscriptScroller.ChangeView(null, scrollable, null, disableAnimation: true);

        // The first visible line starts at the scroll offset in TextBlock
        // coordinates. Half a line, not 0: px rounding of the window height
        // can leave a sub-pixel overflow that is not hidden text. The faded
        // copy shows that line alone; the solid copy shows everything below
        // it. With nothing above, the faded copy shows nothing and the solid
        // copy everything (a clip of null means "no clip").
        bool moreAbove = scrollable > _lineHeightDip / 2;
        double width = TranscriptText.ActualWidth;
        double height = TranscriptText.ActualHeight;
        if (moreAbove)
        {
            double solidTop = scrollable + _lineHeightDip;
            TranscriptText.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, solidTop, width, Math.Max(0, height - solidTop)),
            };
            TranscriptFadeText.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, scrollable, width, _lineHeightDip),
            };
        }
        else
        {
            TranscriptText.Clip = null;
            TranscriptFadeText.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) };
        }
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
