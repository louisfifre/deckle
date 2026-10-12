using System;
using System.Collections.Generic;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

// ─── Tray quick-actions panel — left click ────────────────────────────────────
//
// The panel the tray icon opens on left click, in the spirit of Windows 11
// quick settings: a Record action, "Transcribe audio files…", then one
// ToggleSwitch row per switchable module. Unlike the right-click MenuFlyout,
// whose every item click closes the menu, this is a plain Flyout with real
// controls: flipping a switch leaves the panel open. Actions (Record,
// Transcribe files) close it first, so the window that had focus before the
// click gets it back (paste at Stop relies on it).
//
// Same carrier pattern as TrayContextMenuHost: an invisible TrayCarrierWindow
// of its own, anchored on the icon, sized to the real FlyoutPresenter
// captured by a single prime cycle, Placement Full, animations off.
//
// Dismiss, as Windows quick settings: click outside or focus loss (carrier
// Deactivated), Esc (Flyout light dismiss → Closed), and a second left click
// on the icon. That click dismisses the panel first (the taskbar takes
// activation, which deactivates the carrier or, when the panel's popup held
// focus, light-dismisses the flyout), and only then does WM_LBUTTONUP reach
// Show(); a Show() within ReopenGuardMs of such a dismiss is therefore
// ignored, so the click closes the panel instead of reopening it.

public sealed partial class TrayQuickActionsHost : IDisposable
{
    // Window between a dismiss (deactivation or light dismiss) and a Show()
    // call during which the Show() is read as the same click on the icon that
    // caused the dismiss. A tray click's WM_LBUTTONUP follows the dismiss by
    // a few milliseconds; 250 ms leaves room for a loaded UI thread while
    // staying well under a deliberate second click.
    private const long ReopenGuardMs = 250;

    private readonly TrayCarrierWindow _carrier;
    private readonly Flyout _flyout;

    private IReadOnlyList<TrayToggle> _toggles = Array.Empty<TrayToggle>();
    private IReadOnlyList<TrayToggle> _footerToggles = Array.Empty<TrayToggle>();

    private bool _isVisible;
    private bool _primed;
    private bool _disposed;
    // TickCount64 of the last dismiss the user caused outside Show() (carrier
    // deactivated, flyout light-dismissed); 0 = none yet (TickCount64 counts
    // from boot, so a real tick is never 0).
    private long _lastDismissTick;
    // Reason of the last Hide, for the "dismissed while opening" trace.
    private string _lastHideReason = "";

    /// <summary>
    /// Starts or stops a dictation, as the transcription hotkey does.
    /// Unwired = the Record button is absent.
    /// </summary>
    public Action? OnRecord
    {
        get => _onRecord;
        set { _onRecord = value; InvalidateContent(); }
    }
    private Action? _onRecord;

    /// <summary>Read at each open to label the Record button Record or Stop recording.</summary>
    public Func<bool>? IsRecording { get; set; }

    /// <summary>Opens the audio-file picker. Unwired = the button is absent.</summary>
    public Action? OnTranscribeFiles
    {
        get => _onTranscribeFiles;
        set { _onTranscribeFiles = value; InvalidateContent(); }
    }
    private Action? _onTranscribeFiles;

    /// <summary>
    /// Module switches shown under the actions, in order. Presence is the
    /// caller's: an absent module simply has no entry.
    /// </summary>
    public IReadOnlyList<TrayToggle> Toggles
    {
        get => _toggles;
        set { _toggles = value ?? Array.Empty<TrayToggle>(); InvalidateContent(); }
    }

    /// <summary>
    /// Switches shown last, separated from <see cref="Toggles"/> by a divider
    /// (Ambient Light: the most frequent one, nearest the icon).
    /// </summary>
    public IReadOnlyList<TrayToggle> FooterToggles
    {
        get => _footerToggles;
        set { _footerToggles = value ?? Array.Empty<TrayToggle>(); InvalidateContent(); }
    }

    /// <summary>
    /// Tray icon screen rect accessor; the panel anchors tangent to the icon.
    /// Null, or a null result, falls back to the cursor.
    /// </summary>
    public Func<NativeMethods.RECT?>? GetIconRect { get; set; }

    public TrayQuickActionsHost(IntPtr ownerHwnd)
    {
        _carrier = new TrayCarrierWindow(ownerHwnd, "tray-panel");
        _carrier.Loaded += OnCarrierLoaded;
        _carrier.Deactivated += OnCarrierDeactivated;

        _flyout = new Flyout
        {
            // Same reason as the context menu: hiding the carrier mid-close
            // would cut the animation; the panel appears and disappears at
            // once, like the native tray flyouts.
            AreOpenCloseAnimationsEnabled = false,
        };
        _flyout.Opened += OnFlyoutOpened;
        _flyout.Closed += OnFlyoutClosed;

        DeckleShellTrayMenuSource.Log.HostConstructed();
        DeckleShellTrayMenuSource.Log.HostConstructedDetail(ownerHwnd.ToInt64());
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnCarrierLoaded()
    {
        if (_primed) return;
        _primed = true;
        PrimeFlyout();
    }

    private void OnCarrierDeactivated()
    {
        if (!_isVisible) return;
        _lastDismissTick = Environment.TickCount64;
        Hide("deactivated");
    }

    // The flyout closed on its own (Esc, or a click outside while its popup
    // held activation): a dismiss the user caused, so the reopen guard
    // applies to it as to a deactivation.
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

        _carrier.Loaded -= OnCarrierLoaded;
        _carrier.Deactivated -= OnCarrierDeactivated;
        _flyout.Opened -= OnFlyoutOpened;
        _flyout.Closed -= OnFlyoutClosed;
        _carrier.Dispose();

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
