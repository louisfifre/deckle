using Xunit;

namespace Deckle.Atelier.Tests;

// The neighbours the map panel's small graph draws, in a fixed order, and
// the "+N" that counts the rest.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapNeighbourhoodTests
{
    private static MapInventory Sample(ScratchFolder scratch)
    {
        MapSampleRepository.Write(scratch);
        return MapInventory.Build(scratch.Path, TestContext.Current.CancellationToken);
    }

    private static MapItem Item(MapInventory map, string id) =>
        map.Find(id) ?? throw new Xunit.Sdk.XunitException($"no item {id}");

    [Fact]
    public void TheGraphShowsLinksBothWaysFirstThenWhatTheItemCitesByName()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapNeighbourhood around = MapNeighbourhood.Around(map, Item(map, ".claude/skills/publier-version/SKILL.md"));

        // Five neighbours, capture.ps1 cited from the skill's references/steps.md:
        // four drawn, the fifth counted, as board 12.8a draws "+1".
        Assert.Equal(
            ["CLAUDE.md:Both", "build.ps1:Out", "capture.ps1:Out", "notes-locales:Out"],
            around.Shown.Select(n => $"{n.Item.Name}:{n.Direction}"));
        Assert.Equal(1, around.Hidden);
    }

    [Fact]
    public void BeyondTheShownNeighboursTheRestIsCounted()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapNeighbourhood around = MapNeighbourhood.Around(map, Item(map, ".claude/skills/publier-version/SKILL.md"), maxShown: 2);

        Assert.Equal(["CLAUDE.md", "build.ps1"], around.Shown.Select(n => n.Item.Name));
        Assert.Equal(3, around.Hidden);
        Assert.Equal(5, around.Total);
    }

    [Fact]
    public void AnItemThatOnlyIsCitedReadsAsIncoming()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapNeighbourhood around = MapNeighbourhood.Around(map, Item(map, "CONTRIBUTING.md"));

        Assert.Contains(around.Shown, n => n.Item.Id == "README.md" && n.Direction == MapLinkDirection.In);
    }
}
