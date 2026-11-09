using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The export of a source space on the old keys: the bridge to the manifest
// keys, what stays unmapped, what is skipped.
[Trait("Category", "integration")]
public sealed class HomeSpaceExporterTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static async Task<HomeReplaySheet> ExportAsync(FakeHomeAnytypeServer source) =>
        await new HomeSpaceExporter(new AnytypeApiClient(source.Credentials))
            .ExportAsync(source.SpaceId, "source", DateTimeOffset.UnixEpoch, Ct);

    static HomeReplayObject Single(HomeReplaySheet sheet, string oldId) =>
        sheet.Objects.Single(value => value.OldId == oldId);

    [Fact]
    public async Task OldTypesTravelUnderTheirNewKeysAndBuiltInsAreSkipped()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplaySheet sheet = await ExportAsync(source);

        Assert.Equal("product", Single(sheet, "errand-1").Type);
        Assert.Equal("zone", Single(sheet, "floor-1").Type);
        Assert.Equal(3, sheet.Report.Types["point"]);
        Assert.Equal(1, sheet.Report.SkippedTypes["page"]);
        Assert.Equal(1, sheet.Report.SkippedTypes["collection"]);
        Assert.DoesNotContain(sheet.Objects, value => value.OldId is "page-1" or "section-1");
    }

    [Fact]
    public async Task AnErrandBecomesAProductWithItsCategoryAndKeepsItsAisleUnmapped()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplayObject product = Single(await ExportAsync(source), "errand-1");

        Assert.True(product.Properties["needed"]!.GetValue<bool>());
        Assert.Equal("Produits céréaliers secs", product.Properties["product_category"]!.GetValue<string>());
        Assert.Equal("Rayon fictif", product.Unmapped["aisle"]!.GetValue<string>());
        Assert.False(product.Properties.ContainsKey("errand_category"));
    }

    [Fact]
    public async Task ARoomOfAFloorCollectionPointsAtItsZone()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplayObject room = Single(await ExportAsync(source), "room-1");

        Assert.Equal("ZZ", room.Code);
        Assert.Equal("Étage fictif", room.Properties["zone"]![0]!.GetValue<string>());
        Assert.Equal(["floor-1"], room.References["zone"]);
        Assert.Empty(room.Collections);
    }

    [Fact]
    public async Task NotesMoveIntoTheBodyAfterABlankLine()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplaySheet sheet = await ExportAsync(source);

        Assert.Equal("Note fictive du point", Single(sheet, "point-cs").Body);
        Assert.Equal("Corps fictif du plafonnier\n\nNote fictive du plafonnier", Single(sheet, "point-lc").Body);
        Assert.Equal(2, sheet.Report.NotesMoved);
        Assert.DoesNotContain(sheet.Objects, value => value.Properties.ContainsKey("notes"));
    }

    [Fact]
    public async Task APointKeepsItsRelationsByCodeAndLeavesItsRoomAndCategoryToItsCode()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplayObject point = Single(await ExportAsync(source), "point-cs");

        Assert.Equal("ZZ-CS01", point.Code);
        Assert.Equal("ZZ-LC01", point.Properties["controls"]![0]!.GetValue<string>());
        Assert.Equal(["point-lc"], point.References["controls"]);
        Assert.False(point.Properties.ContainsKey("installed_in"));
        Assert.False(point.Properties.ContainsKey("category"));
    }

    [Fact]
    public async Task ACategoryOutsideTheTwentyTwoIsKeptUnmapped()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplaySheet sheet = await ExportAsync(source);

        Assert.Equal("SA — fictive", Single(sheet, "point-sa").Unmapped["category"]!["name"]!.GetValue<string>());
        Assert.Equal(1, sheet.Report.Unmapped["category"]);
    }

    [Fact]
    public async Task DeadKeysAndRenamedKeysOfEquipment()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplaySheet sheet = await ExportAsync(source);
        HomeReplayObject device = Single(sheet, "device-1");
        HomeReplayObject component = Single(sheet, "component-1");

        Assert.Equal(40, device.Unmapped["power_watts"]!.GetValue<double>());
        Assert.Equal(35, device.Properties["rated_input_power"]!.GetValue<double>());
        Assert.Equal("Marque fictive", device.Properties["manufacturer"]!.GetValue<string>());
        Assert.Equal("ZZ", device.Properties["installed_in"]![0]!.GetValue<string>());
        Assert.False(device.Properties.ContainsKey("links"));
        Assert.Equal([HomeRehearsalFixtures.Section], device.Collections);
        Assert.Equal(16, component.Properties["memory_size"]!.GetValue<double>());
        Assert.Equal("Système fictif", component.Properties["part_of"]![0]!.GetValue<string>());
        Assert.Equal(1, sheet.Report.Unmapped["power_watts"]);
    }

    [Fact]
    public async Task ATaskKeepsItsDoneBoxAndItsWorksite()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplaySheet sheet = await ExportAsync(source);
        HomeReplayObject todo = Single(sheet, "todo-1");

        Assert.True(todo.Done);
        Assert.Equal("Chantier fictif", todo.Properties["worksite"]![0]!.GetValue<string>());
        Assert.Equal("En cours", Single(sheet, "worksite-1").Properties["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnIdeaIsItsText()
    {
        using FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();

        HomeReplayObject idea = Single(await ExportAsync(source), "idea-1");

        Assert.Equal("", idea.Name);
        Assert.Equal("Idée fictive\ndétail fictif", idea.Body);
        Assert.Equal("Idée fictive", idea.Selector);
    }
}

// The pure pieces of the bridge.
[Trait("Category", "unit")]
public sealed class HomeSpaceExporterBridgeTests
{
    [Theory]
    [InlineData(null, "note", "note")]
    [InlineData("", " note ", "note")]
    [InlineData("corps\n\n", "note", "corps\n\nnote")]
    public void NotesFollowTheBodyAfterABlankLine(string? body, string notes, string expected) =>
        Assert.Equal(expected, HomeSpaceExporter.AppendNotes(body, notes));

    [Theory]
    [InlineData("LC — plafonnier", "LC")]
    [InlineData("ps", "PS")]
    [InlineData("SA — fictive", null)]
    [InlineData("O — autre", null)]
    public void ACategoryIsReadFromItsOptionName(string name, string? expected) =>
        Assert.Equal(expected, HomeSpaceExporter.CategoryCode(new JsonObject
        {
            ["select"] = new JsonObject { ["key"] = "hash", ["name"] = name },
        }));

    [Fact]
    public void OptionsTravelByNameAndEmptyValuesNotAtAll()
    {
        Assert.Equal("Nom fictif", HomeSpaceExporter.PlainValue(new JsonObject
        {
            ["select"] = new JsonObject { ["key"] = "hash", ["name"] = "Nom fictif" },
        })!.GetValue<string>());
        JsonArray names = (JsonArray)HomeSpaceExporter.PlainValue(new JsonObject
        {
            ["multi_select"] = new JsonArray(new JsonObject { ["name"] = "Un" }, new JsonObject { ["name"] = "Deux" }),
        })!;
        Assert.Equal(["Un", "Deux"], names.Select(node => node!.GetValue<string>()));
        Assert.Null(HomeSpaceExporter.PlainValue(new JsonObject { ["text"] = "  " }));
        Assert.Null(HomeSpaceExporter.PlainValue(new JsonObject { ["select"] = null }));
    }

    [Fact]
    public void TheOldTypesAreRenamedAndTheOthersKept()
    {
        Assert.Equal("product", HomeSpaceExporter.BridgeType("errand"));
        Assert.Equal("zone", HomeSpaceExporter.BridgeType("floor"));
        Assert.Equal("device", HomeSpaceExporter.BridgeType("device"));
    }
}
