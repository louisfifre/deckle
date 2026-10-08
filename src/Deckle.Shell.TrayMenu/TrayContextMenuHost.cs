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
    // Flat margin for the MeasureFlyout fallback path (presenter not captured
    // during the prime cycle). 4 px per side. Imprecise by nature; it
    // overestimated the presenter's real chrome (≈ 4-6 DIP), creating the Mica
    // gap when it drove measurement. The nominal path now reads the real
    // presenter's DesiredSize (_primedPresenterSize).
    private const double FlyoutFrameMargin = 4.0;

    private readonly TrayCarrierWindow _carrier;

    private MenuFlyout? _flyout;
    private MenuFlyoutItem? _ambientItem;
    private MenuFlyoutItem? _autocorrectItem;
    private MenuFlyoutItem? _taskbarCoverItem;
    private MenuFlyoutItem? _precisionScrollItem;
    private MenuFlyoutItem? _transcribeFilesItem;

    private bool _isVisible;
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
