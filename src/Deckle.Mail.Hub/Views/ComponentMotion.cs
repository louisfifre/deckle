using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
// The preference is read when a window, a page or a menu is built: a change
// made while the Mail window is open applies to what is built after it, and to
// the whole window the next time it opens.
internal static class ComponentMotion
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

    // Not covered: the pane of the NavigationView still glides open and
    // closed. Its motion lives in the visual transitions of the SplitView in
    // the control's template, and the control exposes no switch for it. Two
    // ways around were tried on 11 November 2026 and both broke the window:
    // removing the transitions left the pane folded while the page kept the
    // room of an open one (the open width is set by those storyboards), and
    // speeding their storyboards up crashed the window as it opened. What is
    // left is a template of our own for the pane, a larger piece of work.
}
