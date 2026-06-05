using System;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Core.Interop;
using Deckle.Diagnostics;
using Deckle.Shell.TrayMenu.Interop;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Shell.TrayMenu;

// ─── Tray context menu — WinUI 3 SecondWindow pattern ─────────────────────────
//
// Host that bridges the Win32 tray (Shell_NotifyIcon) to the native WinUI 3
// MenuFlyout. The pattern, inspired by the open-source H.NotifyIcon library
// (MIT), fits in a transparent WinUI window pre-created at init: on tray
// right-click, it is positioned at the cursor and a MenuFlyout opens on it in
// FlyoutShowMode.Transient. Without this carrier window, a MenuFlyout cannot
// anchor to a message-only HWND; it needs a XamlRoot.
//
// The carrier window is invisible: WS_EX_LAYERED + SetLayeredWindowAttributes
// with alpha=0. The MenuFlyout is rendered by WinUI in its own popup (detached
// child HWND), so it remains fully visible. Win11 rendering (mica, rounded
// corners, Fluent shadow, native animations) comes for free with the control.
//
// Dismiss: Window.Activated → Deactivated covers click-outside and global
// focus loss; each item's Click closes explicitly. No close animation, to avoid
// the reopen-during-close hack H.NotifyIcon has to use
// (AreOpenCloseAnimationsEnabled = false).
//
// Observability: positioning events (anchor, monitor, popup position,
// move-and-resize) are emitted through WindowingProbe on the cross-cutting
// DeckleWindowingSource sub-provider, with no duplication on the module-local
// provider. The local Deckle.Shell.TrayMenu sub-provider traces only tray-menu
// specifics: prime cycle, item measurement, dismiss reason.

public sealed class TrayContextMenuHost : IDisposable
{
    // Owner HWND (tray message-only host). Defines the popup's parent z-order
    // so activation/deactivation chains correctly with the tray. Stored as the
    // Win32 GWLP_HWNDPARENT (-8) const offset.
    private const int GWLP_HWNDPARENT = -8;

    // Exclusion margin around the cursor, in physical pixels. Prevents the
    // popup from covering the tray icon itself (CalculatePopupWindowPosition
    // looks for a location adjacent to the exclusion rect). 36 px = typical
    // tray icon slot size at 100% DPI.
    private const int CursorExcludeHalfExtent = 18;

    // Flat margin for the MeasureFlyout fallback path (presenter not captured
    // during the prime cycle). 4 px per side. Imprecise by nature; it
    // overestimated the presenter's real chrome (≈ 4-6 DIP), creating the Mica
    // gap when it drove measurement. The nominal path now reads the real
    // presenter's DesiredSize (_primedPresenterSize).
    private const double FlyoutFrameMargin = 4.0;

    private readonly IntPtr _ownerHwnd;

    private Window? _window;
    private Frame? _frame;
    private MenuFlyout? _flyout;
    private MenuFlyoutItem? _ambientItem;
    private IntPtr _hwnd;
    private AppWindow? _appWindow;

    private bool _isVisible;
    private bool _primed;
    private bool _disposed;

    // Trackers for the ShowRequested event: inter-open delta calculation,
    // useful to correlate the measurement pipeline with possible prolonged
    // inactivity of the primed visual tree between two opens.
    private long _lastShowTickMs;
    private int _showCount;

    // Cache of DesiredSize values captured during the prime cycle, with items
    // attached to the internal popup visual tree. MeasureFlyout() reads this
    // cache instead of calling detached item.Measure(), which returns unstable
    // values (see module JOURNAL.md: 40 → 32 switch on native items after a
    // variable number of opens because the native template falls to MinHeight
    // when measured detached).
    private readonly System.Collections.Generic.Dictionary<MenuFlyoutItemBase, Windows.Foundation.Size> _primedSizes = new();

    // DesiredSize of the real MenuFlyoutPresenter, captured during the prime
    // cycle. Includes the presenter's own padding and border: this is the exact
    // size occupied by the visible popup. MeasureFlyout() prefers it over item
    // sum + flat margin: because Full stretches the presenter to the carrier
    // window, sizing the window to this value cancels stretching and removes
    // the Mica gap (see CLAUDE.md, Placement section). Null until the prime
    // cycle has run, or if the presenter could not be found.
    private Windows.Foundation.Size? _primedPresenterSize;

    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnToggleAmbient   { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }
    public Func<bool>? IsAmbientOn   { get; set; }

