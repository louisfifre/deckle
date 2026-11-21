using Xunit;
using Deckle.Core;

namespace Deckle.FolderMap.Tests;

// The map's two displays of one inventory: flat, and by sections.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapListOrderTests
{
    private static MapInventory Sample(ScratchFolder scratch)
    {
        MapSampleRepository.Write(scratch);
        return MapInventory.Build(scratch.Path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void SectionsHoldEveryShownItemOnceUnderItsKindInTheOrderLookedFor()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        IReadOnlyList<MapSection> sections = MapListOrder.Sections(map.Items);

        Assert.True(sections.Count > 1);
        Assert.All(sections, s => Assert.All(s.Items, i => Assert.Equal(s.Kind, i.Kind)));
        Assert.All(sections, s => Assert.Equal(map.Items.Count(i => i.Kind == s.Kind), s.Count));
        Assert.Equal(map.Items.Count, sections.Sum(s => s.Count));
        List<MapItemKind> expected = [.. MapInventory.KindsLookedFor.Where(k => map.Items.Any(i => i.Kind == k))];
        Assert.Equal(expected, sections.Select(s => s.Kind));
    }

    [Fact]
    public void AKindTheFilterEmptiesHasNoSection()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { HiddenKinds = new HashSet<MapItemKind> { MapItemKind.Script } };

        IReadOnlyList<MapSection> sections = MapListOrder.Sections(filter.Apply(map.Items));

        Assert.Contains(map.Items, i => i.Kind == MapItemKind.Script);
        Assert.DoesNotContain(sections, s => s.Kind == MapItemKind.Script);
        Assert.All(sections, s => Assert.NotEmpty(s.Items));
    }

    [Fact]
    public void AFilterThatKeepsNothingLeavesNoSectionAndNoRow()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { HiddenKinds = MapInventory.KindsLookedFor.ToHashSet() };

        IReadOnlyList<MapItem> shown = filter.Apply(map.Items);

        Assert.Empty(MapListOrder.Sections(shown));
        Assert.Empty(MapListOrder.Flat(shown));
    }

    [Fact]
    public void BothDisplaysSortByNameAndShowTheSameRows()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        IReadOnlyList<MapItem> flat = MapListOrder.Flat(map.Items);
        IReadOnlyList<MapSection> sections = MapListOrder.Sections(map.Items);

        Assert.Equal(map.Items.Count, flat.Count);
        Assert.Equal(flat.ToHashSet(), sections.SelectMany(s => s.Items).ToHashSet());
        Assert.All(sections, s => Assert.Equal(s.Items.OrderBy(i => i.Name, FolderNameOrder.Comparer), s.Items));
        Assert.Equal(flat.OrderBy(i => i.Name, FolderNameOrder.Comparer), flat);
    }
}
