using Xunit;

namespace Deckle.Atelier.Tests;

// The boxes above the map's list, and the total of the rows they keep.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapListFilterTests
{
    private static MapInventory Sample(ScratchFolder scratch)
    {
        MapSampleRepository.Write(scratch);
        return MapInventory.Build(scratch.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AlwaysReadKeepsOnlyWhatEverySessionLoadsAndTheTotalMatchesTheRows()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { AlwaysReadOnly = true };

        IReadOnlyList<MapItem> shown = filter.Apply(map.Items);

        Assert.NotEmpty(shown);
        Assert.All(shown, i => Assert.True(i.IsAlwaysRead));
        Assert.Equal(map.EstimatedAlwaysReadTokens, MapListFilter.EstimatedTokensOf(shown));
    }

    [Fact]
    public void AHiddenKindLeavesTheList()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { HiddenKinds = new HashSet<MapItemKind> { MapItemKind.Script } };

        IReadOnlyList<MapItem> shown = filter.Apply(map.Items);

        Assert.DoesNotContain(shown, i => i.Kind == MapItemKind.Script);
        Assert.Equal(map.Items.Count(i => i.Kind != MapItemKind.Script), shown.Count);
        Assert.Equal(map.Items.Count, MapListFilter.All.Apply(map.Items).Count);
    }
}
