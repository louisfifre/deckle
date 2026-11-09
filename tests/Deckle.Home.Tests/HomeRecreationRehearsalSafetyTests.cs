using System.Text.Json.Nodes;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The rehearsal's refusals and the manifest it applies on the test space.
[Trait("Category", "unit")]
public sealed class HomeRecreationRehearsalSafetyTests
{
    [Theory]
    [InlineData("home")]
    [InlineData(" HOME ")]
    [InlineData("dev")]
    public void TheLiveAndTheProjectSpacesAreNeverATarget(string alias) =>
        Assert.Throws<InvalidOperationException>(() =>
            HomeRecreationRehearsal.RefuseUnsafeTarget("source-id", alias, "target-id"));

    [Fact]
    public void ASourceIsNeverItsOwnTarget()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            HomeRecreationRehearsal.RefuseUnsafeTarget("same-id", "home-test", "same-id"));

        Assert.Contains("même espace", refusal.Message);
    }

    [Fact]
    public void ADistinctTestSpaceIsAccepted() =>
        HomeRecreationRehearsal.RefuseUnsafeTarget("source-id", "home-test", "target-id");

    [Fact]
    public void TheAppliedManifestCarriesTheOpenOptionsTheSheetUsesOnce()
    {
        var sheet = new HomeReplaySheet
        {
            Objects =
            [
                new HomeReplayObject
                {
                    Type = "device",
                    Properties = new JsonObject
                    {
                        ["manufacturer"] = "Marque fictive",
                        ["domain"] = "Audio",
                        ["supported_protocols"] = new JsonArray("Zigbee", "Protocole fictif"),
                    },
                },
                new HomeReplayObject { Type = "device", Properties = new JsonObject { ["manufacturer"] = "marque FICTIVE" } },
                new HomeReplayObject { Type = "worksite", Properties = new JsonObject { ["state"] = "État fictif" } },
            ],
        };

        (JsonObject manifest, int added) = HomeRecreationRehearsal.ManifestFor(sheet);

        Assert.Equal(2, added);
        Assert.Contains("Marque fictive", Tags(manifest, "manufacturer"));
        Assert.Contains("Protocole fictif", Tags(manifest, "supported_protocols"));
        Assert.Equal(Tags(HomeSchema.TargetManifest, "domain"), Tags(manifest, "domain"));
        // A closed vocabulary stays as compiled: the replay refuses and counts.
        Assert.DoesNotContain("État fictif", Tags(manifest, "state"));
        Assert.DoesNotContain("Marque fictive", Tags(HomeSchema.TargetManifest, "manufacturer"));
    }

    static string[] Tags(JsonObject manifest, string key) =>
        (manifest["properties"]!.AsArray().OfType<JsonObject>()
            .Single(property => property["key"]!.GetValue<string>() == key)["tags"] as JsonArray ?? [])
        .Select(tag => tag is JsonObject option ? option["name"]!.GetValue<string>() : tag!.GetValue<string>())
        .ToArray();
}