    /// <summary>
    /// Optional accessor for the tray icon's screen rect. When provided, the
    /// menu anchors tangent to the icon via <c>CalculatePopupWindowPosition</c>
    /// — placement adapts automatically to the taskbar orientation (left,
    /// right, top, bottom). When null or returning null, falls back to the
    /// cursor position with a 36×36 px exclude rect.
    /// </summary>
    public Func<NativeMethods.RECT?>? GetIconRect { get; set; }

    public TrayContextMenuHost(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        BuildWindow();
        BuildFlyout();

        DeckleShellTrayMenuSource.Log.HostConstructed(ownerHwnd.ToInt64());
    }

    // ── Build window ──────────────────────────────────────────────────────────

    private void BuildWindow()
    {
        _window = new Window();
        _frame = new Frame
        {
            // Transparent background: the frame paints nothing, and the entire
            // window stays invisible thanks to layered alpha=0.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _window.Content = _frame;

        _hwnd = WindowNative.GetWindowHandle(_window);
        _appWindow = _window.AppWindow;

        // Owner = HWND of the tray message-only host. Places the popup in the
        // tray's z-order / activation stack, exactly like a native Win32
        // context menu. Without an owner, the popup would be an autonomous
        // top-level window; activation and dismiss would no longer chain
        // correctly with the tray.
        NativeMethods.SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, _ownerHwnd);

        // Win11 rounded corners: DWM clips the HWND at compositor level.
        // Applies to the whole carrier window, but because it is invisible
        // (alpha=0), the visible effect only appears on the MenuFlyout popup,
        // which follows its own rounded shape.
        uint rounded = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref rounded, sizeof(uint));

        // WS_EX_LAYERED + alpha=0: completely invisible carrier window. The
        // MenuFlyout is rendered in a separate WinUI popup (child HWND), so it
        // remains normally visible. No WS_EX_TRANSPARENT: the window must
        // receive focus for dismiss through Activated to work.
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr newExStyle = new(exStyle.ToInt64() | (long)NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        if (_appWindow is not null)
        {
            _appWindow.IsShownInSwitchers = false;
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }

        _window.Activated += OnWindowActivated;
        _frame.Loaded += OnFrameLoaded;
    }

    // ── Build flyout ──────────────────────────────────────────────────────────

