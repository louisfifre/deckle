using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Folders.Tests;

// What a click on the search button offers, behind a probe the test answers
// for: Command Palette present, absent, or a package list that cannot be
// read.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class SearchRoutingTests
{
    private sealed class FixedProbe(Func<bool> answer) : ICommandPaletteProbe
    {
        public bool IsInstalled() => answer();
    }

    [Fact]
    public void CommandPaletteInstalledLeadsToIt()
    {
        using var listener = new TestEventListener("Deckle-Folders");

        SearchRoute route = SearchRouting.Choose(new FixedProbe(() => true));

        Assert.Equal(SearchRoute.CommandPalette, route);
        EventWrittenEventArgs ev = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtSearchRouted);
        Assert.Equal(["command-palette"], ev.Payload);
    }

    [Fact]
    public void CommandPaletteAbsentOffersPowerToys()
    {
        using var listener = new TestEventListener("Deckle-Folders");

        SearchRoute route = SearchRouting.Choose(new FixedProbe(() => false));

        Assert.Equal(SearchRoute.InstallPowerToys, route);
        EventWrittenEventArgs ev = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtSearchRouted);
        Assert.Equal(["install-powertoys"], ev.Payload);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleFoldersSource.EvtCommandPaletteProbeFailed);
    }

    [Fact]
    public void AProbeThatFailsOffersPowerToysAndSaysWhy()
    {
        using var listener = new TestEventListener("Deckle-Folders");

        SearchRoute route = SearchRouting.Choose(new FixedProbe(() => throw new UnauthorizedAccessException()));

        Assert.Equal(SearchRoute.InstallPowerToys, route);
        EventWrittenEventArgs failed = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtCommandPaletteProbeFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal(["UnauthorizedAccessException"], failed.Payload);
    }

    // The Store's product-page link, built from PowerToys' Store id; nothing
    // here opens it.
    [Fact]
    public void TheStoreLinkIsPowerToysProductPage()
    {
        Assert.Equal("ms-windows-store", PowerToysInstallPage.StoreUri.Scheme);
        Assert.Equal("pdp", PowerToysInstallPage.StoreUri.Host);
        Assert.Equal("?ProductId=XP89DCGQ3K6VLD", PowerToysInstallPage.StoreUri.Query);
        Assert.Equal("learn.microsoft.com", PowerToysInstallPage.WebUri.Host);
    }
}
