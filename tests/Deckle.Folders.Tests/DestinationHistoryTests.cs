using Xunit;
using Deckle.Core;

namespace Deckle.Folders.Tests;

// Home and folders in one history: Back and Forward cross both, and removing
// an added folder leaves Home where it was.
[Trait("Category", "unit")]
public sealed class DestinationHistoryTests
{
    private static readonly AddedFolder Photos = new(@"C:\Code\Photos", DateTimeOffset.UnixEpoch);
    private static readonly AddedFolder Documents = new(@"C:\Code\Documents", DateTimeOffset.UnixEpoch);

    private static FolderDestination At(AddedFolder root) => new(FolderPlace.AtRoot(root));

    [Fact]
    public void TwoHomeDestinationsAreEqual()
    {
        Assert.Equal(new HomeDestination(), HomeDestination.Instance);
        Assert.NotEqual<Destination>(HomeDestination.Instance, At(Photos));
    }

    [Fact]
    public void BackFromAFolderOpenedFromHomeReturnsHome()
    {
        var history = new NavigationHistory<Destination>();
        history.Navigate(HomeDestination.Instance);
        history.Navigate(At(Photos));

        Assert.Equal(HomeDestination.Instance, history.GoBack());
        Assert.True(history.CanGoForward);
        Assert.Equal(At(Photos), history.GoForward());
    }

    [Fact]
    public void ForgettingAFoldersDestinationsLeavesHomeBehindIt()
    {
        var history = new NavigationHistory<Destination>();
        history.Navigate(HomeDestination.Instance);
        history.Navigate(At(Photos));
        history.Navigate(new FolderDestination(new FolderPlace(Photos, @"C:\Code\Photos\2026")));
        history.Navigate(At(Documents));

        bool standingInIt = history.Forget(d => d is FolderDestination f
            && FolderPaths.Comparer.Equals(f.Place.Root.Path, Photos.Path));

        Assert.False(standingInIt);
        Assert.Equal(At(Documents), history.Current);
        Assert.Equal(HomeDestination.Instance, history.GoBack());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void ForgettingTheFolderTheWindowStandsInLeavesHomeBack()
    {
        var history = new NavigationHistory<Destination>();
        history.Navigate(HomeDestination.Instance);
        history.Navigate(At(Photos));

        Assert.True(history.Forget(d => d is FolderDestination));

        Assert.Null(history.Current);
        Assert.True(history.CanGoBack);
    }
}
