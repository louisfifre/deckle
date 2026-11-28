using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.FolderMap.Tests;

// The map writes under its own provider, Deckle-FolderMap: a folder it cannot
// read is one Warning with the reason, and no event carries a path.
[Collection(FolderMapProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class MapEventsTests
{
    [Fact]
    public void AMissingFolderIsOneWarningWithItsReason()
    {
        using var scratch = new ScratchFolder();
        string missing = System.IO.Path.Combine(scratch.Path, "absent");
        using var listener = new TestEventListener("Deckle-FolderMap");

        MapInventory.Build(missing, TestContext.Current.CancellationToken);

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.Level == EventLevel.Warning);
        Assert.Equal(DeckleFolderMapSource.EvtMapInventoryFailed, warning.EventId);
        Assert.Equal("NotFound", warning.Payload?[0]);
        Assert.DoesNotContain(listener.Events.SelectMany(e => e.Payload ?? []),
            value => value is string text && text.Contains(scratch.Path, StringComparison.OrdinalIgnoreCase));
    }
}
