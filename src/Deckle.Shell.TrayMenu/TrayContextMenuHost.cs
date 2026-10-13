using System;
using Deckle.Core;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray context menu — WinUI 3 SecondWindow pattern ─────────────────────────
//
// Host that bridges the Win32 tray (Shell_NotifyIcon) to the native WinUI 3
// MenuFlyout. The pattern, inspired by the open-source H.NotifyIcon library
// (MIT), fits in a transparent WinUI window pre-created at init (the
// TrayCarrierWindow): on tray right-click, it is positioned at the icon and a
// MenuFlyout opens on it in FlyoutShowMode.Transient. Without this carrier
// window, a MenuFlyout cannot anchor to a message-only HWND; it needs a
// XamlRoot.
//
// Dismiss: the carrier's Deactivated covers click-outside and global focus
// loss; each item's Click closes explicitly. No close animation, to avoid the
// reopen-during-close hack H.NotifyIcon has to use
// (AreOpenCloseAnimationsEnabled = false).
//
// Observability: positioning events are emitted by the carrier through
// WindowingProbe. The local Deckle.Shell.TrayMenu sub-provider traces only
// tray-menu specifics: prime cycle, item measurement, dismiss reason.

public sealed partial class TrayContextMenuHost : IDisposable
{
    private readonly TrayCarrierWindow _carrier;

    private MenuFlyout? _flyout;

    private bool _isVisible;
    private bool _disposed;

    // Trackers for the ShowRequested event: inter-open delta calculation,
    // useful to correlate the measurement pipeline with possible prolonged
    // inactivity of the primed visual tree between two opens.
    private long _lastShowTickMs;
    private int _showCount;

    // Cache of DesiredSize values captured with items attached to the popup
    // visual tree (prime cycle, then every real open). MeasureFlyout() reads
    // this cache instead of calling detached item.Measure(), which can collapse
    // toward the item's MinHeight. Item height follows the padding rule: items
    // start at 40 DIP and the framework narrows them to 32 only after the first
    // pointer interaction, so ApplyNarrowPadding sets the narrow padding at build.
    private readonly System.Collections.Generic.Dictionary<MenuFlyoutItemBase, Windows.Foundation.Size> _primedSizes = new();

    // DesiredSize of the real MenuFlyoutPresenter, captured with the items
    // attached. Includes the presenter's own padding and border: the exact
    // size of the visible card. Stored only when it holds every item
    // (TrayMenuSizing.HoldsItems), and checked again at each measure. Null
    // until a capture succeeds.
    private (double Width, double Height)? _primedPresenterSize;

    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }

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
        _carrier = new TrayCarrierWindow(ownerHwnd, "tray-popup");
        _carrier.Loaded += OnCarrierLoaded;
        _carrier.Deactivated += OnCarrierDeactivated;
        BuildFlyout();

        DeckleShellTrayMenuSource.Log.HostConstructed();
        DeckleShellTrayMenuSource.Log.HostConstructedDetail(ownerHwnd.ToInt64());
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    // The carrier raises Loaded once, after finalizing its popup style: the
    // XamlRoot is ready, so the flyout is primed exactly once here.
    private void OnCarrierLoaded()
    {
        if (_flyout is null) return;
        PrimeFlyout();
    }

    private void OnCarrierDeactivated()
    {
        if (_isVisible) Hide("deactivated");
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _carrier.Loaded -= OnCarrierLoaded;
        _carrier.Deactivated -= OnCarrierDeactivated;
        if (_flyout is not null)
        {
            _flyout.Closed -= OnFlyoutClosed;
            _flyout.Opened -= OnFlyoutOpened;
        }
        _carrier.Dispose();

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
