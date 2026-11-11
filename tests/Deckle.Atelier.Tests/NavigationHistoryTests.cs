using Xunit;

namespace Deckle.Atelier.Tests;

// Back and Forward behave as in File Explorer.
[Trait("Category", "unit")]
public sealed class NavigationHistoryTests
{
    private sealed record Place(string Name);

    [Fact]
    public void BackReturnsToThePreviousPlaceAndForwardComesBack()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("b"));

        Assert.Equal(new Place("a"), history.GoBack());
        Assert.True(history.CanGoForward);
        Assert.Equal(new Place("b"), history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void GoingSomewhereNewForgetsTheForwardPlaces()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("b"));
        history.GoBack();

        history.Navigate(new("c"));

        Assert.False(history.CanGoForward);
        Assert.Equal(new Place("a"), history.GoBack());
    }

    [Fact]
    public void GoingToThePlaceAlreadyShownLeavesTheHistoryAlone()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));

        Assert.False(history.Navigate(new("a")));
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void BackWithNothingBehindReturnsNull()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));

        Assert.Null(history.GoBack());
        Assert.Equal(new Place("a"), history.Current);
    }
}
