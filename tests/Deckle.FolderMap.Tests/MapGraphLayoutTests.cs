using Xunit;

namespace Deckle.FolderMap.Tests;

// Where the large graph puts the items it draws: one column per kind, only
// what the filter keeps, the chosen item and its neighbours standing out, and
// past the rows a column can hold, the chosen item's neighbourhood and "+N".
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapGraphLayoutTests
{
    private const string Skill = ".claude/skills/publier-version/SKILL.md";

    private static MapInventory Sample(ScratchFolder scratch)
    {
        MapSampleRepository.Write(scratch);
        return MapInventory.Build(scratch.Path, TestContext.Current.CancellationToken);
    }

    private static MapItem Item(MapInventory map, string id) =>
        map.Find(id) ?? throw new Xunit.Sdk.XunitException($"no item {id}");

    [Fact]
    public void WhenEveryColumnFitsEachItemIsDrawnOnceInTheColumnOfItsKindByName()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, chosen: null, maxRows: 100);

        Assert.Equal(map.Items.Count, layout.Nodes.Count);
        Assert.Equal(map.Items.Select(i => i.Id).Order(), layout.Nodes.Select(n => n.Item.Id).Order());
        Assert.Equal(MapListOrder.Sections(map.Items).Select(s => s.Kind), layout.Columns.Select(c => c.Kind));
        Assert.All(layout.Nodes, n => Assert.Equal(layout.Columns[n.Column].Kind, n.Item.Kind));
        Assert.Equal(0, layout.Hidden);
        Assert.False(layout.IsNeighbourhood);
        foreach (MapSection section in MapListOrder.Sections(map.Items))
        {
            Assert.Equal(
                section.Items.Select(i => i.Id),
                layout.Nodes.Where(n => n.Item.Kind == section.Kind).OrderBy(n => n.Row).Select(n => n.Item.Id));
        }
    }

    [Fact]
    public void WithNothingChosenNothingIsDimmed()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, chosen: null, maxRows: 100);

        Assert.All(layout.Nodes, n => Assert.True(n.StandsOut));
        Assert.All(layout.Nodes, n => Assert.False(n.IsChosen));
        Assert.All(layout.Edges, e => Assert.True(e.StandsOut));
    }

    [Fact]
    public void TheChosenItemAndItsNeighboursStandOutAndTheRestIsDimmed()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        MapItem skill = Item(map, Skill);
        var neighbours = map.Neighbours(skill).Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, skill, maxRows: 100);

        Assert.Single(layout.Nodes, n => n.IsChosen);
        Assert.Equal(Skill, layout.Nodes.Single(n => n.IsChosen).Item.Id);
        Assert.All(layout.Nodes, n => Assert.Equal(n.IsChosen || neighbours.Contains(n.Item.Id), n.StandsOut));
        Assert.Contains(layout.Nodes, n => !n.StandsOut);
        Assert.All(layout.Edges, e => Assert.Equal(e.From.Id == Skill || e.To.Id == Skill, e.StandsOut));
    }

    [Fact]
    public void ALinkBothWaysIsOneEdge()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, chosen: null, maxRows: 100);

        MapGraphEdge edge = Assert.Single(layout.Edges, e =>
            (e.From.Id, e.To.Id) is ("CLAUDE.md", Skill) or (Skill, "CLAUDE.md"));
        Assert.True(edge.BothWays);
        Assert.Equal(layout.Edges.Count, layout.Edges.Select(e => string.Join('|', new[] { e.From.Id, e.To.Id }.Order())).Distinct().Count());
    }

    [Fact]
    public void AnItemTheFilterHidesIsNotDrawnNorAreItsLinks()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { HiddenKinds = new HashSet<MapItemKind> { MapItemKind.Script } };

        MapGraphLayout layout = MapGraphLayout.Of(map, filter, chosen: null, maxRows: 100);

        Assert.Contains(map.Items, i => i.Kind == MapItemKind.Script);
        Assert.DoesNotContain(layout.Nodes, n => n.Item.Kind == MapItemKind.Script);
        Assert.DoesNotContain(layout.Columns, c => c.Kind == MapItemKind.Script);
        Assert.DoesNotContain(layout.Edges, e => e.From.Kind == MapItemKind.Script || e.To.Kind == MapItemKind.Script);
        Assert.Equal(0, layout.Hidden);
    }

    [Fact]
    public void AChosenItemTheFilterHidesLeavesNothingChosen()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        var filter = MapListFilter.All with { HiddenKinds = new HashSet<MapItemKind> { MapItemKind.Skill } };

        MapGraphLayout layout = MapGraphLayout.Of(map, filter, Item(map, Skill), maxRows: 100);

        Assert.DoesNotContain(layout.Nodes, n => n.IsChosen);
        Assert.All(layout.Nodes, n => Assert.True(n.StandsOut));
    }

    [Fact]
    public void PastTheRowsAColumnHoldsOnlyTheChosenItemsNeighbourhoodIsDrawnAndTheRestCounted()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        MapItem skill = Item(map, Skill);
        var near = map.Neighbours(skill).Select(n => n.Id).Append(Skill).ToHashSet(StringComparer.OrdinalIgnoreCase);
        const int maxRows = 3;
        Assert.Contains(MapListOrder.Sections(map.Items), s => s.Count > maxRows);

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, skill, maxRows);

        Assert.True(layout.IsNeighbourhood);
        Assert.Contains(layout.Nodes, n => n.IsChosen);
        Assert.All(layout.Nodes, n => Assert.Contains(n.Item.Id, near));
        Assert.All(layout.Nodes, n => Assert.True(n.StandsOut));
        Assert.True(layout.RowCount <= maxRows);
        Assert.Equal(map.Items.Count, layout.Nodes.Count + layout.Hidden);
        Assert.All(layout.Columns, c => Assert.Equal(map.Items.Count(i => i.Kind == c.Kind), c.Drawn + c.Hidden));
    }

    [Fact]
    public void PastTheRowsAColumnHoldsWithNothingChosenTheFirstItemsByNameAreDrawn()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        const int maxRows = 3;

        MapGraphLayout layout = MapGraphLayout.Of(map, MapListFilter.All, chosen: null, maxRows);

        Assert.False(layout.IsNeighbourhood);
        Assert.True(layout.RowCount <= maxRows);
        Assert.True(layout.Hidden > 0);
        foreach (MapSection section in MapListOrder.Sections(map.Items))
        {
            int room = section.Count <= maxRows ? section.Count : maxRows - 1;
            Assert.Equal(
                section.Items.Take(room).Select(i => i.Id),
                layout.Nodes.Where(n => n.Item.Kind == section.Kind).OrderBy(n => n.Row).Select(n => n.Item.Id));
        }
    }

    [Fact]
    public void TheSameInventoryFilterAndChoiceGiveTheSameLayout()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapGraphLayout first = MapGraphLayout.Of(map, MapListFilter.All, Item(map, Skill), maxRows: 3);
        MapGraphLayout second = MapGraphLayout.Of(map, MapListFilter.All, Item(map, Skill), maxRows: 3);

        Assert.Equal(first.Columns, second.Columns);
        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.Edges, second.Edges);
    }
}
