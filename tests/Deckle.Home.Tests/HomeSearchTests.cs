using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Home.Tests;

public sealed class HomeSearchTests
{
    private static HomeSearchFilter Filter() => new(null, null, null, null, null, null);

    [Fact]
    public async Task EquipmentFiltersCombineAndExcludeUnrelatedDevices()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            Device("bulb-a", "Lamp A", "domotique", "bulb"),
            Device("speaker", "Speaker", "audio", "speaker"),
            Device("bulb-b", "Lamp B", "audio", "bulb"));
        var gestures = new HomeGestures(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);
        HomeSearchPage result = await gestures.SearchPageAsync(
            Filter() with { Type = "device", Domain = "domotique", EquipmentCategory = "bulb" },
            TestContext.Current.CancellationToken);
        Assert.Equal("bulb-a", Assert.Single(result.Data["matches"]!.AsArray())!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExplicitWorksiteDoesNotReturnTasksFromOtherWorksites()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Worksite("a", "Work A"),
            FakeHomeAnytypeServer.Worksite("b", "Work B"),
            FakeHomeAnytypeServer.Todo("task-a", "Check socket", "a", false),
            FakeHomeAnytypeServer.Todo("task-b", "Check socket", "b", false));
        var gestures = new HomeGestures(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);
        HomeSearchPage result = await gestures.SearchPageAsync(
            Filter() with { Worksite = "Work A", Done = false }, TestContext.Current.CancellationToken);
        Assert.Equal("task-a", Assert.Single(result.Data["matches"]!.AsArray())!["id"]!.GetValue<string>());
    }

    [Fact]
    public void PaginationSortsTiesByIdentityAndReportsExistingMatchesBeyondThePage()
    {
        JsonObject[] objects = [Device("b", "Lamp", "audio", "bulb"), Device("a", "Lamp", "audio", "bulb")];
        HomeSearchPage first = HomeGestures.BuildSearchPage(objects, 0, 1);
        Assert.Equal("a", first.Data["matches"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(2, first.Data["total"]!.GetValue<int>());
        Assert.Equal(1, first.Data["next_offset"]!.GetValue<int>());
        HomeSearchPage pastEnd = HomeGestures.BuildSearchPage(objects, 2, 1);
        Assert.Empty(pastEnd.Data["matches"]!.AsArray());
        Assert.Equal(2, pastEnd.Data["total"]!.GetValue<int>());
        Assert.Null(pastEnd.Data["next_offset"]);
        Assert.NotEqual("Aucun résultat.", pastEnd.Text);
    }

    [Theory]
    [InlineData("bulb")]
    [InlineData("Bulb")]
    [InlineData("Ampoule")]
    [InlineData("live-bulb")]
    public void SeedKeyEnglishFrenchAndLiveIdResolveTheSameSelect(string requested)
    {
        JsonObject value = Device("a", "Lamp", "audio", "live-bulb");
        SchemaTagInfo[] tags = [new("live-bulb", "ampoule", "Ampoule", "")];
        Assert.True(HomeGestures.SearchSelectMatches(value, "equipment_category", requested, tags));
        Assert.False(HomeGestures.SearchSelectMatches(value, "equipment_category", "speaker", tags));
    }

    [Fact]
    public void LiveOnlyOptionMatchesItsNameWithoutRequiringCompiledTerms()
    {
        JsonObject value = Device("a", "Example", "live-domain", "bulb");
        SchemaTagInfo[] tags = [new("live-domain", "custom", "Custom domain", "")];
        Assert.True(HomeGestures.SearchSelectMatches(value, "domain", "Custom domain", tags));
    }

    private static JsonObject Device(string id, string name, string domain, string category) => new()
    {
        ["id"] = id, ["name"] = name, ["type"] = new JsonObject { ["key"] = "device" },
        ["properties"] = new JsonArray(
            new JsonObject { ["key"] = "domain", ["select"] = domain },
            new JsonObject { ["key"] = "equipment_category", ["select"] = category }),
    };
}
