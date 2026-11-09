using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// Export → replay → compare across two fake spaces: a source on the old keys
// and a target on the compiled manifest. The bridge holds, the refused item
// costs itself only, and every mapped value reads back equal.
[Trait("Category", "integration")]
public sealed class HomeRecreationRoundTripTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "deckle-home-rehearsal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed record Run(
        HomeReplaySheet Sheet, HomeReplayReport Replay, HomeComparison Comparison, FakeHomeAnytypeServer Target);

    private async Task<Run> RunAsync(FakeHomeAnytypeServer source, FakeHomeAnytypeServer target)
    {
        string sheetPath = Path.Combine(_folder, "sheet.json");
        HomeReplaySheet sheet = await new HomeSpaceExporter(new AnytypeApiClient(source.Credentials))
            .ExportAsync(source.SpaceId, "source", DateTimeOffset.UnixEpoch, Ct);
        sheet.Save(sheetPath);
        sheet = HomeReplaySheet.Load(sheetPath);

        HomeReplayReport replay = await new HomeSpaceReplayer(new AnytypeApiClient(target.Credentials), target.SpaceId)
            .ReplayAsync(sheet, HomeReplaySheet.Beside(sheetPath, "replay"), Ct);
        HomeComparison comparison = await new HomeRecreationComparer(new AnytypeApiClient(target.Credentials))
            .CompareAsync(sheet, target.SpaceId, HomeReplaySheet.Beside(sheetPath, "compare"), Ct);
        return new Run(sheet, replay, comparison, target);
    }

    static JsonObject Created(FakeHomeAnytypeServer target, string type, string name) =>
        target.Objects.Single(value =>
            value["type"]!["key"]!.GetValue<string>() == type && value["name"]!.GetValue<string>() == name);

    static JsonObject? Entry(JsonObject value, string key) =>
        value["properties"]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(entry => entry["key"]!.GetValue<string>() == key);

    [Fact]
    public async Task EveryMappedValueReadsBackEqual()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();

        Run run = await RunAsync(source, target);

        Assert.Equal(0, run.Comparison.Different);
        Assert.Equal(0, run.Comparison.ValuesDifferent);
        Assert.Equal(0, run.Comparison.Extra);
        Assert.False(run.Comparison.CollectionsUnread);
        // The one point whose category left the 22 is the only object missing.
        Assert.Equal(1, run.Comparison.Missing);
        Assert.Equal(run.Sheet.Objects.Count - 1, run.Comparison.Equal);
    }

    [Fact]
    public async Task TheReplayCountsWhatItCreatedRefusedAndLinkedAfterwards()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();

        Run run = await RunAsync(source, target);

        Assert.Equal(run.Sheet.Objects.Count - 1, run.Replay.Created);
        Assert.Equal(2, run.Replay.CreatedByType["point"]);
        Assert.Equal(1, run.Replay.RefusedByReason["catégorie hors des 22"]);
        Assert.Equal(1, run.Replay.Refused);
        // The two points name each other: the second exists only after the
        // batch, so both relations land in the update pass.
        Assert.Equal(2, run.Replay.RelationUpdates);
        Assert.Equal(1, run.Replay.CompletedTodos);
        Assert.Equal(0, run.Replay.CollectionsDropped);
        Assert.Equal(0, run.Replay.RelationsDropped);
        Assert.Contains(
            target.Requests,
            request => request.Method == "POST" && request.Path.EndsWith("/lists/target-section/objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheBridgeArrivesOnTheNewModel()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();

        await RunAsync(source, target);

        JsonObject product = Created(target, "product", "Riz fictif");
        Assert.True(Entry(product, "needed")!["checkbox"]!.GetValue<bool>());
        Assert.Equal("tag-product_category-dry_cereal_products", Entry(product, "product_category")!["select"]!.GetValue<string>());

        JsonObject zone = Created(target, "zone", "Étage fictif");
        JsonObject room = Created(target, "room", "Pièce fictive");
        Assert.Equal(zone["id"]!.GetValue<string>(), Entry(room, "zone")!["objects"]![0]!.GetValue<string>());

        JsonObject point = Created(target, "point", "Interrupteur fictif");
        Assert.Equal("Note fictive du point", point["markdown"]!.GetValue<string>());
        Assert.Equal(Created(target, "point", "Plafonnier fictif")["id"]!.GetValue<string>(),
            Entry(point, "controls")!["objects"]![0]!.GetValue<string>());

        JsonObject component = Created(target, "component", "Carte fictive");
        Assert.Equal(16, Entry(component, "memory_size")!["number"]!.GetValue<double>());
        Assert.True(Entry(Created(target, "todo", "Tâche fictive"), "done")!["checkbox"]!.GetValue<bool>());
        Assert.Null(Entry(Created(target, "device", "Appareil fictif"), "power_watts"));
    }

    [Fact]
    public async Task TheSheetCountsWhatTheBridgeLeftUnmapped()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();

        Run run = await RunAsync(source, target);

        Assert.Equal(
            new SortedDictionary<string, int> { ["aisle"] = 1, ["category"] = 1, ["power_watts"] = 1 },
            run.Sheet.Report.Unmapped);
        Assert.Equal(2, run.Sheet.Report.NotesMoved);
    }

    [Fact]
    public async Task ATypeWithASingleTemplateCreatesFromIt()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();
        target.AddTemplate("device", "template-device", "Modèle fictif");

        Run run = await RunAsync(source, target);

        Assert.Equal(1, run.Replay.Templated);
        Assert.Contains(target.Requests, request =>
            request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal)
            && request.Body.Contains("\"template_id\":\"template-device\"", StringComparison.Ordinal));
    }

    // Two components share a name; the target lists them in the reverse of
    // the sheet's order. Each must be compared with its own twin.
    [Fact]
    public async Task TwinsAreComparedWithTheTwinWhoseValuesAgree()
    {
        using var target = new FakeHomeAnytypeServer(HomeRehearsalFixtures.TargetSpace);
        target.SetObjects(
            HomeRehearsalFixtures.Item("new-b", "component", "Carte jumelle",
                HomeRehearsalFixtures.Number("purchase_price", 20)),
            HomeRehearsalFixtures.Item("new-a", "component", "Carte jumelle",
                HomeRehearsalFixtures.Number("purchase_price", 10)));
        var sheet = new HomeReplaySheet
        {
            Objects =
            [
                new HomeReplayObject { OldId = "old-a", Type = "component", Name = "Carte jumelle",
                    Properties = new JsonObject { ["purchase_price"] = 10 } },
                new HomeReplayObject { OldId = "old-b", Type = "component", Name = "Carte jumelle",
                    Properties = new JsonObject { ["purchase_price"] = 20 } },
            ],
        };

        HomeComparison comparison = await new HomeRecreationComparer(new AnytypeApiClient(target.Credentials))
            .CompareAsync(sheet, target.SpaceId, null, Ct);

        Assert.Equal(0, comparison.ValuesDifferent);
        Assert.Equal(2, comparison.Equal);
    }

    [Fact]
    public async Task TheDetailsStayBesideTheSheet()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        using FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();

        await RunAsync(source, target);

        string replay = File.ReadAllText(Path.Combine(_folder, "sheet.replay.json"));
        Assert.Contains("catégorie hors des 22", replay);
        Assert.Contains("ZZ-SA01", replay);
        Assert.Contains("ZZ-SA01", File.ReadAllText(Path.Combine(_folder, "sheet.compare.json")));
    }
}
