using System;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TrayMenu;
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

public sealed partial class TrayContextMenuHost : IDisposable
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
    private MenuFlyoutItem? _autocorrectItem;
    private MenuFlyoutItem? _taskbarCoverItem;
    private MenuFlyoutItem? _precisionScrollItem;
    private MenuFlyoutItem? _transcribeFilesItem;
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

    public Action? OnTranscribeFiles { get; set; }
    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnToggleAmbient   { get; set; }
    public Action? OnToggleAutocorrect { get; set; }
    public Action? OnToggleTaskbarCover { get; set; }
    public Action? OnTogglePrecisionScroll { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }
    public Func<bool>? IsAmbientOn   { get; set; }
    public Func<bool>? IsAutocorrectOn { get; set; }
    public Func<bool>? IsTaskbarCoverOn { get; set; }
    public Func<bool>? IsPrecisionScrollOn { get; set; }

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

        DeckleShellTrayMenuSource.Log.HostConstructed();
        DeckleShellTrayMenuSource.Log.HostConstructedDetail(ownerHwnd.ToInt64());
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnFrameLoaded(object sender, RoutedEventArgs e)
    {
        DeckleShellTrayMenuSource.Log.FrameLoaded(_primed);

        if (_primed || _flyout is null || _frame is null) return;
        _primed = true;

        FinalizePopupWindow();
        PrimeFlyout();
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
