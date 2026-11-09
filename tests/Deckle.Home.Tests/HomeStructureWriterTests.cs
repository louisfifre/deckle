using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;
using static Deckle.Home.Tests.HomeStructureFixtures;

namespace Deckle.Home.Tests;

// The structure written on a target after the replay: templates, type
// settings, type views, collection views, widgets, each read back off the
// fake target and held against what the source meant.
[Trait("Category", "integration")]
public sealed class HomeStructureWriterTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "deckle-home-structure-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    internal sealed record Run(
        HomeReplaySheet Sheet, HomeReplayReport Replay, HomeStructureOutcome Outcome, string DetailPath);

    internal async Task<Run> RunAsync(FakeHomeAnytypeServer source, FakeHomeAnytypeServer target)
    {
        var sourceApi = new AnytypeApiClient(source.Credentials);
        var targetApi = new AnytypeApiClient(target.Credentials);
        HomeReplaySheet sheet = await new HomeSpaceExporter(sourceApi)
            .ExportAsync(source.SpaceId, "source", DateTimeOffset.UnixEpoch, Ct);
        sheet.Structure = await new HomeStructureExporter(sourceApi).ExportAsync(source.SpaceId, Ct);
        HomeReplayReport replay = await new HomeSpaceReplayer(targetApi, target.SpaceId).ReplayAsync(sheet, null, Ct);

        string detailPath = Path.Combine(_folder, "sheet.structure.json");
        HomeStructureOutcome outcome = await new HomeStructureWriter(targetApi, target.SpaceId)
            .WriteAsync(sheet.Structure, replay.CreatedIds, detailPath, Ct);
        return new Run(sheet, replay, outcome, detailPath);
    }

    static JsonArray Views(FakeHomeAnytypeServer target, string typeKey) =>
        (JsonArray)((JsonArray)target.TypeStructure(typeKey)!["blocks"]!)[0]!["views"]!;

    static string[] Visible(JsonNode? view) =>
        ((JsonArray)view!["columns"]!).OfType<JsonObject>()
            // lastOpenedDate cannot be hidden through the API (see the writer): it is
            // not part of what a view shows on purpose.
            .Where(column => column["hidden"] is null && column["property"]!.GetValue<string>() != "lastOpenedDate")
            .Select(column => column["property"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();

    static JsonObject Settings(FakeHomeAnytypeServer target, string typeKey) =>
        (JsonObject)target.TypeStructure(typeKey)!["type_settings"]!;

    static string CreatedId(FakeHomeAnytypeServer target, string type, string name) =>
        target.Objects.Single(value =>
            value["type"]!["key"]!.GetValue<string>() == type && value["name"]!.GetValue<string>() == name)["id"]!.GetValue<string>();

    [Fact]
    public async Task TemplatesAreCreatedOnTheNewKeysAndTheirIdsFollow()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        Assert.Equal(3, run.Outcome.Report.Written["templates"]);
        Assert.Empty(run.Outcome.Report.Refused);
        JsonObject room = target.Templates().Single(template => template["template_for"]!.GetValue<string>() == "room");
        JsonObject properties = (JsonObject)room["properties"]!;
        Assert.Equal(["name", "zone", "code"], properties.Select(pair => pair.Key));
        Assert.Equal(CreatedId(target, "zone", "Étage fictif"), properties["zone"]![0]!.GetValue<string>());
        JsonArray blocks = (JsonArray)room["blocks"]!;
        Assert.DoesNotContain(blocks.OfType<JsonObject>(), block => block["property"]?.GetValue<string>() == "aisle");
        JsonObject dataview = blocks.OfType<JsonObject>().Single(block => block["type"]!.GetValue<string>() == "dataview");
        Assert.Equal("type-product", dataview["object_id"]!.GetValue<string>());

        JsonObject zone = target.Templates().Single(template => template["template_for"]!.GetValue<string>() == "zone");
        Assert.Equal(
            "_filter_template_1_",
            zone["blocks"]![0]!["views"]![0]!["filters"]![0]!["value"]![0]!.GetValue<string>());
        Assert.Equal("zone", zone["blocks"]![0]!["views"]![0]!["filters"]![0]!["property"]!.GetValue<string>());
        Assert.Equal(2, run.Outcome.Plan.TemplateIds.Count - 1);
    }

    [Fact]
    public async Task TypeSettingsFollowTheSourceWithTheMappedTemplate()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        JsonObject product = Settings(target, "product");
        Assert.Equal("kanban", product["default_view"]!.GetValue<string>());
        Assert.Null(product["default_template"]);
        JsonObject room = Settings(target, "room");
        Assert.Equal("list", room["default_view"]!.GetValue<string>());
        Assert.Equal(run.Outcome.Plan.TemplateIds[OldTemplateRoom], room["default_template"]!.GetValue<string>());
        Assert.Equal(run.Outcome.Plan.TemplateIds[OldTemplateDevice], Settings(target, "device")["default_template"]!.GetValue<string>());
        Assert.Null(Settings(target, "worksite")["default_view"]);
    }

    [Fact]
    public async Task SectionsAreSetOnTheWholeListAndLeaveWhatTheSourceDidNotNameAsItWas()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        await RunAsync(source, target);

        JsonArray definitions = (JsonArray)Settings(target, "product")["property_definitions"]!;
        Assert.Equal(
            ["featured", "hidden", null, null, null],
            definitions.OfType<JsonObject>().Select(entry => entry["section"]?.GetValue<string>()));
        Assert.Equal(
            ["product_category", "needed", "quantity", "about", "archived"],
            definitions.OfType<JsonObject>().Select(entry => entry["property"]!.GetValue<string>()));
        // The full list was sent back, not the two entries the source named.
        JsonObject patch = JsonNode.Parse(target.Requests.Last(request =>
            request.Method == "PATCH" && request.Path.EndsWith("/types/product", StringComparison.Ordinal)
            && request.Body.Contains("property_definitions", StringComparison.Ordinal)).Body)!.AsObject();
        Assert.Equal(5, patch["property_definitions"]!.AsArray().Count);
    }

    [Fact]
    public async Task EverySourceViewIsInsertedAndTheViewsTheTargetHeldAreDeleted()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        JsonArray views = Views(target, "product");
        Assert.Equal(2, views.Count);
        Assert.DoesNotContain(views, view => view!["id"]!.GetValue<string>() == "default");
        Assert.Equal(run.Outcome.Plan.ViewIds[HomeStructurePlan.ViewKey("src-type-errand", "vE1")], views[0]!["id"]!.GetValue<string>());
        Assert.Equal("All", views[0]!["name"]!.GetValue<string>());
        Assert.Equal("Par catégorie", views[1]!["name"]!.GetValue<string>());
        Assert.Equal("kanban", views[1]!["type"]!.GetValue<string>());
        Assert.Equal("product_category", views[1]!["group_by"]!.GetValue<string>());
        Assert.Equal("small", views[1]!["card_size"]!.GetValue<string>());

        // aisle left: the sort and the filter on it are gone, the others stay.
        Assert.Equal(
            ["last_modified_date"], views[0]!["sorts"]!.AsArray().Select(sort => sort!["property"]!.GetValue<string>()));
        Assert.Equal(
            ["needed"], views[0]!["filters"]!.AsArray().Select(filter => filter!["property"]!.GetValue<string>()));
        Assert.Equal(["name", "product_category"], Visible(views[0]));
        Assert.Equal(["name", "product_category", "quantity"], Visible(views[1]));
        Assert.Equal("sum", views[1]!["columns"]!.AsArray().Single(column => column!["property"]!.GetValue<string>() == "quantity")!["aggregation"]!.GetValue<string>());
        Assert.Equal(run.Outcome.Plan.ViewIds[HomeStructurePlan.ViewKey("src-type-errand", "vE2")], views[1]!["id"]!.GetValue<string>());
        Assert.Equal(18, run.Outcome.Report.Written["type_views"]);
    }

    [Fact]
    public async Task AViewFilterOnAnObjectNamesTheNewObjectAndAnUnknownIdIsDropped()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        await RunAsync(source, target);

        JsonObject view = (JsonObject)Views(target, "room")[0]!;
        JsonObject filter = (JsonObject)view["filters"]![0]!;
        Assert.Equal("zone", filter["property"]!.GetValue<string>());
        Assert.Equal([CreatedId(target, "zone", "Étage fictif")], filter["value"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("zone", view["group_by"]!.GetValue<string>());
        Assert.Equal(["zone", "name"], view["sorts"]!.AsArray().Select(sort => sort!["property"]!.GetValue<string>()));
        Assert.Equal(["code", "name", "zone"], Visible(view));
        Assert.Equal("list", view["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ADroppedPropertyLeavesItsColumnAndItsSortOnly()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        JsonObject view = (JsonObject)Views(target, "device")[0]!;
        Assert.Equal(["purchase_date"], view["sorts"]!.AsArray().Select(sort => sort!["property"]!.GetValue<string>()));
        Assert.Equal(["manufacturer", "name"], Visible(view));
        Assert.DoesNotContain(
            ((JsonArray)view["columns"]!).OfType<JsonObject>(), column => column["property"]!.GetValue<string>() == "power_watts");
        Assert.True(run.Outcome.Report.Dropped["column"] >= 4);
        Assert.True(run.Outcome.Report.Dropped["sort"] >= 3);
    }

    [Fact]
    public async Task ASelectFilterOnAWorksiteViewReadsTheOptionThroughTheManifest()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        await RunAsync(source, target);

        JsonArray views = Views(target, "worksite");
        Assert.Equal(4, views.Count);
        JsonObject filter = (JsonObject)views[2]!["filters"]![0]!;
        Assert.Equal(["In progress"], filter["value"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("state", views[1]!["group_by"]!.GetValue<string>());
        // estimated_budget is attached to the worksite: its sum stays.
        Assert.Equal(["estimated_budget", "name"], Visible(views[3]));
    }

    [Fact]
    public async Task ASectionCollectionTakesTheViewsOfItsNamesake()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        JsonArray views = target.CollectionViews("target-section")!;
        Assert.Equal(2, views.Count);
        Assert.Equal("All", views[0]!["name"]!.GetValue<string>());
        Assert.Equal(["name", "type"], Visible(views[0]));
        Assert.Equal("Par nom", views[1]!["name"]!.GetValue<string>());
        Assert.Equal("list", views[1]!["type"]!.GetValue<string>());
        Assert.Equal(["manufacturer", "name"], Visible(views[1]));
        Assert.Equal(2, run.Outcome.Report.Written["collection_views"]);
    }

    [Fact]
    public async Task WidgetsKeepTheirOrderAndPointAtTheNewTargetsAndViews()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        IReadOnlyList<JsonObject> widgets = target.Widgets();
        Assert.Equal(5, widgets.Count);
        Assert.All(widgets, widget => Assert.Equal("space", widget["scope"]!.GetValue<string>()));

        Assert.Equal("tgt-type-product", widgets[0]["target"]!.GetValue<string>());
        Assert.Equal("view", widgets[0]["layout"]!.GetValue<string>());
        Assert.Equal(6, widgets[0]["limit"]!.GetValue<int>());
        Assert.Equal(Views(target, "product")[1]!["id"]!.GetValue<string>(), widgets[0]["view_id"]!.GetValue<string>());

        Assert.Equal("target-section", widgets[1]["target"]!.GetValue<string>());
        Assert.Equal("list", widgets[1]["layout"]!.GetValue<string>());
        Assert.Equal(target.CollectionViews("target-section")![1]!["id"]!.GetValue<string>(), widgets[1]["view_id"]!.GetValue<string>());

        Assert.Equal(CreatedId(target, "room", "Pièce fictive"), widgets[2]["target"]!.GetValue<string>());
        Assert.Equal("link", widgets[2]["layout"]!.GetValue<string>());
        Assert.Null(widgets[2]["limit"]);

        Assert.Equal("tgt-type-room", widgets[3]["target"]!.GetValue<string>());
        Assert.Equal(Views(target, "room")[0]!["id"]!.GetValue<string>(), widgets[3]["view_id"]!.GetValue<string>());

        // The view the source named is not one that was carried: the widget
        // stays, on the collection's default view.
        Assert.Equal("target-section", widgets[4]["target"]!.GetValue<string>());
        Assert.Null(widgets[4]["view_id"]);

        Assert.Equal(1, run.Outcome.Report.Dropped["widget"]);
        Assert.Equal(1, run.Outcome.Report.Dropped["widget_view"]);
        Assert.Equal(5, run.Outcome.Report.Written["widgets"]);
    }

    [Fact]
    public async Task ARefusedViewCostsThatViewOnlyAndIsCountedAndDescribedBesideTheSheet()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        target.FailNext(request => request.Method == "PATCH"
            && request.Path.EndsWith("/types/device", StringComparison.Ordinal)
            && request.Body.Contains("insert_view", StringComparison.Ordinal));

        Run run = await RunAsync(source, target);

        Assert.Equal(1, run.Outcome.Report.Refused["type_views"]);
        Assert.Equal(17, run.Outcome.Report.Written["type_views"]);
        Assert.Equal(3, run.Outcome.Report.Written["templates"]);
        Assert.Equal(5, run.Outcome.Report.Written["widgets"]);
        string details = File.ReadAllText(run.DetailPath);
        Assert.Contains("type device", details);
        Assert.Contains("injected failure", details);
        Assert.Contains("aisle", details);
    }

    [Fact]
    public async Task ARefusedDeletionCostsThatViewOnlyAndIsCounted()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        target.FailNext(request => request.Method == "PATCH"
            && request.Path.EndsWith("/types/device", StringComparison.Ordinal)
            && request.Body.Contains("delete_view", StringComparison.Ordinal));

        Run run = await RunAsync(source, target);

        Assert.Equal(1, run.Outcome.Report.Refused["view_delete"]);
        Assert.Equal(18, run.Outcome.Report.Written["type_views"]);
        // The default view stayed beside the inserted one; nothing else was lost.
        Assert.Equal(2, Views(target, "device").Count);
        Assert.Single(Views(target, "product"), view => view!["name"]!.GetValue<string>() == "All");
    }

    // 2026-11-09: update_view with `set` on a view that existed before corrupts
    // its sorts on the real server a few seconds later. The writer inserts.
    [Fact]
    public async Task TheWriterNeverSendsASetOnAViewThatExistedBefore()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        await RunAsync(source, target);

        var ops = target.Requests
            .Where(request => request.Method == "PATCH" && request.Body.Contains("\"ops\"", StringComparison.Ordinal))
            .SelectMany(request => JsonNode.Parse(request.Body)!["ops"]!.AsArray().OfType<JsonObject>())
            .ToArray();
        Assert.NotEmpty(ops);
        Assert.DoesNotContain(ops, op => op["op"]!.GetValue<string>() == "update_view" && op["set"] is not null);
        Assert.Contains(ops, op => op["op"]!.GetValue<string>() == "delete_view");
    }

    // 2026-11-09: hiding lastOpenedDate appends a duplicate column on the real
    // server, so no request may name it; each view where it stays is counted.
    [Fact]
    public async Task LastOpenedDateIsNeverSentAndEachViewShowingItIsCounted()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        Run run = await RunAsync(source, target);

        Assert.DoesNotContain(target.Requests, request =>
            request.Body.Contains("lastOpenedDate", StringComparison.Ordinal)
            || request.Body.Contains("last_opened_date", StringComparison.Ordinal));
        // 18 type views and 2 collection views were inserted, each showing it.
        Assert.Equal(20, run.Outcome.Report.Dropped["unhideable_column"]);
        Assert.All(Views(target, "product"), view =>
            Assert.Single(((JsonArray)view!["columns"]!).OfType<JsonObject>(),
                column => column["property"]!.GetValue<string>() == "lastOpenedDate"));
    }

    [Fact]
    public async Task TheFakeAppendsADuplicateWhenLastOpenedDateIsHidden()
    {
        using FakeHomeAnytypeServer target = Target();
        var api = new AnytypeApiClient(target.Credentials);
        await api.V2.SendJsonAsync(System.Net.Http.HttpMethod.Patch, "types/room", new JsonObject
        {
            ["ops"] = new JsonArray(new JsonObject
            {
                ["op"] = "insert_view", ["name"] = "Test",
                ["columns"] = new JsonObject { ["lastOpenedDate"] = new JsonObject { ["hidden"] = true } },
            }),
        }, null, target.SpaceId, Ct);

        JsonObject view = (JsonObject)Views(target, "room")[1]!;
        JsonObject[] entries = ((JsonArray)view["columns"]!).OfType<JsonObject>()
            .Where(column => column["property"]!.GetValue<string>() == "lastOpenedDate").ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry["hidden"] is null);
    }

    [Fact]
    public async Task ARefusedTemplateLeavesItsDefaultTemplateUnsetAndTheRestWritten()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();
        target.FailNext(request => request.Method == "POST"
            && request.Path.EndsWith("/templates", StringComparison.Ordinal)
            && request.Body.Contains("appareil fictif", StringComparison.Ordinal));

        Run run = await RunAsync(source, target);

        Assert.Equal(1, run.Outcome.Report.Refused["templates"]);
        Assert.Equal(2, run.Outcome.Report.Written["templates"]);
        Assert.Null(Settings(target, "device")["default_template"]);
        Assert.True(run.Outcome.Report.Dropped["id"] >= 1);
        Assert.NotNull(Settings(target, "room")["default_template"]);
    }

    [Fact]
    public async Task NothingIsWrittenOnTheSource()
    {
        using FakeHomeAnytypeServer source = Source();
        using FakeHomeAnytypeServer target = Target();

        await RunAsync(source, target);

        Assert.DoesNotContain(source.Requests, request => request.Method != "GET"
            && request.Path.StartsWith("/v2/", StringComparison.Ordinal));
        Assert.DoesNotContain(target.Requests, request => request.Path.StartsWith("/v2/spaces/source-space", StringComparison.Ordinal));
    }
}
