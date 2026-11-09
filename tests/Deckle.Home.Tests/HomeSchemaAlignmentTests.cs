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
                "manual", "location",
            },
            point["properties"]!.AsArray().Select(value => value!.GetValue<string>()));
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
        Assert.False(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, "light_nature"));
        Assert.False(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, "power_watts"));
        Assert.False(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, "notes"));
        Assert.True(HomeSchema.IsAllowedProperty(HomeSchema.Types.Point, HomeSchema.Properties.Dcl));
        Assert.True(HomeSchema.IsAllowedProperty(HomeSchema.Types.Device, HomeSchema.Properties.ConnectedTo));
    }

    [Fact]
    public void PointCategoryIsTheClosedListOfTwentyTwo()
    {
        Assert.Equal(
            new[]
            {
                "ps", "pp", "pn", "pf", "pc", "pr",
                "lc", "lw", "ls", "lr",
                "cp", "cs",
                "ar", "ad",
                "ve", "vs", "vt", "vf", "vp",
                "ws", "wt", "wd",
            },
            HomeSchema.ClosedVocabularies[HomeSchema.Properties.Category]);
    }

    [Fact]
    public void RatedOutputPowerIsARequiredNumberOnDevice()
    {
        Assert.Equal("number", HomeSchema.RequiredProperties[HomeSchema.Properties.RatedOutputPower]);
        Assert.Contains(HomeSchema.Properties.RatedOutputPower, HomeSchema.RequiredByType[HomeSchema.Types.Device]);
    }

    [Fact]
    public void ArchivedIsARequiredCheckboxOnTheSixTypesThatHaveALife()
    {
        string[] archivable =
        [
            HomeSchema.Types.Device, HomeSchema.Types.Component, HomeSchema.Types.Utensil,
            HomeSchema.Types.Plant, HomeSchema.Types.Idea, HomeSchema.Types.Product,
        ];

        Assert.Equal("checkbox", HomeSchema.RequiredProperties[HomeSchema.Properties.Archived]);
        Assert.Equal(
            archivable.OrderBy(type => type, StringComparer.Ordinal),
            HomeSchema.RequiredByType
                .Where(type => type.Value.Contains(HomeSchema.Properties.Archived))
                .Select(type => type.Key)
                .OrderBy(type => type, StringComparer.Ordinal));
    }

    [Fact]
    public void ZoneIsACreatableBasicTypeWithoutProperties()
    {
        Assert.Contains(HomeSchema.Types.Zone, HomeSchema.CreatableTypes);
        Assert.DoesNotContain(HomeSchema.Types.Zone, HomeSchema.CodedTypes);
        Assert.Empty(HomeSchema.RequiredByType[HomeSchema.Types.Zone]);
        JsonObject zone = HomeSchema.CreateRequiredSchemaManifest()["types"]!.AsArray().OfType<JsonObject>()
            .Single(type => type["key"]!.GetValue<string>() == HomeSchema.Types.Zone);
        Assert.Equal("basic", zone["layout"]!.GetValue<string>());
        Assert.Empty(zone["properties"]!.AsArray());
        Assert.Equal(new[] { HomeSchema.Types.Zone }, HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.Zone]);
    }

    [Fact]
    public void AnnotatedTargetsReplaceTheHardCodedPoweredByAndWorksiteGuards()
    {
        Assert.Equal(
            new[] { HomeSchema.Types.Point, HomeSchema.Types.Device },
            HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.PoweredBy]);
        Assert.Equal(
            new[] { HomeSchema.Types.Worksite },
            HomeSchema.ObjectPropertyTargets[HomeSchema.Properties.Worksite]);
    }

    [Fact]
    public void RequiredSchemaManifestListsTheFourteenManifestTypesWithTheirLayouts()
    {
        (string Key, string Layout)[] Types(JsonObject manifest) => manifest["types"]!.AsArray().OfType<JsonObject>()
            .Select(type => (type["key"]!.GetValue<string>(), type["layout"]!.GetValue<string>()))
            .ToArray();

        (string Key, string Layout)[] expected = Types(HomeSchema.TargetManifest);
        Assert.Equal(14, expected.Length);
        Assert.Equal(expected, Types(HomeSchema.CreateRequiredSchemaManifest()));
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
