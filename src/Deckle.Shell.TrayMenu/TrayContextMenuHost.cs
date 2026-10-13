using System;
using Deckle.Core;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray context menu — WinUI 3 SecondWindow pattern ─────────────────────────
//
// Bridges the Win32 tray (Shell_NotifyIcon) to the native WinUI 3 MenuFlyout.
// The pattern, inspired by the open-source H.NotifyIcon library (MIT), opens
// the MenuFlyout on a transparent WinUI window pre-created at init (the
// TrayCarrierWindow), positioned at the icon on tray right-click.
//
// Dismiss: the carrier's Deactivated covers click-outside and global focus
// loss; each item's Click closes explicitly.

public sealed partial class TrayContextMenuHost : IDisposable
{
    private readonly TrayCarrierWindow _carrier;

    private MenuFlyout? _flyout;

    private bool _isVisible;
    private bool _disposed;

    private long _lastShowTickMs;
    private int _showCount;

    // DesiredSize values captured with items attached to the popup visual tree
    // (prime cycle, then every real open). MeasureFlyout() reads this cache
    // instead of a detached item.Measure(), which can collapse toward the
    // item's MinHeight.
    private readonly System.Collections.Generic.Dictionary<MenuFlyoutItemBase, Windows.Foundation.Size> _primedSizes = new();

    // DesiredSize of the real MenuFlyoutPresenter, captured with the items
    // attached. Includes the presenter's own padding and border: the exact
    // size of the visible card. Stored only when it holds every item
    // (TrayMenuSizing.HoldsItems), and checked again at each measure.
    private (double Width, double Height)? _primedPresenterSize;

    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }

    /// <summary>
    /// Tray icon screen rect accessor; the menu anchors tangent to the icon.
    /// Null, or a null result, falls back to the cursor.
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
