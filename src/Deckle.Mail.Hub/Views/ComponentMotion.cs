using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Deckle.Mail.Hub;

// ── The motion of Windows' own controls ───────────────────────────────────────
//
// WinUI 3 keeps animating its controls when the person has turned "Animation
// effects" off in Windows: a page still slides in, a menu still unfolds, the
// navigation pane still glides open. Nothing in the framework reads the
// preference for them, so each place that would move asks here and stands
// still when the preference is off.
//
// Only Windows' preference is read. Deckle's own Animations setting governs
// the motion Deckle adds to signal something (the HUD); it never turns a
// Windows control's motion back on.
//
// The preference is read when a window, a page or a menu is built: a change
// made while the Mail window is open applies to what is built after it, and to
// the whole window the next time it opens.
internal static partial class ComponentMotion
{
    private static readonly UISettings Settings = new();

    public static bool IsEnabled
    {
        get
        {
            try { return Settings.AnimationsEnabled; }
            catch (Exception) { return true; }
        }
    }

    public static NavigationTransitionInfo PageTransition(bool suppress = false)
        => suppress || !IsEnabled ? new SuppressNavigationTransitionInfo() : new EntranceNavigationTransitionInfo();

    // A menu or a flyout appears whole, without its unfolding.
    public static T Still<T>(T flyout) where T : FlyoutBase
    {
        flyout.AreOpenCloseAnimationsEnabled = IsEnabled;
        return flyout;
    }

    // A row that joins or leaves a list does so at once, and its neighbours
    // take their place without sliding.
    public static void Still(ListViewBase list)
    {
        if (!IsEnabled) list.ItemContainerTransitions = new TransitionCollection();
    }

    // The pane of a NavigationView opens and folds at once. Its glide lives in
    // the visual transitions of the SplitView inside the control's template,
    // and the control exposes no switch for it. The SplitView asks its state
    // manager for each state "with transitions"; a state manager of our own,
    // set on the root of its template, answers the same request without them,
    // so every state lands whole and the template stays Windows' own.
    //
    // Two other ways were tried on 11 November 2026 and both broke the window:
    // removing the transitions from the template left the pane folded while
    // the page kept the room of an open one, and speeding their storyboards up
    // crashed the window as it opened.
    public static void Still(NavigationView navigation)
    {
        if (!IsEnabled) WhenLoaded(navigation, () => StillPaneOf(navigation));
    }

    private static void StillPaneOf(NavigationView navigation)
    {
        if (FindDescendant<SplitView>(navigation) is not { } pane) return;
        WhenLoaded(pane, () =>
        {
            if (VisualTreeHelper.GetChildrenCount(pane) > 0
                && VisualTreeHelper.GetChild(pane, 0) is FrameworkElement templateRoot)
                VisualStateManager.SetCustomVisualStateManager(templateRoot, new StatesWithoutTransitions());
        });
    }

    private static void WhenLoaded(FrameworkElement element, Action then)
    {
        if (element.IsLoaded)
        {
            then();
            return;
        }
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            element.Loaded -= OnLoaded;
            then();
        }
        element.Loaded += OnLoaded;
    }

    private static T? FindDescendant<T>(DependencyObject node) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private sealed partial class StatesWithoutTransitions : VisualStateManager
    {
        protected override bool GoToStateCore(Control control, FrameworkElement templateRoot, string stateName,
            VisualStateGroup group, VisualState state, bool useTransitions)
            => base.GoToStateCore(control, templateRoot, stateName, group, state, useTransitions: false);
    }
}