    private void BuildFlyout()
    {
        _flyout = new MenuFlyout
        {
            // Animations disabled: H.NotifyIcon documents a
            // reopen-during-close-animation hack to avoid the carrier window
            // being hidden mid-animation and cutting the transition. By
            // disabling them we avoid the hack entirely; the menu appears and
            // disappears instantly, matching the tempo of a native Windows tray
            // click (TrackPopupMenu is just as abrupt).
            AreOpenCloseAnimationsEnabled = false,
        };

        // Ambient Light first: this is Louis's most frequent toggle command
        // (turn LEDs on/off without navigating into Settings). Window-opening
        // commands come next, separated from lifecycle commands (Restart,
        // Quit) by a final separator.
        //
        // Ambient item built through the reusable TraySwitchMenuItem helper,
        // which applies ToggleSwitchMenuItemStyle (hand-drawn custom pill, see
        // Themes/TrayMenu.xaml) and encapsulates the visual state switch. State
        // is synchronized before each open in Show() through
        // TraySwitchMenuItem.SetState. To add another togglable item: one
        // Create line + one SetState line in Show().
        _ambientItem = TraySwitchMenuItem.Create(
            Loc.Get("TrayMenu_AmbientLight"),
            () =>
            {
                DeckleShellTrayMenuSource.Log.ItemClicked(_ambientItem!.Text);
                Hide("item_click:Ambient");
                OnToggleAmbient?.Invoke();
            });
        _flyout.Items.Add(_ambientItem);

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Logs"),       () => OnShowLogs?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Settings"),   () => OnShowSettings?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Playground"), () => OnShowPlayground?.Invoke()));

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Restart"), () => OnRestart?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Quit"),    () => OnQuit?.Invoke()));

        ApplyNarrowPadding();

        _flyout.Closed += OnFlyoutClosed;
        _flyout.Opened += OnFlyoutOpened;

        DeckleShellTrayMenuSource.Log.FlyoutBuilt(_flyout.Items.Count);
    }

    // ── Force NarrowPadding on every open ────────────────────────────────────
    //
    // NarrowPadding state (compact 32 DIP density, Win11 mouse-driven target)
    // is applied by the framework as soon as a mouse pointer interacts with the
    // menu, but the state resets to DefaultPadding (40 DIP) between flyout
    // Hide/Show cycles. Visible consequence: on the first click after launch,
    // items render at 40 DIP while the carrier window is sized at 32 DIP/item
    // through the _primedSizes cache; content overflows, MenuFlyoutPresenter
    // enables its internal ScrollViewer, and the user can scroll in a menu that
    // should not scroll. From the 2nd click onward, the framework restores
    // NarrowPadding (persisted mouse interaction) and everything aligns.
    //
    // Fix: force NarrowPadding on all items in the Opened handler, when the
    // framework attaches them to the popup visual tree. This is when GoToState
    // can actually apply the state. Aligned with the native Win11 desktop
    // pattern: Sound, Defender, Date/Time, Network all render their tray menu
    // in narrow density.
    private void OnFlyoutOpened(object? sender, object e)
    {
        if (_flyout is null) return;
        foreach (var item in _flyout.Items)
        {
            if (item is MenuFlyoutItem mfi)
                VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
        }
    }

    private MenuFlyoutItem CreateItem(string text, Action action)
    {
        // Pure native MenuFlyoutItem: no Style or Template override, no forced
        // Height. The framework fully handles hover, radius, inset, padding,
        // foreground, DPI scaling, and cell height from natural DesiredSize.
        // The only retemplated tray menu item is Ambient Light, because there
        // is no native slot to graft a switch on the right (see
        // ToggleSwitchMenuItemStyle in Themes/TrayMenu.xaml).
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked(text);
            Hide($"item_click:{text}");
            action();
        };
        return item;
    }

    // Neutralizes the PaddingSizeStates VisualStateGroup by setting each item's
    // Padding to the narrow value. The initial DefaultPadding state is an empty
    // VisualState: it leaves LayoutRoot.Padding at its TemplateBinding value,
    // therefore item.Padding. By setting item.Padding to narrow, the first
    // render is already compact, without waiting for the framework to switch to
    // NarrowPadding (that switch only arrived after the first frame, causing
    // first-click scroll: items rendered at 40 DIP in a window sized for 32).
    // The NarrowPadding state sets the same value, making both states
    // equivalent and keeping narrow density permanently.
    //
    // Narrow density is assumed as the single target: the tray menu opens on
    // mouse right-click (the native touch/DefaultPadding branch does not apply
    // in practice to a desktop app), consistent with the module CLAUDE.md Win11
    // density doctrine.
    private void ApplyNarrowPadding()
    {
        if (_flyout is null) return;
        if (!Application.Current.Resources.TryGetValue(
                "MenuFlyoutItemThemePaddingNarrow", out var narrowObj)
            || narrowObj is not Thickness narrowPadding)
        {
            // Resource not resolved from app scope: leave the prime cycle and
            // Opened handler GoToState(NarrowPadding) as the safety net (the
            // first click may then stay in DefaultPadding).
            return;
        }

        foreach (var item in _flyout.Items)
            if (item is MenuFlyoutItem mfi)
                mfi.Padding = narrowPadding;
    }

    // ── Show ──────────────────────────────────────────────────────────────────

    public void Show()
    {
        if (_disposed || _window is null || _frame is null || _flyout is null || _appWindow is null)
            return;

        long nowTickMs = Environment.TickCount64;
        double msSinceLastShow = _showCount == 0 ? 0 : (nowTickMs - _lastShowTickMs);
        _showCount++;
        _lastShowTickMs = nowTickMs;
        DeckleShellTrayMenuSource.Log.ShowRequested(msSinceLastShow, _showCount);

        if (_ambientItem is not null && IsAmbientOn is not null)
        {
            bool ambientOn = IsAmbientOn();
            TraySwitchMenuItem.SetState(_ambientItem, ambientOn);
            DeckleShellTrayMenuSource.Log.AmbientStateRead(ambientOn);
        }

        // Anchor + exclude: prefer the real tray icon rect
        // (Shell_NotifyIconGetRect API). This makes positioning automatically
        // correct regardless of taskbar orientation: above if the taskbar is at
        // the bottom, right if the taskbar is on the left, etc., without
        // depending on the click point on the icon. Fallback to cursor position
        // if the shell does not know (icon not yet registered, hidden in
        // overflow, or explorer.exe restarting).
        NativeMethods.RECT? iconRect = GetIconRect?.Invoke();
        POINT anchor;
        NativeMethods.RECT exclude;
        int parentRectX, parentRectY, parentRectW, parentRectH;
        if (iconRect is { } icon)
        {
            anchor = new POINT { X = (icon.left + icon.right) / 2, Y = (icon.top + icon.bottom) / 2 };
            exclude = icon;
            parentRectX = icon.left;
            parentRectY = icon.top;
            parentRectW = icon.right - icon.left;
            parentRectH = icon.bottom - icon.top;
        }
        else
        {
            NativeMethods.GetCursorPos(out POINT cursor);
            anchor = cursor;
            exclude = new NativeMethods.RECT
            {
                left   = cursor.X - CursorExcludeHalfExtent,
                top    = cursor.Y - CursorExcludeHalfExtent,
                right  = cursor.X + CursorExcludeHalfExtent,
                bottom = cursor.Y + CursorExcludeHalfExtent,
            };
            // Degenerate parent rect (0×0 at the anchor point): convention
            // documented on DeckleWindowingSource.PopupAnchored for popups
            // where the app has no identified parent control.
            parentRectX = cursor.X;
            parentRectY = cursor.Y;
            parentRectW = 0;
            parentRectH = 0;
        }

        // Real DPI of the monitor under the anchor point. XamlRoot
        // RasterizationScale would reflect the DPI of the monitor where the
        // carrier window is hidden (typically primary), not where the tray
        // lives: mismatch on multi-monitor setups or a 150% primary screen.
        IntPtr monitor = TrayMenuNativeMethods.MonitorFromPoint(
            anchor, TrayMenuNativeMethods.MONITOR_DEFAULTTONEAREST);
        TrayMenuNativeMethods.GetDpiForMonitor(
            monitor, TrayMenuNativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale = dpiX / 96.0;

        var (width, height) = MeasureFlyout(scale);
        var size = new TrayMenuNativeMethods.SIZE { cx = width, cy = height };

        NativeMethods.RECT popup = default;
        TrayMenuNativeMethods.CalculatePopupWindowPosition(
            ref anchor, ref size,
            NativeMethods.TPM_BOTTOMALIGN | TrayMenuNativeMethods.TPM_WORKAREA,
            ref exclude, ref popup);

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = popup.left,
            Y = popup.top,
            Width = popup.right - popup.left,
            Height = popup.bottom - popup.top,
        });

        _isVisible = true;
        NativeMethods.ShowWindow(_hwnd, TrayMenuNativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(_hwnd);

        // Canonical Windowing emission: common WindowPositioned trunk
        // (effective HWND state after MoveAndResize + ShowWindow +
        // SetForegroundWindow) and specialized PopupAnchored (parent rect =
        // tray icon or degenerate cursor rect).
        WindowingProbe.EmitWindowPositioned(_hwnd, "tray-popup", "CursorRelative");
        WindowingProbe.EmitPopupAnchored(
            _hwnd, "tray-popup",
            parentRectX, parentRectY, parentRectW, parentRectH);

        // FlyoutPlacementMode.Full: opens the menu at the exact target
        // location (our frame). Without Full, default Top placement places the
        // popup above the frame, adding a vertical offset on top of the one
        // already calculated by CalculatePopupWindowPosition; the menu jumps up
        // by roughly one menu height. Since the carrier window is already
        // positioned at the exact desired coordinate, Full neutralizes that
        // offset.
        //
        // Trade-off (MS docs + 2026-05-31 repro): Full also stretches the
        // presenter to fill the carrier window. The visible menu size is
        // therefore dictated by MeasureFlyout; the 8 DIP FlyoutFrameMargin we
        // added showed up as a Mica gap at the bottom (stretched presenter not
        // consuming it as padding). See module CLAUDE.md / JOURNAL.
        _flyout.ShowAt(_frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient,
            Placement = FlyoutPlacementMode.Full,
        });
        DeckleShellTrayMenuSource.Log.FlyoutShownAt();
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    // The `reason` parameter qualifies the dismiss origin at the call site:
    // "deactivated" (activation loss), "flyout_closed" (Flyout.Closed),
    // "item_click:<label>" (item selection). Traced on the Hidden event to
    // distinguish close paths in JSONL.
    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        DeckleShellTrayMenuSource.Log.Hidden(reason);
        _flyout?.Hide();
        if (_hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnFrameLoaded(object sender, RoutedEventArgs e)
    {
        DeckleShellTrayMenuSource.Log.FrameLoaded(_primed);

        if (_primed || _flyout is null || _frame is null) return;
        _primed = true;

        // WS_POPUPWINDOW post-Loaded: clears the caption inherited from
        // OverlappedPresenter even when SetBorderAndTitleBar(false, false) was
        // called. Without this, the HWND keeps a residual WS_CAPTION visible to
        // DWM, which interferes with rounded-corner rendering.
        IntPtr styleBefore = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        DeckleShellTrayMenuSource.Log.PrimeCycleStarted(
            styleBefore.ToInt64(), newStyle.ToInt64());

        // Prime measure: prime the visual tree so native MenuFlyoutItems have
        // their ControlTemplate applied and DesiredSize measurable on the first
        // real Show(). A synchronous ShowAt + Hide cycle is insufficient:
        // 2026-05-25 app.jsonl observation: show_count=1 measured desired_w/h=0
        // for all native items. Cause: immediate synchronous Hide cuts the
        // prime before WinUI's layout pass has run on MenuFlyoutPresenter
        // items. Fix: defer Hide through DispatcherQueue.TryEnqueue(Low); Low
        // priority inserts the callback after the layout pass and initial
        // popup render frame have occurred. At that point each item has its
        // correct DesiredSize, and the visual tree remains "warmed" for the
        // process lifetime.
        var sw = Stopwatch.StartNew();
        _flyout.ShowAt(_frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // Capture DesiredSize for items attached to the popup visual tree,
            // after forcing NarrowPadding state on each item. The framework
            // switches to NarrowPadding anyway as soon as a mouse/pen/keyboard
            // pointer interacts with the menu (see PaddingSizeStates
            // VisualState in DefaultMenuFlyoutItemStyle, WindowsAppSDK
            // generic.xaml line 24058); we accelerate that switch during the
            // prime cycle so the cache reflects the final size (≈ 32 DIP/item)
            // rather than the initial DefaultPadding size (≈ 40). Without this
            // force, the carrier window was sized to the initial size while the
            // internal popup (following NarrowPadding state) rendered more
            // compact, creating a visible Mica gap at the bottom.
            if (_flyout is not null)
            {
                foreach (var item in _flyout.Items)
                {
                    if (item is MenuFlyoutItem mfi)
                        VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
                }
                // Force a layout pass so the new Padding values applied by the
                // VisualState Storyboard are effective in the DesiredSize we
                // are about to capture.
                _frame!.UpdateLayout();

                _primedSizes.Clear();
                foreach (var item in _flyout.Items)
                    _primedSizes[item] = item.DesiredSize;

                // Capture the real presenter size (walking up from the first
                // item, attached at this point). Its DesiredSize includes its
                // padding + border, so it exactly reflects the visible card; in
                // contrast, the item sum ignores those and we compensated with
                // an imprecise flat margin.
                _primedPresenterSize = null;
                if (_flyout.Items.Count > 0)
                {
                    var presenter = FindAncestorPresenter(_flyout.Items[0]);
                    if (presenter is not null)
                    {
                        presenter.UpdateLayout();
                        _primedPresenterSize = presenter.DesiredSize;
                    }
                }
            }

            _flyout?.Hide();
            sw.Stop();
            DeckleShellTrayMenuSource.Log.PrimeCycleCompleted(sw.Elapsed.TotalMilliseconds);
        });
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        DeckleShellTrayMenuSource.Log.WindowActivated(
            args.WindowActivationState.ToString(), _isVisible);

        if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
            Hide("deactivated");
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        DeckleShellTrayMenuSource.Log.FlyoutClosed(_isVisible);

        if (_isVisible) Hide("flyout_closed");
    }

    // Walks up the visual tree from a descendant to the MenuFlyoutPresenter
    // that hosts popup items. Returns null if the tree is not mounted yet
    // (presenter absent from the tree at call time).
    private static MenuFlyoutPresenter? FindAncestorPresenter(DependencyObject start)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is MenuFlyoutPresenter presenter)
                return presenter;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Sizes the carrier window to the real MenuFlyoutPresenter DesiredSize,
    // captured during the prime cycle (_primedPresenterSize). This size
    // includes the presenter's own padding and border, so it exactly matches
    // the card painted by the popup. Because Full stretches the presenter to
    // the carrier window, sizing the window to this value makes stretching
    // neutral: no Mica gap (oversize), no scroll (undersize).
    //
    // The loop below remains for per-item diagnostics (ItemAttachmentChecked /
    // ItemMeasured events in JSONL) and to feed the fallback. DesiredSize
    // values are read from the _primedSizes cache (items attached during the
    // prime cycle) rather than through detached item.Measure(), which returns
    // unstable values (see module JOURNAL.md).
    //
    // Fallback (presenter not captured during prime, or prime not yet run):
    // sum of item heights + FlyoutFrameMargin × 2. Historical, imprecise path
    // (the 8 DIP flat margin overestimated the presenter's real chrome
    // ≈ 4-6 DIP, causing the gap); kept as a guard against zero-size popup.

    private (int width, int height) MeasureFlyout(double scale)
    {
        if (_flyout is null) return (0, 0);

        double width = 0;
        double height = 0;
        int idx = 0;
        foreach (var item in _flyout.Items)
        {
            string itemText = item switch
            {
                MenuFlyoutItem mi => mi.Text,
                MenuFlyoutSeparator => "<separator>",
                _ => "<unknown>",
            };

            Windows.Foundation.Size desired;
            if (_primedSizes.TryGetValue(item, out var cached))
            {
                desired = cached;
            }
            else
            {
                // Safety fallback: the prime cycle has not populated the cache
                // yet. Detached measurement is accepted for lack of a better
                // option; at worst the popup displays the native compressed
                // height.
                item.Measure(new Windows.Foundation.Size(10_000, 10_000));
                desired = item.DesiredSize;
            }
            width = Math.Max(width, desired.Width);
            height += desired.Height;

            DeckleShellTrayMenuSource.Log.ItemMeasured(
                idx, itemText, item.GetType().Name,
                desired.Width, desired.Height);
            idx++;
        }

        double dipW;
        double dipH;
        if (_primedPresenterSize is { } presenterSize
            && presenterSize.Width > 0 && presenterSize.Height > 0)
        {
            // Exact size of the real presenter; Full has nothing left to stretch.
            dipW = presenterSize.Width;
            dipH = presenterSize.Height;
        }
        else
        {
            // Imprecise fallback: item sum + flat margin.
            dipW = width + FlyoutFrameMargin * 2;
            dipH = height + FlyoutFrameMargin * 2;
        }

        // Ceiling rather than truncation: prefer a possible sub-pixel gap
        // (invisible) over a one-pixel undersize that would reactivate
        // presenter scrolling.
        int physW = (int)Math.Ceiling(dipW * scale);
        int physH = (int)Math.Ceiling(dipH * scale);

        DeckleShellTrayMenuSource.Log.FlyoutMeasured(dipW, dipH, physW, physH, scale);

        return (physW, physH);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            if (_frame is not null)
                _frame.Loaded -= OnFrameLoaded;
            if (_flyout is not null)
            {
                _flyout.Closed -= OnFlyoutClosed;
                _flyout.Opened -= OnFlyoutOpened;
            }
            _window.Close();
        }

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
