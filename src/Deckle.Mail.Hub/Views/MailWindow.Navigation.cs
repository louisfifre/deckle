using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Deckle.Mail.Hub;

// ── Moving between pages ──────────────────────────────────────────────────────
//
// Every page change goes through Show: the history is pushed (MailHistory),
// the frame navigates, the rail selection follows, and the back button's
// state is set. The frame's own back stack is cleared after each navigation:
// MailHistory is the one record of where back leads.
//
// Pages are cached (NavigationCacheMode.Required), so going back to a list
// finds it as it was left, its scroll and its treated rows included.
public sealed partial class MailWindow
{
    private readonly MailHistory _history = new();

    // True while the self-check walks the pages: the entrance transition would
    // still be sliding when the image is taken.
    private bool _suppressTransitions;

    public MailDestination? CurrentDestination => _history.Current;

    public FrameworkElement? CurrentPage => PageFrame.Content as FrameworkElement;

    public void Go(MailDestination destination)
    {
        if (_history.Current == destination) return;
        _history.Go(destination);
        Show(destination);
    }

    public void GoBack()
    {
        MailDestination? back = _history.Back(StillExists);
        if (back is not null) Show(back);
    }

    private void ShowFirstPage()
    {
        _history.Reset(MailDestination.Triage);
        Show(MailDestination.Triage);
    }

    private void Show(MailDestination destination)
    {
        Type page = destination.Page switch
        {
            MailPage.Triage or MailPage.Mailbox or MailPage.Alias => typeof(TriagePage),
            MailPage.Subject => typeof(SubjectPage),
            MailPage.Activity => typeof(ActivityPage),
            MailPage.Settings => typeof(MailSettingsPage),
            MailPage.MailboxSettings => typeof(MailboxSettingsPage),
            _ => typeof(AddMailboxPage),
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            NavigationTransitionInfo transition = ComponentMotion.PageTransition(suppress: _suppressTransitions);
            PageFrame.Navigate(page, new MailPageArgs(this, destination), transition);
            PageFrame.BackStack.Clear();
            DeckleMailHubSource.Log.PageShown(destination.Page.ToString(), clock.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            DeckleMailHubSource.Log.NavigationFailed(destination.Page.ToString(), $"{ex.GetType().Name}: {ex.Message}");
        }

        SyncRailSelection(destination);
        AppTitleBar.IsBackButtonEnabled = _history.CanGoBack;
    }

    // A page whose mailbox or subject is gone (removed in Settings) is skipped
    // on the way back.
    private bool StillExists(MailDestination d) => d.Page switch
    {
        MailPage.Mailbox or MailPage.Alias or MailPage.MailboxSettings => Session.FindMailbox(d.MailboxId) is not null,
        MailPage.Subject => Session.FindSubject(d.SubjectId) is not null,
        _ => true,
    };

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_syncingSelection) return;
        if (args.InvokedItemContainer?.Tag is RailEntry { Destination: { } destination })
            Go(destination);
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args) => GoBack();

    // ── Back by keyboard and mouse, Ctrl+Z ───────────────────────────────────
    //
    // Alt+Left and the mouse back button go back, as in every Windows app that
    // has a back button. Ctrl+Z undoes the last gesture of Triage, except
    // while a text field holds the focus, which has its own undo.
    private void InstallBackInputs()
    {
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, e) =>
        {
            GoBack();
            e.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(back);

        var undo = new KeyboardAccelerator { Key = VirtualKey.Z, Modifiers = VirtualKeyModifiers.Control };
        undo.Invoked += (_, e) =>
        {
            if (FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox or PasswordBox) return;
            e.Handled = Session.UndoLast();
        };
        RootGrid.KeyboardAccelerators.Add(undo);
        // The accelerators answer without a tooltip of their own: the back
        // button's tooltip is the system's.
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // handledEventsToo: a click on a button inside the page has already
        // handled the press; the back button of the mouse must still go back.
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CloseSearchOverlayOnPressOutside(e);
        PointerPointProperties props = e.GetCurrentPoint(RootGrid).Properties;
        if (!props.IsXButton1Pressed) return;
        GoBack();
        e.Handled = true;
    }
}
