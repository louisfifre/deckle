using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;
using static Deckle.Home.Tests.HomeStructureFixtures;

namespace Deckle.Home.Tests;

// The read-back holds the target against the remapped expectation: nothing
// differs after a faithful write, and a drift is counted under its field and
// described beside the sheet.
[Trait("Category", "integration")]
public sealed class HomeStructureComparerTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HomeStructureWriterTests _writer = new();

    public void Dispose() => _writer.Dispose();

    private static Task<HomeStructureComparison> CompareAsync(
        HomeStructureWriterTests.Run run, FakeHomeAnytypeServer target, string? detailPath = null) =>
        new HomeStructureComparer(new AnytypeApiClient(target.Credentials))
            .CompareAsync(run.Outcome.Plan, target.SpaceId, detailPath, Ct);

    [Fact]
    public async Task AFaithfulWriteReadsBackWithoutDifference()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        HomeStructureWriterTests.Run run = await _writer.RunAsync(source, target);

        HomeStructureComparison comparison = await CompareAsync(run, target);

        Assert.Equal(0, comparison.Different);
        Assert.Empty(comparison.Unread);
        // Types (settings, sections, views), templates, collections, widgets.
        Assert.True(comparison.Compared > 150, $"compared {comparison.Compared}");
    }

    [Fact]
    public async Task ADriftIsCountedUnderItsFieldAndDescribedBesideTheSheet()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        HomeStructureWriterTests.Run run = await _writer.RunAsync(source, target);

        var api = new AnytypeApiClient(target.Credentials);
        await api.V2.SendJsonAsync(HttpMethod.Patch, "types/product", new JsonObject { ["default_view"] = "gallery" }, null, target.SpaceId, Ct);
        await api.V2.SendJsonAsync(HttpMethod.Patch, "types/product", new JsonObject
        {
            ["ops"] = new JsonArray(new JsonObject
            {
                ["op"] = "update_view", ["view"] = run.Outcome.Plan.ViewIds[HomeStructurePlan.ViewKey("src-type-errand", "vE1")],
                ["set"] = new JsonObject { ["group_by"] = "needed" },
                ["columns"] = new JsonObject { ["quantity"] = new JsonObject { ["hidden"] = false } },
            }),
        }, null, target.SpaceId, Ct);
        target.RemoveWidgets();
        string detailPath = Path.Combine(Path.GetTempPath(), $"home-compare-{Guid.NewGuid():N}.json");

        try
        {
            HomeStructureComparison comparison = await CompareAsync(run, target, detailPath);

            Assert.Equal(1, comparison.DifferentByField["type.default_view"]);
            Assert.Equal(1, comparison.DifferentByField["type_view.group_by"]);
            Assert.Equal(1, comparison.DifferentByField["type_view.columns"]);
            Assert.Equal(1, comparison.DifferentByField["widget.count"]);
            Assert.Equal(5, comparison.DifferentByField["widget.missing"]);
            Assert.Equal(9, comparison.Different);
            string details = File.ReadAllText(detailPath);
            Assert.Contains("type product", details);
            Assert.Contains("gallery", details);
        }
        finally
        {
            File.Delete(detailPath);
        }
    }

    [Fact]
    public async Task AViewTheWriteRefusedIsReportedMissing()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        target.FailNext(request => request.Method == "PATCH"
            && request.Path.EndsWith("/types/worksite", StringComparison.Ordinal)
            && request.Body.Contains("insert_view", StringComparison.Ordinal));
        HomeStructureWriterTests.Run run = await _writer.RunAsync(source, target);

        HomeStructureComparison comparison = await CompareAsync(run, target);

        Assert.Equal(1, comparison.DifferentByField["type_view.missing"]);
        Assert.Equal(1, comparison.DifferentByField["type_view.count"]);
    }

    [Fact]
    public void TheFilterTextIgnoresOrderAndWhatTheServerAdds()
    {
        JsonNode? first = JsonNode.Parse("""[{"property":"a","condition":"in","value":["y","x"],"id":"1"},{"property":"b","condition":"empty"}]""");
        JsonNode? second = JsonNode.Parse("""[{"property":"b","condition":"empty","include_time":false},{"property":"a","condition":"in","value":["x","y"]}]""");

        Assert.Equal(HomeStructureComparer.FilterText(first), HomeStructureComparer.FilterText(second));
        Assert.NotEqual(HomeStructureComparer.FilterText(first), HomeStructureComparer.FilterText(JsonNode.Parse("[]")));
    }

    [Fact]
    public void TheHistogramCountsBlockKinds() =>
        Assert.Equal(
            "dataview 1, heading_2 2",
            HomeStructureComparer.Histogram((JsonArray)JsonNode.Parse("""[{"type":"heading_2"},{"type":"dataview"},{"type":"heading_2"}]""")!));
}
