using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;
using static Deckle.Home.Tests.HomeStructureFixtures;

namespace Deckle.Home.Tests;

// The rehearsal's structure step over the fake spaces: the export carries the
// structure into the sheet, and after the objects exist on the target the
// structure is written, read back and counted beside the sheet. (The schema
// step between them runs the schema-admin gestures, which the fake does not
// model: RecreateStructureAsync is the step on its own.)
[Trait("Category", "integration")]
public sealed class HomeRecreationRehearsalStructureTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "deckle-home-rehearsal-structure-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static AnytypeSpaceAliases Aliases(FakeHomeAnytypeServer source, FakeHomeAnytypeServer target) =>
        new(new Dictionary<string, string> { ["source-alias"] = source.SpaceId, ["target-test"] = target.SpaceId });

    [Fact]
    public async Task TheExportCarriesTheStructureIntoTheVersionedSheet()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        string path = Path.Combine(_folder, "sheet.json");

        await new HomeRecreationRehearsal(new AnytypeApiClient(source.Credentials), Aliases(source, target))
            .ExportAsync("source-alias", path, Ct);

        HomeReplaySheet sheet = HomeReplaySheet.Load(path);
        Assert.Equal(HomeReplaySheet.StructureVersion, sheet.Version);
        Assert.Equal(14, sheet.Structure!.Types.Count);
        Assert.Equal(3, sheet.Structure.Templates.Count);
        Assert.Equal(6, sheet.Structure.Widgets.Count);
        Assert.DoesNotContain(source.Requests, request => request.Method != "GET" && request.Path.StartsWith("/v2/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheStructureIsWrittenAndReadBackAfterTheObjectsAndCountedBesideTheSheet()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        string path = Path.Combine(_folder, "sheet.json");
        var aliases = Aliases(source, target);
        await new HomeRecreationRehearsal(new AnytypeApiClient(source.Credentials), aliases).ExportAsync("source-alias", path, Ct);
        HomeReplaySheet sheet = HomeReplaySheet.Load(path);
        var targetApi = new AnytypeApiClient(target.Credentials);
        HomeReplayReport replay = await new HomeSpaceReplayer(targetApi, target.SpaceId).ReplayAsync(sheet, null, Ct);

        HomeStructureResult structure = await new HomeRecreationRehearsal(targetApi, aliases)
            .RecreateStructureAsync(sheet.Structure!, replay, target.SpaceId, path, Ct);

        Assert.Null(structure.Failure);
        Assert.Equal(3, structure.Write!.Written["templates"]);
        Assert.Equal(5, structure.Write.Written["widgets"]);
        Assert.Empty(structure.Write.Refused);
        Assert.Equal(0, structure.Comparison!.Different);
        Assert.True(File.Exists(Path.Combine(_folder, "sheet.structure.json")));
        Assert.True(File.Exists(Path.Combine(_folder, "sheet.structure-compare.json")));

        var requests = target.Requests.ToList();
        int lastObjectCreate = requests.FindLastIndex(request => request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal));
        int firstStructureWrite = requests.FindIndex(request => request.Method != "GET" && request.Path.StartsWith("/v2/", StringComparison.Ordinal));
        Assert.True(firstStructureWrite > lastObjectCreate);
    }

    [Fact]
    public async Task ASurpriseInTheStructureStepIsKeptAsItsFailureAndCountedInsteadOfLosingTheRun()
    {
        using FakeHomeAnytypeServer source = Source();
        FakeHomeAnytypeServer target = Target();
        string path = Path.Combine(_folder, "sheet.json");
        var aliases = Aliases(source, target);
        await new HomeRecreationRehearsal(new AnytypeApiClient(source.Credentials), aliases).ExportAsync("source-alias", path, Ct);
        HomeReplaySheet sheet = HomeReplaySheet.Load(path);
        var targetApi = new AnytypeApiClient(target.Credentials);
        target.Dispose();

        HomeStructureResult structure = await new HomeRecreationRehearsal(targetApi, aliases)
            .RecreateStructureAsync(sheet.Structure!, new HomeReplayReport(), target.SpaceId, path, Ct);

        Assert.Equal("HttpRequestException", structure.Failure);
        Assert.Null(structure.Write);
        Assert.True(File.Exists(Path.Combine(_folder, "sheet.structure-error.json")));
    }

    [Fact]
    public void TheSummaryAndTheCountsCarryTheStructure()
    {
        var write = new HomeStructureWriteReport();
        write.Written["templates"] = 3;
        write.Refused["widgets"] = 1;
        write.Dropped["column"] = 4;
        write.Dropped["unhideable_column"] = 20;
        write.Dropped["column_unwrapped"] = 1;
        write.Dropped["collection_item"] = 2;
        write.Dropped["collection_icon"] = 1;
        var read = new HomeStructureComparison { Compared = 40, Different = 2 };
        read.DifferentByField["type.default_view"] = 2;
        var result = new HomeRehearsalResult(
            "sheet.json", new HomeReplaySheetReport(), 0, 0, new HomeReplayReport(), new HomeComparison(),
            new HomeStructureResult(new Dictionary<string, int> { ["types"] = 14 }, write, read, null));

        string summary = result.Summary();
        JsonObject counts = result.Counts()["structure"]!.AsObject();

        Assert.Contains("Structure, export : types 14", summary);
        Assert.Contains("templates 3", summary);
        Assert.Contains("column 4", summary);
        Assert.Contains("unhideable_column 20", summary);
        Assert.Contains("column_unwrapped 1", summary);
        Assert.Contains("collection_item 2", summary);
        Assert.Contains("collection_icon 1", summary);
        Assert.Equal(20, result.Counts()["structure"]!["dropped"]!["unhideable_column"]!.GetValue<int>());
        Assert.Contains("40 fait(s) comparé(s), 2 différent(s) (type.default_view 2)", summary);
        Assert.True(counts["carried"]!.GetValue<bool>());
        Assert.Equal(3, counts["written"]!["templates"]!.GetValue<int>());
        Assert.Equal(2, counts["comparison"]!["different"]!.GetValue<int>());
        Assert.Equal(2, counts["comparison"]!["different_by_field"]!["type.default_view"]!.GetValue<int>());
    }

    [Fact]
    public void ASheetWithoutStructureSaysSoInTheSummaryAndTheCounts()
    {
        var result = new HomeRehearsalResult(
            "sheet.json", new HomeReplaySheetReport(), 0, 0, new HomeReplayReport(), new HomeComparison());

        Assert.Contains("Structure : absente de la feuille", result.Summary());
        Assert.False(result.Counts()["structure"]!["carried"]!.GetValue<bool>());
    }
}
