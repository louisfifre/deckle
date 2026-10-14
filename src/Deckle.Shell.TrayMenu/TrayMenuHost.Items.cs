// TrayMenuHost — the menu items built from the entries, and their state at each open.

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayMenuHost
{
    // Command items are pure native MenuFlyoutItems: no Style or Template
    // override, no forced Height. The framework owns hover, radius, inset,
    // foreground, DPI scaling and cell height. A toggle is a TraySwitchMenuItem,
    // the same row with an on/off pill at its right edge; not a
    // ToggleMenuFlyoutItem, whose check-mark column would push every other
    // item of the menu to the right (see TraySwitchMenuItem). A click flips
    // the module and closes the menu, as in any menu.
    private void BuildItems()
    {
        _flyout.Items.Clear();
        _items.Clear();

        foreach (TrayMenuEntry entry in _entries)
        {
            MenuFlyoutItemBase item = entry switch
            {
                TrayMenuCommand command => CreateCommandItem(command),
                TrayToggle toggle => CreateToggleItem(toggle),
                TrayMenuSeparator => new MenuFlyoutSeparator(),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(Entries), entry.GetType().Name, "Unknown tray menu entry."),
            };
            _flyout.Items.Add(item);
            _items.Add((entry, item));
        }

        ApplyNarrowPadding();
        DeckleShellTrayMenuSource.Log.FlyoutBuilt(_flyout.Items.Count);
    }

    // An item hides the menu BEFORE invoking: hiding the anchor hands
    // activation back to the window the user was in, which a dictation's
    // paste at Stop needs.
    private MenuFlyoutItem CreateCommandItem(TrayMenuCommand command)
    {
        var item = new MenuFlyoutItem { Text = command.Label() };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked();
            DeckleShellTrayMenuSource.Log.ItemClickedDetail(command.Id);
            Hide($"item_click:{command.Id}");
            command.Invoke();
        };
        return item;
    }

    private TraySwitchMenuItem CreateToggleItem(TrayToggle toggle)
    {
        var item = new TraySwitchMenuItem { Text = toggle.Label };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked();
            DeckleShellTrayMenuSource.Log.ItemClickedDetail(toggle.Id);
            Hide($"item_click:{toggle.Id}");
            toggle.Toggle();
        };
        return item;
    }

    // Reflects module and engine state at each open: a command label may
    // follow a state (Record / Stop recording), and a toggle's pill follows
    // its module's Enabled flag, which its Settings page can change meanwhile.
    private void SyncItems()
    {
        foreach (var (entry, item) in _items)
        {
            switch (entry)
            {
                case TrayToggle toggle when item is TraySwitchMenuItem switchItem:
                    switchItem.IsOn = toggle.IsOn();
                    break;
                case TrayMenuCommand command when item is MenuFlyoutItem menuItem:
                    menuItem.Text = command.Label();
                    break;
            }
        }
    }

    // Narrow density (32 DIP items, the Windows 11 mouse target) from the
    // first open. The framework starts items at DefaultPadding (40 DIP) and
    // only switches to NarrowPadding after a pointer has interacted with the
    // menu (PaddingSizeStates in the default item style), so the first menu
    // after launch would be taller than every later one. Setting each item's
    // Padding to the narrow value makes both states equal: a tray menu opens
    // from a mouse click, the touch density never applies.
    private void ApplyNarrowPadding()
    {
        if (!Application.Current.Resources.TryGetValue("MenuFlyoutItemThemePaddingNarrow", out var narrow)
            || narrow is not Thickness narrowPadding)
            return;

        foreach (var (_, item) in _items)
            if (item is MenuFlyoutItem menuItem)
                menuItem.Padding = narrowPadding;
    }
}
