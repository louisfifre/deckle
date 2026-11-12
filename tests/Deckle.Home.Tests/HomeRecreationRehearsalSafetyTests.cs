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

    // A replay from a sheet does not know the source's id (it may live on
    // another network): an empty source id skips that one check only.
    [Fact]
    public void AnUnknownSourceIdStillRefusesTheLiveSpace()
    {
        HomeRecreationRehearsal.RefuseUnsafeTarget("", "home-test", "target-id");
        Assert.Throws<InvalidOperationException>(() =>
            HomeRecreationRehearsal.RefuseUnsafeTarget("", "home", "target-id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("other-id")]
    public void TheLiveHomeOpensOnlyOnItsOwnRepeatedId(string confirmed) =>
        Assert.Throws<InvalidOperationException>(() =>
            HomeRecreationRehearsal.RefuseUnconfirmedLiveTarget("old-id", "live-id", confirmed));

    [Fact]
    public void TheLiveHomeIsNeverTheSpaceItsSheetCameFrom() =>
        Assert.Throws<InvalidOperationException>(() =>
            HomeRecreationRehearsal.RefuseUnconfirmedLiveTarget("live-id", "live-id", "live-id"));

    [Fact]
    public void TheConfirmedLiveHomeIsAccepted() =>
        HomeRecreationRehearsal.RefuseUnconfirmedLiveTarget("old-id", "live-id", " live-id ");

    [Fact]
    public async Task AReplayRefusesTheAliasItsSheetWasExportedFromBeforeAnyRead()
    {
        string path = Path.Combine(Path.GetTempPath(), $"home-sheet-{Guid.NewGuid():N}.json");
        try
        {
            new HomeReplaySheet { SourceAlias = "home-ancien" }.Save(path);
            using var server = new FakeHomeAnytypeServer();
            using var api = new Deckle.Anytype.AnytypeApiClient(server.Credentials);
            var aliases = new Deckle.Anytype.AnytypeSpaceAliases(
                new Dictionary<string, string> { ["home-ancien"] = "space-old" });

            InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new HomeRecreationRehearsal(api, aliases)
                    .ReplayFromSheetAsync(path, "HOME-ANCIEN", TestContext.Current.CancellationToken));

            Assert.Contains("exportée de « home-ancien »", refusal.Message);
            Assert.Empty(server.Requests);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
        Assert.Equal(Tags(HomeRecreationRehearsal.Localized(HomeSchema.TargetManifest), "domain"), Tags(manifest, "domain"));
        // A closed vocabulary stays as compiled: the replay refuses and counts.
        Assert.DoesNotContain("État fictif", Tags(manifest, "state"));
        Assert.DoesNotContain("Marque fictive", Tags(HomeSchema.TargetManifest, "manufacturer"));
    }

    // The space shows the terms file's words, not the manifest's English.
    [Fact]
    public void TheAppliedManifestSpeaksTheTermsFile()
    {
        (JsonObject manifest, _) = HomeRecreationRehearsal.ManifestFor(new HomeReplaySheet());

        JsonObject room = manifest["types"]!.AsArray().OfType<JsonObject>()
            .Single(type => type["key"]!.GetValue<string>() == "room");
        Assert.Equal("Pièce", room["name"]!.GetValue<string>());
        Assert.Equal("Pièces", room["plural_name"]!.GetValue<string>());
        JsonObject needed = manifest["properties"]!.AsArray().OfType<JsonObject>()
            .Single(property => property["key"]!.GetValue<string>() == "needed");
        Assert.Equal("Prendre", needed["name"]!.GetValue<string>());
        Assert.Contains("Oui", Tags(manifest, "earthed"));
    }

    [Fact]
    public void ASheetWithItsOwnSectionCollectionsTakesNoManifestSection()
    {
        var carried = new HomeReplaySheet { Structure = new HomeStructure { Collections = [new HomeStructureCollection()] } };

        (JsonObject withOwn, _) = HomeRecreationRehearsal.ManifestFor(carried);
        (JsonObject without, _) = HomeRecreationRehearsal.ManifestFor(new HomeReplaySheet());

        Assert.Empty(withOwn["sections"]!.AsArray());
        Assert.NotEmpty(without["sections"]!.AsArray());
    }

    static string[] Tags(JsonObject manifest, string key) =>
        (manifest["properties"]!.AsArray().OfType<JsonObject>()
            .Single(property => property["key"]!.GetValue<string>() == key)["tags"] as JsonArray ?? [])
        .Select(tag => tag is JsonObject option ? option["name"]!.GetValue<string>() : tag!.GetValue<string>())
        .ToArray();
}
