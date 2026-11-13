using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── Subpages: one level under a rail page ────────────────────────────────────
//
// A subpage is a destination the rail does not list, opened from a card of the
// page on screen: one rewrite profile under Rewriting, as Windows 11 Settings
// opens one device under Bluetooth & devices. The rail keeps the page's item
// selected, the header shows "Rewriting › Affinage", and the way back is the
// title bar's back button, Alt+Left, the mouse's back button, the parent
// segment of the header, or the rail item itself.
//
// The back button shows only while a subpage is on screen. It does not follow
// Frame.CanGoBack: Frame.Navigate stacks every rail switch too, so a back
// button bound to the stack would offer to retrace ordinary page switches.
// Going back returns to the rail's page, not to whatever came before.
public sealed partial class SettingsWindow
{
    private void InitializeSubpages()
    {
        AppTitleBar.BackRequested += (_, _) => ReturnToRailPage();
        PageFrame.Navigated += OnPageFrameNavigated;
        Nav.ItemInvoked += OnNavItemInvoked;

        // Alt+Left, the shortcut every Windows 11 window with a back button
        // answers. The accelerator does nothing, and lets the key through, on
        // a rail page.
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, args) => args.Handled = ReturnToRailPage();
        RootGrid.KeyboardAccelerators.Add(back);
        // Hidden: the root would otherwise show a tooltip naming the shortcut
        // under the window whenever keyboard focus lands inside it.
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // The mouse's back button (XButton1), handled even when a card under
        // the pointer marked the press as handled.
        RootGrid.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnRootMouseBackPressed), handledEventsToo: true);
    }

    // The page type of the rail's selected item, the page a subpage opens from.
    private Type? RailPageType =>
        (Nav.SelectedItem as NavigationViewItem)?.Tag is string tag ? Type.GetType(tag) : null;

    private bool IsOnSubpage =>
        PageFrame.Content is not null && PageFrame.CurrentSourcePageType != RailPageType;

    public void OpenSubpage(Type pageType, object? parameter)
    {
        DeckleSettingsSource.Log.NavStarted();
        DeckleSettingsSource.Log.NavStartedDetail(pageType.Name);
        DeckleSettingsSource.NavClock.Restart();
        try
        {
            // DrillIn is the transition WinUI gives a move into a detail page;
            // GoBack plays it in reverse on the way out. None during the
            // capture walk, which photographs the page once it has settled.
            NavigationTransitionInfo transition = _suppressPageTransition
                ? new SuppressNavigationTransitionInfo()
                : new DrillInNavigationTransitionInfo();
            if (PageFrame.Navigate(pageType, parameter, transition))
            {
                DeckleSettingsSource.Log.NavCompleted();
                DeckleSettingsSource.Log.NavCompletedDetail(
                    pageType.Name, DeckleSettingsSource.NavClock.ElapsedMilliseconds);
            }
            else
            {
                DeckleSettingsSource.Log.NavFailedFrameRejected();
                DeckleSettingsSource.Log.NavFailedFrameRejectedDetail(pageType.Name);
            }
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.NavFailedThrew(pageType.Name, ex.GetType().Name, ex.Message);
            DeckleSettingsSource.Log.NavStackTrace(ex.StackTrace ?? "(no stack)");
        }
    }

    // Back to the page the subpage opened from. True when there was a subpage
    // to leave, so a key or a click that found none passes through.
    private bool ReturnToRailPage()
    {
        if (!IsOnSubpage || RailPageType is not Type railPage) return false;

        // The usual case: the rail page is right under the subpage in the
        // stack, and GoBack restores its cached instance with the reverse
        // transition. Otherwise (a stack the walk or a search reshaped) a plain
        // navigation lands on the same page.
        IList<PageStackEntry> stack = PageFrame.BackStack;
        if (stack.Count > 0 && stack[^1].SourcePageType == railPage)
            PageFrame.GoBack();
        else
            PageFrame.Navigate(railPage, null, new EntranceNavigationTransitionInfo());
        return true;
    }

    private void OnPageFrameNavigated(object sender, NavigationEventArgs e)
    {
        DeckleSettingsSource.Log.PageOpened(e.SourcePageType?.Name ?? "unknown");
        bool onSubpage = IsOnSubpage;
        AppTitleBar.IsBackButtonVisible = onSubpage;
        AppTitleBar.IsBackButtonEnabled = onSubpage;
    }

    // A click on the selected rail item raises no SelectionChanged; on a
    // subpage it still means "this page", so it goes back up.
    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, Nav.SelectedItem))
            ReturnToRailPage();
    }

    private void OnRootMouseBackPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        if (e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed && ReturnToRailPage())
            e.Handled = true;
    }
}
