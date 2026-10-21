using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public sealed class HomeSchemaAlignmentTests
{
    [Fact]
    public void TargetManifestIsCanonicalAndPointKeepsTheAcceptedOrder()
    {
        JsonObject manifest = HomeSchema.TargetManifest;
        JsonObject point = manifest["types"]!.AsArray().OfType<JsonObject>()
            .Single(type => type["key"]!.GetValue<string>() == "point");

        Assert.Equal(
            new[]
            {
                "code", "category", "installed_in", "controls", "controlled_by",
                "circuit", "outlet_count", "earthed", "conduit_diameter", "dcl",
                "switch_nature", "duct_diameter", "manufacturer", "model_ref",
                "manual", "location", "notes",
            },
            point["properties"]!.AsArray().Select(value => value!.GetValue<string>()));
    }

    [Fact]
    public void WireProjectionPreservesLegacyCoordinatesWithoutPowerConversion()
    {
        Assert.Equal("errand", HomeSchema.WireTypeKey("product"));
        Assert.Equal("floor", HomeSchema.WirePropertyKey("zone"));
        Assert.Equal("documents", HomeSchema.WirePropertyKey("manual"));
        Assert.Equal("errand_category", HomeSchema.WirePropertyKey("product_category"));
        Assert.Equal("memory_size_gb", HomeSchema.WirePropertyKey("memory_size"));
        Assert.Equal("memory_frequency_mhz", HomeSchema.WirePropertyKey("memory_frequency"));
        Assert.Equal("switch_kind", HomeSchema.WirePropertyKey("switch_nature"));
        Assert.Equal("power", HomeSchema.WirePropertyKey("power"));
    }

    [Fact]
    public void TargetFormatsAndRelationsAreTyped()
    {
        Assert.Equal("select", HomeSchema.RequiredProperties[HomeSchema.Properties.Earthed]);
        Assert.Equal("select", HomeSchema.RequiredProperties[HomeSchema.Properties.Dcl]);
        Assert.Equal("multi_select", HomeSchema.RequiredProperties[HomeSchema.Properties.ControlLink]);
        Assert.Equal("number", HomeSchema.RequiredProperties[HomeSchema.Properties.RatedInputPower]);

        Assert.Equal(
            new[] { HomeSchema.Types.Point },
            HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.ConnectedTo]);
        Assert.Equal(
            new[] { HomeSchema.Types.Point, HomeSchema.Types.Device },
            HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.Controls]);
        Assert.Equal(
            new[] { HomeSchema.Types.Panel },
            HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.UpstreamPanel]);
    }

    [Fact]
    public void ObsoleteSurplusPointPropertiesAreNotAllowedByTheTargetContract()
    {
        Assert.False(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, HomeSchema.Properties.LightNature));
        Assert.False(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, HomeSchema.Properties.PowerWatts));
        Assert.True(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, HomeSchema.Properties.Dcl));
        Assert.True(HomeSchema.IsAllowedProperty(HomeSchema.Types.Device, HomeSchema.Properties.ConnectedTo));
    }

    [Fact]
    public void NonConformingSchemaErrorListsSixFailuresAndCountsTheRest()
    {
        // An empty space fails every compiled check, which is the worst case
        // for the size of the message a model receives.
        var empty = new SchemaSnapshot(
            new Dictionary<string, SchemaTypeInfo>(),
            new Dictionary<string, SchemaPropertyInfo>(),
            new Dictionary<string, IReadOnlyDictionary<string, SchemaTagInfo>>());
        int totalFailures = HomeSchema.RequiredProperties.Count
            + HomeSchema.RequiredByType.Count
            + HomeSchema.ClosedVocabularies.Sum(vocabulary => vocabulary.Value.Count);
        Assert.True(totalFailures > 6);

        HomeSchemaException error = Assert.Throws<HomeSchemaException>(() => HomeSchema.Validate(empty));

        const string Prefix = "Le schéma Home n’est pas conforme : ";
        Assert.StartsWith(Prefix, error.Message);
        string marker = $" ; … et {totalFailures - 6} autres";
        int markerIndex = error.Message.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex > 0, error.Message);
        string[] listed = error.Message[Prefix.Length..markerIndex].Split(" ; ");
        Assert.Equal(6, listed.Length);
        Assert.StartsWith(". Compare", error.Message[(markerIndex + marker.Length)..]);
    }
}
