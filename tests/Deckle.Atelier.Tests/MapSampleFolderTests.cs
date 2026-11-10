using Xunit;

namespace Deckle.Atelier.Tests;

// The project the self-check photographs the map on holds what its images
// need: every kind, items always read, and a skill with more neighbours than
// the panel's graph draws.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapSampleFolderTests
{
    [Fact]
    public void TheSampleProjectShowsEveryKindLinksBothWaysAndAPlusN()
    {
        using var scratch = new ScratchFolder();
        string carnet = MapSampleFolder.Create(scratch.Path);

        MapInventory map = MapInventory.Build(carnet, TestContext.Current.CancellationToken);

        Assert.Equal(MapInventory.KindsLookedFor.ToHashSet(), map.Items.Select(i => i.Kind).ToHashSet());
        Assert.Contains(map.Items, i => i.Id == "CLAUDE.md" && i.IsAlwaysRead);
        MapItem skill = map.Find(".claude/skills/publier-version/SKILL.md")!;
        MapNeighbourhood around = MapNeighbourhood.Around(map, skill);
        Assert.True(around.Hidden > 0, $"{around.Total} neighbours");
        Assert.Contains(around.Shown, n => n.Item.Id == "CLAUDE.md" && n.Direction == MapLinkDirection.Both);
    }

    [Fact]
    public void ASecondRunReplacesTheFirst()
    {
        using var scratch = new ScratchFolder();
        string first = MapSampleFolder.Create(scratch.Path);
        File.WriteAllText(Path.Combine(first, "left-over.ps1"), "x");

        string second = MapSampleFolder.Create(scratch.Path);

        Assert.Equal(first, second);
        Assert.False(File.Exists(Path.Combine(second, "left-over.ps1")));
    }
}
