using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public sealed class HomeTypedToolCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void BuildExposesTwoStrictToolsPerCanonicalHomeType()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeTypedToolCatalog.Build(
            () => throw new InvalidOperationException("handlers must stay lazy"));

        Assert.Equal(28, tools.Count);
        Assert.Equal(14, tools.Select(tool => tool.Name[..^7]).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tools, tool => Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>()));
        Assert.All(tools, tool => Assert.Equal(tool.Name.EndsWith("_create", StringComparison.Ordinal)
            ? ToolChangeKind.Additive
            : ToolChangeKind.Overwriting, tool.Execution.Change));
    }

    [Fact]
    public void EveryCreateTakesABoundedBatchOfStrictItems()
    {
        IReadOnlyList<ToolDescriptor> creates = HomeTypedToolCatalog.Build(() => throw new InvalidOperationException())
            .Where(tool => tool.Name.EndsWith("_create", StringComparison.Ordinal)).ToArray();

        Assert.Equal(14, creates.Count);
        Assert.All(creates, tool =>
        {
            JsonObject items = tool.InputSchema["properties"]!["items"]!.AsObject();
            Assert.Equal(1, items["minItems"]!.GetValue<int>());
            Assert.Equal(100, items["maxItems"]!.GetValue<int>());
            Assert.False(items["items"]!["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(new[] { "items" }, tool.InputSchema["required"]!.AsArray().Select(value => value!.GetValue<string>()));
        });
    }

    [Theory]
    [InlineData("device_create", true)]
    [InlineData("idea_create", true)]
    [InlineData("zone_create", true)]
    [InlineData("room_create", false)]
    [InlineData("circuit_create", false)]
    public void OnlyFreeTitledItemsOfferForce(string tool, bool offersForce)
    {
        JsonObject item = CreateItemSchema(HomeTypedToolCatalog.Build(() => throw new InvalidOperationException()), tool);

        Assert.Equal(offersForce, item["properties"]!.AsObject().ContainsKey("force"));
    }

    [Fact]
    public void PointSchemasKeepIdentityAndDerivedFieldsOutOfFlatProperties()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeTypedToolCatalog.Build(() => throw new InvalidOperationException());
        JsonObject create = CreateItemSchema(tools, "point_create");
        JsonObject update = tools.Single(tool => tool.Name == "point_update").InputSchema;

        JsonObject createProperties = Assert.IsType<JsonObject>(create["properties"]);
        JsonObject updateProperties = Assert.IsType<JsonObject>(update["properties"]);
        Assert.Contains("code", createProperties.Select(pair => pair.Key));
        Assert.Contains("name", createProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("installed_in", createProperties.Select(pair => pair.Key));
        // category and room only mint a code; they are not free properties.
        Assert.Contains("PS", createProperties["category"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.DoesNotContain("code", create["required"]?.AsArray().Select(value => value!.GetValue<string>()) ?? []);
        Assert.DoesNotContain("code", updateProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("installed_in", updateProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("category", updateProperties.Select(pair => pair.Key));
    }

    [Fact]
    public void ComponentPlantAndTodoKeepTheirShorthandsWithoutAPropertiesMap()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeTypedToolCatalog.Build(() => throw new InvalidOperationException());
        JsonObject component = CreateItemSchema(tools, "component_create");
        JsonObject plant = CreateItemSchema(tools, "plant_create");
        JsonObject todo = CreateItemSchema(tools, "todo_create");

        Assert.Contains("system", Assert.IsType<JsonObject>(component["properties"]).Select(pair => pair.Key));
        Assert.DoesNotContain("part_of", Assert.IsType<JsonObject>(component["properties"]).Select(pair => pair.Key));
        Assert.Contains("room", Assert.IsType<JsonObject>(plant["properties"]).Select(pair => pair.Key));
        Assert.Contains("worksite", Assert.IsType<JsonObject>(todo["properties"]).Select(pair => pair.Key));
        Assert.All(tools.Where(tool => tool.Name.EndsWith("_create", StringComparison.Ordinal)), tool =>
            Assert.DoesNotContain("properties", CreateItemSchema(tools, tool.Name)["properties"]!.AsObject().Select(pair => pair.Key)));

        JsonObject worksiteSchema = Assert.IsType<JsonObject>(
            Assert.IsType<JsonObject>(todo["properties"])["worksite"]);
        Assert.Contains("oneOf", worksiteSchema.Select(pair => pair.Key));
    }

    [Fact]
    public void UpdateRelationSchemaAdvertisesReplacementAndAddRemoveForms()
    {
        JsonObject point = ItemSchema(HomeTypedToolCatalog.Build(() => throw new InvalidOperationException()), "point_update");
        JsonObject controls = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(point["properties"])["controls"]);
        JsonArray forms = Assert.IsType<JsonArray>(controls["oneOf"]);
        Assert.Equal(3, forms.Count);
        JsonObject edit = Assert.IsType<JsonObject>(forms[2]);
        Assert.False(edit.ContainsKey("required"));
        Assert.False(edit["additionalProperties"]?.GetValue<bool>() ?? true);
        Assert.Contains("add", Assert.IsType<JsonObject>(edit["properties"]).Select(pair => pair.Key));
        Assert.Contains("remove", Assert.IsType<JsonObject>(edit["properties"]).Select(pair => pair.Key));
    }

    [Fact]
    public async Task UpdateRelationRejectsMalformedEditBeforeResolvingHome()
    {
        ToolDescriptor tool = HomeTypedToolCatalog.Build(
            () => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "point_update");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.Handler(new JsonObject
            {
                ["object"] = "Fictional point",
                ["controls"] = new JsonObject { ["replace"] = new JsonArray("bad") },
            }, Ct));

        Assert.Contains("controls", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandlersRejectUnknownFieldsBeforeResolvingHome()
    {
        ToolDescriptor tool = HomeTypedToolCatalog.Build(
            () => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "device_create");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.Handler(new JsonObject { ["unknown"] = "value" }, Ct));

        Assert.Contains("Unknown field", error.Message);
    }

    [Fact]
    public async Task TypedPointCreateSendsFlatValuesThroughTheExistingGesture()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Fictional room"));
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "point_create");

        await tool.Handler(Batch(new JsonObject
        {
            ["code"] = "ZZ-PS01",
            ["name"] = "Fictional socket",
            ["outlet_count"] = 2,
        }), Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Point, body["type_key"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.Equal(2, Entry(properties, HomeSchema.Properties.OutletCount)["number"]!.GetValue<double>());
    }

    [Fact]
    public async Task TypedDeviceCreateKeepsItsInitialBody()
    {
        using var server = new FakeHomeAnytypeServer();
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "device_create");

        await tool.Handler(Batch(new JsonObject
        {
            ["name"] = "Fictional device",
            ["text"] = "Label details",
            ["model_ref"] = "MODEL-1",
        }), Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal("Label details", body["body"]!.GetValue<string>());
        Assert.Equal("MODEL-1", Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.ModelRef)["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task TypedTodoUpdateRejectsADeviceOnlyFieldBeforeWriting()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Todo("todo-1", "Fictional task", null, done: false));
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "todo_update");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.Handler(new JsonObject { ["object"] = "Fictional task", ["model_ref"] = "MODEL-1" }, Ct));

        Assert.Contains("Unknown field", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task TypedTodoCreateWritesItsWorksiteRelationOnce()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Worksite("site-1", "Fictional worksite"));
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "todo_create");

        await tool.Handler(Batch(new JsonObject
        {
            ["name"] = "Fictional task",
            ["worksite"] = "Fictional worksite",
        }), Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        JsonArray references = Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.Worksite)["objects"]);
        Assert.Single(references);
        Assert.Equal("site-1", references[0]!.GetValue<string>());
    }

    [Fact]
    public async Task TypedTodoCreateAttachesATaskToTwoWorksites()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Fictional worksite one"),
            FakeHomeAnytypeServer.Worksite("site-2", "Fictional worksite two"));
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "todo_create");

        await tool.Handler(Batch(new JsonObject
        {
            ["name"] = "Fictional shared task",
            ["worksite"] = new JsonArray("Fictional worksite one", "site-2"),
        }), Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal(
            new[] { "site-1", "site-2" },
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.Worksite)["objects"]!
                .AsArray().Select(value => value!.GetValue<string>()));
    }

    [Fact]
    public async Task AMalformedSecondItemStopsTheBatchBeforeHomeIsRead()
    {
        ToolDescriptor tool = HomeTypedToolCatalog.Build(
            () => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "device_create");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => tool.Handler(Batch(
            new JsonObject { ["name"] = "Fictional device one" },
            new JsonObject { ["name"] = "Fictional device two", ["notes"] = "gone" }), Ct));

        Assert.Contains("items[1]", error.Message);
        Assert.Contains("Unknown field 'notes'", error.Message);
    }

    [Fact]
    public async Task ASecondItemTheSpaceRefusesStopsTheBatchBeforeAnyWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Fictional room"));
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "device_create");

        await Assert.ThrowsAsync<InvalidOperationException>(() => tool.Handler(Batch(
            new JsonObject { ["name"] = "Fictional device one", ["installed_in"] = "ZZ" },
            new JsonObject { ["name"] = "Fictional device two", ["installed_in"] = "Fictional nowhere" }), Ct));

        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ABatchCreatesEveryItemAndListsEachWithItsId()
    {
        using var server = new FakeHomeAnytypeServer();
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "utensil_create");

        ToolOutput output = await tool.Handler(Batch(
            new JsonObject { ["name"] = "Fictional whisk" },
            new JsonObject { ["name"] = "Fictional ladle" }), Ct);

        Assert.Equal(2, server.Requests.Count(request => request.Method == "POST"));
        Assert.Contains("utensil · Fictional whisk (id created-1)", output.Text);
        Assert.Contains("utensil · Fictional ladle (id created-2)", output.Text);
    }

    [Fact]
    public async Task TheItemsArrayIsBounded()
    {
        ToolDescriptor tool = HomeTypedToolCatalog.Build(
            () => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "device_create");

        await Assert.ThrowsAsync<ArgumentException>(() => tool.Handler(Batch(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => tool.Handler(Batch(
            Enumerable.Range(1, 101).Select(n => new JsonObject { ["name"] = $"Fictional device {n}" }).ToArray()), Ct));
    }

    [Fact]
    public async Task TypedTodoUpdateRefusesADeviceTargetBeforePatch()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(new JsonObject
        {
            ["id"] = "device-1",
            ["name"] = "Fictional device",
            ["type"] = new JsonObject { ["key"] = HomeSchema.Types.Device },
            ["properties"] = new JsonArray(),
        });
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "todo_update");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.Handler(new JsonObject
            {
                ["object"] = "Fictional device",
                ["definition_of_done"] = "Wrong target",
            }, Ct));

        Assert.Contains("todo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new Deckle.Anytype.AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    private static JsonObject ItemSchema(IReadOnlyList<ToolDescriptor> tools, string name) =>
        tools.Single(tool => tool.Name == name).InputSchema;

    // A create takes a batch: its item schema sits under items.items.
    private static JsonObject CreateItemSchema(IReadOnlyList<ToolDescriptor> tools, string name) =>
        tools.Single(tool => tool.Name == name).InputSchema["properties"]!["items"]!["items"]!.AsObject();

    private static JsonObject Batch(params JsonObject[] items) =>
        new() { ["items"] = new JsonArray(items.Select(item => (JsonNode?)item).ToArray()) };

    private static JsonObject Entry(JsonArray properties, string key) =>
        properties.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);
}
