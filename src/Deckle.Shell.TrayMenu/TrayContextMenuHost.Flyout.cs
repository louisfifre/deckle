// TrayContextMenuHost — MenuFlyout construction, item creation, padding, close.

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

public sealed partial class TrayContextMenuHost
{
    // ── Build flyout ──────────────────────────────────────────────────────────

    private void BuildFlyout()
    {
        _flyout = new MenuFlyout
        {
            // Hiding the carrier mid-animation would cut the transition, which
            // H.NotifyIcon works around with a reopen-during-close hack. Without
            // animations the menu opens and closes instantly, as a native tray
            // menu (TrackPopupMenu) does.
            AreOpenCloseAnimationsEnabled = false,
        };

        // The rare items only: the window-opening commands, then the lifecycle
        // commands (Restart, Quit) after a separator. Module switches and the
        // dictation actions live in the left-click quick-actions panel
        // (TrayQuickActionsHost), where a switch can flip without closing.
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
    // The framework applies NarrowPadding (32 DIP, the Win11 mouse density) on
    // pointer interaction, but resets to DefaultPadding (40 DIP) between
    // Hide/Show cycles: items then overflow a carrier sized for 32 DIP/item and
    // the presenter scrolls. GoToState only takes effect once the items are
    // attached to the popup tree, hence the Opened handler.
    //
    // A real open then re-captures the sizes for the next open, so a fallback
    // size or a bad capture does not stick for the process lifetime. The prime
    // cycle's own open is excluded (_isVisible is false): it captures in its
    // own callback.
    private void OnFlyoutOpened(object? sender, object e)
    {
        if (_flyout is null) return;
        foreach (var item in _flyout.Items)
        {
            if (item is MenuFlyoutItem mfi)
                VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
        }

        if (!_isVisible) return;
        _carrier.Frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_isVisible && _flyout is { IsOpen: true }) CaptureSizes("opened");
        });
    }

    private MenuFlyoutItem CreateItem(string text, Action action)
    {
        // Pure native MenuFlyoutItem: no Style or Template override, no forced
        // Height. The framework owns hover, padding, DPI scaling and cell height.
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked();
            DeckleShellTrayMenuSource.Log.ItemClickedDetail(text);
            Hide($"item_click:{text}");
            action();
        };
        return item;
    }

    // Neutralizes the PaddingSizeStates VisualStateGroup: the initial
    // DefaultPadding state is empty and leaves LayoutRoot.Padding bound to
    // item.Padding, so a narrow item.Padding makes the first render compact
    // and both states equivalent. Narrow density is the single target: the
    // menu opens on a mouse right-click.
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

    private void OnFlyoutClosed(object? sender, object e)
    {
        DeckleShellTrayMenuSource.Log.FlyoutClosed(_isVisible);

        if (_isVisible) Hide("flyout_closed");
    }
}
