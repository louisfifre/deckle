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
    public void PointSchemasKeepIdentityAndDerivedFieldsOutOfFlatProperties()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeTypedToolCatalog.Build(() => throw new InvalidOperationException());
        JsonObject create = ItemSchema(tools, "point_create");
        JsonObject update = tools.Single(tool => tool.Name == "point_update").InputSchema;

        JsonObject createProperties = Assert.IsType<JsonObject>(create["properties"]);
        JsonObject updateProperties = Assert.IsType<JsonObject>(update["properties"]);
        Assert.Contains("code", createProperties.Select(pair => pair.Key));
        Assert.Contains("name", createProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("installed_in", createProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("category", createProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("code", updateProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("installed_in", updateProperties.Select(pair => pair.Key));
        Assert.DoesNotContain("category", updateProperties.Select(pair => pair.Key));
    }

    [Fact]
    public void ComponentPlantAndTodoRetainTheirDedicatedCompatibilityAliases()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeTypedToolCatalog.Build(() => throw new InvalidOperationException());
        JsonObject component = ItemSchema(tools, "component_create");
        JsonObject plant = ItemSchema(tools, "plant_create");
        JsonObject todo = ItemSchema(tools, "todo_create");

        Assert.Contains("system", Assert.IsType<JsonObject>(component["properties"]).Select(pair => pair.Key));
        Assert.Contains("properties", Assert.IsType<JsonObject>(component["properties"]).Select(pair => pair.Key));
        Assert.DoesNotContain("part_of", Assert.IsType<JsonObject>(component["properties"]).Select(pair => pair.Key));
        Assert.Contains("room", Assert.IsType<JsonObject>(plant["properties"]).Select(pair => pair.Key));
        Assert.Contains("properties", Assert.IsType<JsonObject>(plant["properties"]).Select(pair => pair.Key));
        Assert.Contains("worksite", Assert.IsType<JsonObject>(todo["properties"]).Select(pair => pair.Key));

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

        await tool.Handler(new JsonObject
        {
            ["code"] = "ZZ-PS01",
            ["name"] = "Fictional socket",
            ["outlet_count"] = 2,
        }, Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.WireTypeKey("point"), body["type_key"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.Equal(2, Entry(properties, HomeSchema.WirePropertyKey("outlet_count"))["number"]!.GetValue<double>());
    }

    [Fact]
    public async Task TypedDeviceCreateKeepsItsInitialBody()
    {
        using var server = new FakeHomeAnytypeServer();
        ToolDescriptor tool = HomeTypedToolCatalog.Build(() => Gestures(server))
            .Single(value => value.Name == "device_create");

        await tool.Handler(new JsonObject
        {
            ["name"] = "Fictional device",
            ["text"] = "Label details",
            ["model_ref"] = "MODEL-1",
        }, Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal("Label details", body["body"]!.GetValue<string>());
        Assert.Equal("MODEL-1", Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.WirePropertyKey("model_ref"))["text"]!.GetValue<string>());
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

        await tool.Handler(new JsonObject
        {
            ["name"] = "Fictional task",
            ["worksite"] = "Fictional worksite",
        }, Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!;
        JsonArray references = Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.WirePropertyKey("worksite"))["objects"]);
        Assert.Single(references);
        Assert.Equal("site-1", references[0]!.GetValue<string>());
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
                ["notes"] = "Wrong target",
            }, Ct));

        Assert.Contains("todo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new Deckle.Anytype.AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    private static JsonObject ItemSchema(IReadOnlyList<ToolDescriptor> tools, string name) =>
        tools.Single(tool => tool.Name == name).InputSchema;

    private static JsonObject Entry(JsonArray properties, string key) =>
        properties.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);
}
