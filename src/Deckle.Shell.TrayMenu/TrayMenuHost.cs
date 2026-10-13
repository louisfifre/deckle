// TrayMenuHost — a native MenuFlyout the tray icon opens, from an invisible anchor window.

using System;
using System.Collections.Generic;
using Deckle.Core;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray menu ────────────────────────────────────────────────────────────────
//
// Both tray surfaces are instances of this host: the context menu on right
// click (the rare items) and the quick-actions menu on left click (Record,
// Transcribe audio files…, one toggle item per switchable module). Each is a
// plain WinUI MenuFlyout at its natural size: WinUI measures it, places it
// beside its anchor and draws it in a popup window of its own (a MenuFlyout is
// unconstrained to its root by default). Nothing here measures, primes or
// sizes a window to the menu: the 2026 "menu one item tall" bug lived in that
// machinery, which stretched a Placement-Full presenter over a carrier window
// sized by hand (TrayMenuBugNotes.md).
//
// A MenuFlyout needs a XamlRoot to open from. The anchor is TrayCarrierWindow,
// an invisible WinUI window placed exactly on the tray icon's rect, so the
// menu opens beside the icon as a menu opens beside a button: above it for a
// taskbar at the bottom, to its right for a taskbar on the left, and so on
// (TrayMenuPlacement). Transient: a menu takes no keyboard focus.
//
// Dismiss: click outside or focus loss (anchor Deactivated), Esc (flyout
// Closed), an item click (the item hides the menu first, then its action
// runs, so a window the action needs gets activation back), and a second
// click on the icon. That click dismisses the menu first (the taskbar takes
// activation), and only then does the button-up reach Show(); a Show() within
// ReopenGuardMs of such a dismiss is read as that same click and ignored, so
// the click closes the menu instead of reopening it.

public sealed partial class TrayMenuHost : IDisposable
{
    // Window between a dismiss the user caused and a Show() during which the
    // Show() is read as the click that caused the dismiss. The click's
    // button-up follows the dismiss by a few milliseconds; 250 ms leaves room
    // for a loaded UI thread while staying well under a deliberate second click.
    private const long ReopenGuardMs = 250;

    private readonly TrayCarrierWindow _carrier;
    private readonly MenuFlyout _flyout;
    private readonly string _windowName;
    private readonly List<(TrayMenuEntry Entry, MenuFlyoutItemBase Item)> _items = new();

    private IReadOnlyList<TrayMenuEntry> _entries = Array.Empty<TrayMenuEntry>();
    private bool _isVisible;
    private bool _disposed;
    // TickCount64 of the last dismiss the user caused outside Show(); 0 = none
    // yet (TickCount64 counts from boot, so a real tick is never 0).
    private long _lastDismissTick;

    /// <summary>
    /// The entries, in order. The App composes them from module presence;
    /// setting them rebuilds the items. Labels and toggle states are read at
    /// each open, not here.
    /// </summary>
    public IReadOnlyList<TrayMenuEntry> Entries
    {
        get => _entries;
        set
        {
            _entries = value ?? Array.Empty<TrayMenuEntry>();
            BuildItems();
        }
    }

    /// <summary>
    /// Tray icon screen rect accessor, in physical pixels. The menu opens
    /// beside that rect; null, or a null result, falls back to the cursor.
    /// </summary>
    public Func<NativeMethods.RECT?>? GetIconRect { get; set; }

    /// <param name="ownerHwnd">The tray message-only host HWND.</param>
    /// <param name="windowName">Logical name from DeckleWindowingSource's
    /// closed <c>window</c> vocabulary ("tray-popup", "tray-actions").</param>
    public TrayMenuHost(IntPtr ownerHwnd, string windowName)
    {
        _windowName = windowName;
        _carrier = new TrayCarrierWindow(ownerHwnd, windowName);
        _carrier.Deactivated += OnCarrierDeactivated;

        _flyout = new MenuFlyout
        {
            // No open or close animation: the menu appears and disappears at
            // once, like a native tray menu (TrackPopupMenu is as abrupt), and
            // hiding the anchor right after a close cuts nothing.
            AreOpenCloseAnimationsEnabled = false,
        };
        _flyout.Closed += OnFlyoutClosed;

        DeckleShellTrayMenuSource.Log.HostConstructed();
        DeckleShellTrayMenuSource.Log.HostConstructedDetail(ownerHwnd.ToInt64());
    }

    // ── Dismiss sources ───────────────────────────────────────────────────────

    private void OnCarrierDeactivated()
    {
        if (!_isVisible) return;
        _lastDismissTick = Environment.TickCount64;
        Hide("deactivated");
    }

    // Esc, or a click outside while the flyout's popup held the pointer: a
    // dismiss the user caused, so the reopen guard applies to it too.
    private void OnFlyoutClosed(object? sender, object e)
    {
        DeckleShellTrayMenuSource.Log.FlyoutClosed(_isVisible);
        if (!_isVisible) return;
        _lastDismissTick = Environment.TickCount64;
        Hide("flyout_closed");
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _carrier.Deactivated -= OnCarrierDeactivated;
        _flyout.Closed -= OnFlyoutClosed;
        _carrier.Dispose();

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
