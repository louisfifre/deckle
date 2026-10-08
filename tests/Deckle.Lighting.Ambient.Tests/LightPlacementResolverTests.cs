using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class LightPlacementResolverTests
{
    private static HueLightPlacement Placement(string id, double x = 0, double z = 0)
        => new(id, $"Light {id}", x, 1, z);

    private static HueEntertainmentArea Area(string id, string name, params HueLightPlacement[] placements)
        => new(id, name, placements, []);

    [Fact]
    public void AnAreaNamedLikeTheGroupWinsOverABetterOverlap()
    {
        var named   = Area("a", "Living room", Placement("9"));
        var overlap = Area("b", "TV", Placement("1"), Placement("2"));

        var match = LightPlacementResolver.MatchEntertainmentArea(
            "living ROOM", ["1", "2"], [overlap, named]);

        Assert.NotNull(match);
        Assert.Same(named, match.Area);
        Assert.Equal(EntertainmentAreaMatchKind.Name, match.Kind);
    }

    [Fact]
    public void WithoutANameMatchTheAreaSharingTheMostLightsWins()
    {
        var one = Area("a", "Desk", Placement("1"));
        var two = Area("b", "TV", Placement("1"), Placement("2"), Placement("7"));

        var match = LightPlacementResolver.MatchEntertainmentArea(
            "Living room", ["1", "2", "3"], [one, two]);

        Assert.NotNull(match);
        Assert.Same(two, match.Area);
        Assert.Equal(EntertainmentAreaMatchKind.Overlap, match.Kind);
        Assert.Equal(2, match.Overlap);
    }

    [Fact]
    public void NoNameAndNoSharedLightMeansNoMatch()
    {
        var area = Area("a", "Desk", Placement("8"));

        Assert.Null(LightPlacementResolver.MatchEntertainmentArea("Living room", ["1"], [area]));
        Assert.Null(LightPlacementResolver.MatchEntertainmentArea("Living room", [], [area]));
        Assert.Null(LightPlacementResolver.MatchEntertainmentArea(null, ["1"], []));
    }

    [Fact]
    public void SuggestionsCoverOnlyTheLightsOfTheGroup()
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
