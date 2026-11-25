using System.Text.Json;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// Ambient runs in one mode for the beta, a colour per screen edge, and each
// light starts on the edge its place in the Hue Entertainment area suggests
// (Louis, 2026-11-25, grill answer r10q4 B: "on peut les placer déjà"). The
// person's pick, No zone included, overrides the suggestion.
[Trait("Category", "unit")]
public sealed class LightZoneAssignmentTests
{
    private static readonly IReadOnlyDictionary<string, LightZone> Suggested = new Dictionary<string, LightZone>
    {
        ["1"] = LightZone.Left,
        ["2"] = LightZone.Top,
        ["3"] = LightZone.None,
    };

    [Fact]
    public void ALightNeverAssignedFollowsItsPlacement()
    {
        Assert.Equal(LightZone.Left, LightZoneAssignment.Resolve("1", new Dictionary<string, LightZone>(), Suggested));
        Assert.Equal(LightZone.Top, LightZoneAssignment.Resolve("2", null, Suggested));
    }

    [Fact]
    public void APickOverridesThePlacement()
    {
        var stored = new Dictionary<string, LightZone> { ["1"] = LightZone.Right };

        Assert.Equal(LightZone.Right, LightZoneAssignment.Resolve("1", stored, Suggested));
    }

    [Fact]
    public void ANoZonePickStaysNoZone()
    {
        var stored = new Dictionary<string, LightZone> { ["2"] = LightZone.None };

        Assert.Equal(LightZone.None, LightZoneAssignment.Resolve("2", stored, Suggested));
    }

    [Fact]
    public void ACentredOrUnplacedLightFollowsNoZone()
    {
        Assert.Equal(LightZone.None, LightZoneAssignment.Resolve("3", null, Suggested));
        Assert.Equal(LightZone.None, LightZoneAssignment.Resolve("9", null, Suggested));
        Assert.Equal(LightZone.None, LightZoneAssignment.Resolve("1", null, null));
    }

    [Fact]
    public void EveryLightOfTheRunGetsItsEdge()
    {
        var stored = new Dictionary<string, LightZone> { ["2"] = LightZone.Bottom };

        var zones = LightZoneAssignment.ResolveAll(["1", "2", "3"], stored, Suggested);

        Assert.Equal(LightZone.Left, zones["1"]);
        Assert.Equal(LightZone.Bottom, zones["2"]);
        Assert.Equal(LightZone.None, zones["3"]);
    }

    // A settings file written before 2026-11-25 carries the single-colour
    // switch; it loads without it, and a No zone pick reads back as a pick.
    [Fact]
    public void AnOlderSettingsFileStillLoads()
    {
        const string json = """
            {
              "enabled": true,
              "useMultiLight": false,
              "lightZones": { "1": 3, "2": 0 }
            }
            """;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var settings = JsonSerializer.Deserialize<AmbientSettings>(json, options)!;

        Assert.True(settings.Enabled);
        Assert.Equal(LightZone.Left, settings.LightZones["1"]);
        Assert.Equal(LightZone.None, LightZoneAssignment.Resolve("2", settings.LightZones, Suggested));
    }
}
