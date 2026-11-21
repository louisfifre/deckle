using Xunit;

namespace Deckle.Core.Tests;

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
    public void ForgettingAPlaceBehindAndAheadKeepsBackAndForwardOutOfIt()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("gone"));
        history.Navigate(new("b"));
        history.Navigate(new("gone"));
        history.Navigate(new("c"));
        history.GoBack();
        history.GoBack();

        bool currentGone = history.Forget(p => p.Name == "gone");

        Assert.False(currentGone);
        Assert.Equal(new Place("b"), history.Current);
        Assert.Equal(new Place("a"), history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Equal(new Place("b"), history.GoForward());
        Assert.Equal(new Place("c"), history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void ForgettingTheCurrentPlaceLeavesNoneAndKeepsWhatWasBehind()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("b"));
        history.Navigate(new("gone"));

        Assert.True(history.Forget(p => p.Name == "gone"));
        Assert.Null(history.Current);

        history.Navigate(new("c"));
        Assert.Equal(new Place("b"), history.GoBack());
        Assert.Equal(new Place("a"), history.GoBack());
    }

    [Fact]
    public void PlacesLeftSideBySideThatAreEqualBecomeOne()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("gone"));
        history.Navigate(new("a"));
        history.Navigate(new("gone"));

        history.Forget(p => p.Name == "gone");
        history.Navigate(new("a"));

        Assert.Equal(new Place("a"), history.Current);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void ForgettingEveryPlaceEmptiesTheHistory()
    {
        var history = new NavigationHistory<Place>();
        history.Navigate(new("a"));
        history.Navigate(new("b"));
        history.GoBack();

        history.Forget(_ => true);

        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
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
