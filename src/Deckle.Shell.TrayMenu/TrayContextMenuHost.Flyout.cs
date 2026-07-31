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
            // Animations disabled: H.NotifyIcon documents a
            // reopen-during-close-animation hack to avoid the carrier window
            // being hidden mid-animation and cutting the transition. By
            // disabling them we avoid the hack entirely; the menu appears and
            // disappears instantly, matching the tempo of a native Windows tray
            // click (TrackPopupMenu is just as abrupt).
            AreOpenCloseAnimationsEnabled = false,
        };

        // Ambient Light first: this is Louis's most frequent toggle command
        // (turn LEDs on/off without navigating into Settings). The command
        // group comes next — file transcription, then the window-opening
        // commands — separated from lifecycle commands (Restart, Quit) by a
        // final separator.
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
                DeckleShellTrayMenuSource.Log.ItemClicked();
                DeckleShellTrayMenuSource.Log.ItemClickedDetail(_ambientItem!.Text);
                Hide("item_click:Ambient");
                OnToggleAmbient?.Invoke();
            });
        _flyout.Items.Add(_ambientItem);

        _autocorrectItem = TraySwitchMenuItem.Create(
            Loc.Get("TrayMenu_Autocorrect"),
            () =>
            {
                DeckleShellTrayMenuSource.Log.ItemClicked();
                DeckleShellTrayMenuSource.Log.ItemClickedDetail(_autocorrectItem!.Text);
                Hide("item_click:Autocorrect");
                OnToggleAutocorrect?.Invoke();
            });
        _flyout.Items.Add(_autocorrectItem);

        _taskbarCoverItem = TraySwitchMenuItem.Create(
            Loc.Get("TrayMenu_TaskbarCover"),
            () =>
            {
                DeckleShellTrayMenuSource.Log.ItemClicked();
                DeckleShellTrayMenuSource.Log.ItemClickedDetail(_taskbarCoverItem!.Text);
                Hide("item_click:TaskbarCover");
                OnToggleTaskbarCover?.Invoke();
            });
        _flyout.Items.Add(_taskbarCoverItem);

        _precisionScrollItem = TraySwitchMenuItem.Create(
            Loc.Get("TrayMenu_PrecisionScroll"),
            () =>
            {
                DeckleShellTrayMenuSource.Log.ItemClicked();
                DeckleShellTrayMenuSource.Log.ItemClickedDetail(_precisionScrollItem!.Text);
                Hide("item_click:PrecisionScroll");
                OnTogglePrecisionScroll?.Invoke();
            });
        _flyout.Items.Add(_precisionScrollItem);

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _transcribeFileItem = CreateItem(Loc.Get("TrayMenu_TranscribeFile"), () => OnTranscribeFile?.Invoke());
        _flyout.Items.Add(_transcribeFileItem);
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
            DeckleShellTrayMenuSource.Log.ItemClicked();
            DeckleShellTrayMenuSource.Log.ItemClickedDetail(text);
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

    private void OnFlyoutClosed(object? sender, object e)
    {
        DeckleShellTrayMenuSource.Log.FlyoutClosed(_isVisible);

        if (_isVisible) Hide("flyout_closed");
    }
}
