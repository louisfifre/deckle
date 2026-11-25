using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The Lights card lists the lights of the selected Entertainment area, the
// lights the stream drives, each with the screen edge its placement in the
// Hue app suggests. Finding the area itself is the output factory's, tested
// in Deckle.Lighting.Tests (HueLightOutputFactoryTests).
[Trait("Category", "unit")]
public sealed class LightPlacementResolverTests
{
    private static HueLightPlacement Placement(string id, double x = 0, double z = 0)
        => new(id, $"Light {id}", x, 1, z);

    private static HueEntertainmentArea Area(string id, string name, params HueLightPlacement[] placements)
        => new(id, name, placements, []);

    [Fact]
    public void TheAreaListsItsLightsInItsOwnOrderOnce()
    {
        var area = Area("a", "TV", Placement("3"), Placement("1"), Placement("3"));

        var (lights, _) = LightPlacementResolver.Build(area, new Dictionary<string, bool>());

        Assert.Equal(["3", "1"], lights.Select(l => l.Id));
        Assert.Equal("Light 3", lights[0].Name);
    }

    [Fact]
    public void ALightTheBridgeReportsUnreachableIsOffline()
    {
        var area = Area("a", "TV", Placement("1"), Placement("2"));

        var (lights, _) = LightPlacementResolver.Build(
            area, new Dictionary<string, bool> { ["1"] = false });

        Assert.False(lights[0].IsReachable);
        Assert.True(lights[1].IsReachable);
    }

    [Fact]
    public void EveryPlacedLightGetsTheZoneItsPositionSuggests()
    {
        var area = Area("a", "TV",
            Placement("1", x: -0.8),
            Placement("2", z: 0.9),
            Placement("3", x: 0.8));

        var (_, suggestions) = LightPlacementResolver.Build(area, new Dictionary<string, bool>());

        Assert.Equal(LightZone.Left, suggestions["1"]);
        Assert.Equal(LightZone.Top, suggestions["2"]);
        Assert.Equal(LightZone.Right, suggestions["3"]);
    }

    [Fact]
    public void SuggestionsCoverOnlyTheLightsAsked()
    {
        var area = Area("a", "TV",
            Placement("1", x: -0.8),
            Placement("2", z: 0.9),
            Placement("3", x: 0.8));

        var suggestions = LightPlacementResolver.SuggestZones(area, ["1", "2"]);

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(LightZoneSuggester.Suggest(area.LightPlacements[0]), suggestions["1"]);
        Assert.Equal(LightZoneSuggester.Suggest(area.LightPlacements[1]), suggestions["2"]);
        Assert.False(suggestions.ContainsKey("3"));
    }
}
