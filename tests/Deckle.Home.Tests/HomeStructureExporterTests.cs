using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The structure export reads a source space through v2 GETs and keeps what it
// finds as the source speaks it; the sheet carries it under `structure`.
[Trait("Category", "integration")]
public sealed class HomeStructureExporterTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static async Task<HomeStructure> ExportAsync(FakeHomeAnytypeServer source) =>
        await new HomeStructureExporter(new AnytypeApiClient(source.Credentials)).ExportAsync(source.SpaceId, Ct);

    [Fact]
    public async Task TheHomeTypesAreExportedUnderTheirSourceKeyWithSettingsAndViews()
    {
        using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();

        HomeStructure structure = await ExportAsync(source);

        Assert.Equal(14, structure.Types.Count);
        HomeStructureType errand = structure.Types.Single(type => type.Key == "errand");
        Assert.Equal("src-type-errand", errand.TypeId);
        Assert.Equal("kanban", errand.DefaultView);
        Assert.Null(errand.DefaultTemplate);
        Assert.Equal(2, errand.Views.Count);
        Assert.Equal("hidden", errand.Properties.Single(property => property.Property == "aisle").Section);
        Assert.Null(errand.Properties.Single(property => property.Property == "quantity").Section);

        HomeStructureType room = structure.Types.Single(type => type.Key == "room");
        Assert.Equal(HomeStructureFixtures.OldTemplateRoom, room.DefaultTemplate);
        Assert.Equal(4, structure.Types.Single(type => type.Key == "worksite").Views.Count);
        Assert.Equal(18, structure.Report["type_views"]);
        Assert.DoesNotContain(structure.Types, type => type.Key == "product");
    }

    [Fact]
    public async Task OnlySectionCollectionsAreExportedWithTheirViews()
    {
        using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();

        HomeStructure structure = await ExportAsync(source);

        // The old floor collection is a zone object now, not a section.
        HomeStructureCollection section = Assert.Single(structure.Collections);
        Assert.Equal("section-1", section.OldId);
        Assert.Equal(HomeRehearsalFixtures.Section, section.Name);
        Assert.Equal(2, section.Views.Count);
        Assert.Equal(2, structure.Report["collection_views"]);
    }

    [Fact]
    public async Task TemplatesAreKeptWholeAndThoseOfOtherTypesAreCounted()
    {
        using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();

        HomeStructure structure = await ExportAsync(source);

        Assert.Equal(3, structure.Templates.Count);
        HomeStructureTemplate room = structure.Templates.Single(template => template.OldId == HomeStructureFixtures.OldTemplateRoom);
        Assert.Equal("room", room.TemplateFor);
        Assert.Equal("Modèle pièce fictif", room.Name);
        Assert.Equal(9, ((JsonArray)room.Document["blocks"]!).Count);
        Assert.Equal(1, structure.Report["templates_skipped"]);
        Assert.Equal(13, structure.Report["template_blocks"]);
    }

    [Fact]
    public async Task WidgetsComeInOrderAndThePersonalOnesAreLeftOut()
    {
        using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();

        HomeStructure structure = await ExportAsync(source);

        Assert.Equal(["sw1", "sw2", "sw3", "sw4", "sw6", "sw7"], structure.Widgets.Select(widget => widget.OldId));
        HomeStructureWidget first = structure.Widgets[0];
        Assert.Equal(("src-type-errand", "view", 6, "vE2"), (first.Target, first.Layout, first.Limit, first.ViewId));
        Assert.Equal(1, structure.Report["widgets_skipped"]);
    }

    [Fact]
    public async Task ARefusedReadCostsThatItemOnlyAndIsCounted()
    {
        using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();
        source.FailNext(request => request.Method == "GET" && request.Path.EndsWith("/v2/spaces/source-space/types/room", StringComparison.Ordinal));
        source.FailNext(request => request.Method == "GET" && request.Path.EndsWith("/objects/old-template-device", StringComparison.Ordinal));

        HomeStructure structure = await ExportAsync(source);

        Assert.Equal(13, structure.Types.Count);
        Assert.Equal(1, structure.Report["unread_types"]);
        Assert.Equal(2, structure.Templates.Count);
        Assert.Equal(1, structure.Report["unread_templates"]);
        Assert.Equal(6, structure.Widgets.Count);
    }

    [Fact]
    public async Task ASourceThatServesNoV2ExportsAnEmptyStructureAndCountsWhatItCouldNotRead()
    {
        using var source = HomeRehearsalFixtures.Source();

        HomeStructure structure = await ExportAsync(source);

        Assert.Empty(structure.Types);
        Assert.Empty(structure.Templates);
        Assert.Empty(structure.Widgets);
        Assert.Equal(14, structure.Report["unread_types"]);
    }

    [Fact]
    public void ASheetWithoutStructureLoadsAsVersionOneAndCarriesNone()
    {
        string path = Path.Combine(Path.GetTempPath(), $"home-sheet-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"source_space":"s","objects":[],"report":{}}""");

            HomeReplaySheet sheet = HomeReplaySheet.Load(path);

            Assert.Equal(1, sheet.Version);
            Assert.Null(sheet.Structure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TheStructureRoundTripsThroughTheSheetFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"home-sheet-{Guid.NewGuid():N}.json");
        try
        {
            using FakeHomeAnytypeServer source = HomeStructureFixtures.Source();
            var sheet = new HomeReplaySheet
            {
                Version = HomeReplaySheet.StructureVersion,
                Structure = await ExportAsync(source),
            };

            sheet.Save(path);
            HomeReplaySheet loaded = HomeReplaySheet.Load(path);

            Assert.Equal(HomeReplaySheet.StructureVersion, loaded.Version);
            Assert.Equal(sheet.Structure.Types.Count, loaded.Structure!.Types.Count);
            Assert.Equal(
                sheet.Structure.Templates[0].Document.ToJsonString(),
                loaded.Structure.Templates[0].Document.ToJsonString());
            Assert.Equal(sheet.Structure.Widgets.Select(widget => widget.ViewId), loaded.Structure.Widgets.Select(widget => widget.ViewId));
            Assert.Equal(sheet.Structure.Report, loaded.Structure.Report);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
