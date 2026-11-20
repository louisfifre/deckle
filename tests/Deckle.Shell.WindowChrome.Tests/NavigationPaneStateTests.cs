using Deckle.Shell.WindowChrome;
using Xunit;

namespace Deckle.Shell.WindowChrome.Tests;

// Behaviour of NavigationPaneState against specs/deckle-navigation-pane.md: in a
// window wide enough for the rail, the button changes the user's choice and the
// choice stays; in a narrow window, the navigation opens over the page and never
// touches that choice.
[Trait("Category", "unit")]
public class NavigationPaneStateTests
{
    [Fact]
    public void A_wide_window_opens_as_the_first_choice_says()
    {
        Assert.True(new NavigationPaneState(openByChoice: true, docked: true).PaneOpen);
        Assert.False(new NavigationPaneState(openByChoice: false, docked: true).PaneOpen);
    }

    [Fact]
    public void The_button_in_a_wide_window_changes_the_choice()
    {
        var state = new NavigationPaneState(openByChoice: true, docked: true);

        state.Toggle();

        Assert.False(state.PaneOpen);
        Assert.False(state.OpenByChoice);
    }

    [Fact]
    public void A_narrow_window_hides_the_navigation_whatever_the_choice()
    {
        var state = new NavigationPaneState(openByChoice: true, docked: true);

        Assert.True(state.SetDocked(false));

        Assert.False(state.PaneOpen);
        Assert.True(state.OpenByChoice);
    }

    [Fact]
    public void The_button_in_a_narrow_window_opens_over_the_page_without_changing_the_choice()
    {
        var state = new NavigationPaneState(openByChoice: false, docked: false);

        state.Toggle();

        Assert.True(state.PaneOpen);
        Assert.True(state.OpenOverPage);
        Assert.False(state.OpenByChoice);
    }

    [Fact]
    public void Widening_again_brings_back_the_choice_and_drops_the_pane_over_the_page()
    {
        var state = new NavigationPaneState(openByChoice: false, docked: false);
        state.Toggle();

        state.SetDocked(true);

        Assert.False(state.PaneOpen);
        Assert.False(state.OpenOverPage);
    }

    [Fact]
    public void Narrowing_then_widening_keeps_an_open_choice()
    {
        var state = new NavigationPaneState(openByChoice: true, docked: true);

        state.SetDocked(false);
        state.SetDocked(true);

        Assert.True(state.PaneOpen);
    }

    [Fact]
    public void A_resize_that_stays_in_the_same_band_changes_nothing()
    {
        var state = new NavigationPaneState(openByChoice: false, docked: true);

        Assert.False(state.SetDocked(true));
        Assert.False(state.PaneOpen);
    }

    [Fact]
    public void The_control_closing_its_pane_over_the_page_follows_into_the_state()
    {
        var state = new NavigationPaneState(openByChoice: true, docked: false);
        state.Toggle();

        state.ClosedOverPage();

        Assert.False(state.PaneOpen);
        Assert.True(state.OpenByChoice);
    }

    [Fact]
    public void A_docked_pane_closing_does_not_change_the_choice()
    {
        var state = new NavigationPaneState(openByChoice: true, docked: true);

        state.ClosedOverPage();

        Assert.True(state.PaneOpen);
    }
}
