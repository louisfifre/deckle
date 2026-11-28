using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Deckle.Browser.Views;

// ─── Browser window: the usual shortcuts ─────────────────────────────────────
//
// The table is the domain's (Shortcuts: a gesture, a command); this file takes
// the keys and runs the command. XAML KeyboardAccelerators do not fire while
// the web content has the keyboard focus (found in step 2), so the window
// handles PreviewKeyDown on its root, which tunnels from whichever element has
// the focus, the web view included when the framework routes the page's keys
// to it. The page's own accelerator keys are off (WebViewSettings), so the
// engine does not take Ctrl+F or F5 for itself. No web message and no script
// injected into a page is involved.
public sealed partial class BrowserWindow
{
    private void WireKeys() => RootGrid.PreviewKeyDown += OnRootPreviewKeyDown;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu) return;

        var gesture = new KeyGesture(e.Key.ToString(), IsDown(VirtualKey.Control), IsDown(VirtualKey.Shift), IsDown(VirtualKey.Menu));
        if (Shortcuts.Find(gesture) is not { } command) return;
        e.Handled = true;
        RunShortcut(command);
    }

    /// <summary>Runs what a shortcut asks, as its keys do.</summary>
    public void RunShortcut(ShortcutCommand command)
    {
        switch (command)
        {
            case ShortcutCommand.NewTab: NewTab(); break;
            case ShortcutCommand.CloseTab: CloseShownTab(); break;
            case ShortcutCommand.ReopenClosedTab: ReopenClosedTab(); break;
            case ShortcutCommand.FocusAddress: FocusAddress(); break;
            case ShortcutCommand.Find: OpenFind(); break;
            case ShortcutCommand.Reload: ReloadShown(); break;
            case ShortcutCommand.Back: GoBack(); break;
            case ShortcutCommand.Forward: GoForward(); break;
            case ShortcutCommand.NextTab: CycleTab(+1); break;
            case ShortcutCommand.PreviousTab: CycleTab(-1); break;
            case ShortcutCommand.PrivateWindow: OpenPrivateWindow(); break;
        }
    }

    private void ReopenClosedTab()
    {
        using var gesture = new GestureScope(BrowserGesture.ReopenClosedTab);
        if (_closedTabs.Pop() is not { } closed)
        {
            gesture.Outcome = "refused";
            return;
        }
        PanelItem tab = AddTab(closed.Url);
        tab.Item.Title = closed.Title;
        tab.Title = closed.Title;
    }

    // Ctrl+L and Alt+D: the field takes the focus and selects the address. The
    // field is not there in the rail, so the panel opens first.
    private void FocusAddress()
    {
        using var gesture = new GestureScope(BrowserGesture.FocusAddress);
        if (AddressRow.Visibility != Visibility.Visible)
        {
            _pane.SetOpen(true);
            DispatcherQueue.TryEnqueue(() => AddressBox.Focus(FocusState.Programmatic));
            return;
        }
        AddressBox.Focus(FocusState.Programmatic);
    }

    // Ctrl+Tab and Ctrl+Shift+Tab walk the pinned sites then the tabs, as the
    // panel lists them, and wrap.
    private void CycleTab(int step)
    {
        using var gesture = new GestureScope(step > 0 ? BrowserGesture.NextTab : BrowserGesture.PreviousTab);
        List<PanelItem> order = [.. _pinnedItems, .. _tabItems];
        if (order.Count < 2 || _shown is null)
        {
            gesture.Outcome = "refused";
            return;
        }
        int at = order.IndexOf(_shown);
        SelectItem(order[((at + step) % order.Count + order.Count) % order.Count]);
    }
}
