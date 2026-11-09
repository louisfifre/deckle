using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public class HomeToolCatalogTests
{
    private static readonly string[] TypeKeys =
    [
        "zone", "room", "point", "circuit", "panel", "system", "device", "component",
        "utensil", "plant", "idea", "product", "worksite", "todo",
    ];

    private static readonly string[] ExpectedNames =
    [
        "get", "search", "delete", "complete", "worksite_overview", "archive", "point_dictate",
        .. TypeKeys.SelectMany(type => new[] { type + "_create", type + "_update" }),
    ];

    [Fact]
    public void BuildExposesExactlyTheSpecifiedToolsWithoutResolvingHome()
    {
        int resolutions = 0;

        IReadOnlyList<ToolDescriptor> tools = HomeToolCatalog.Build(() =>
        {
            resolutions++;
            throw new InvalidOperationException("must stay lazy");
        });

        Assert.Equal(ExpectedNames.OrderBy(value => value), tools.Select(tool => tool.Name).OrderBy(value => value));
        Assert.DoesNotContain(tools, tool => tool.Name is "create" or "update");
        Assert.Equal(0, resolutions);
    }

    [Fact]
    public void EveryToolAndBatchItemForbidsUnknownFields()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeToolCatalog.Build(
            () => throw new InvalidOperationException("handlers are not invoked"));

        foreach (ToolDescriptor tool in tools)
        {
            Assert.Equal("object", tool.InputSchema["type"]!.GetValue<string>());
            Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
            foreach ((string _, JsonNode? field) in tool.InputSchema["properties"]!.AsObject())
            {
                // A batch is an array of objects: each is bounded and strict.
                if (field?["type"]?.GetValue<string>() != "array" || field["items"] is not JsonObject items
                    || items["type"]?.GetValue<string>() != "object") continue;
                Assert.True(field["maxItems"] is not null, $"{tool.Name}: a batch is bounded");
                Assert.False(items["additionalProperties"]!.GetValue<bool>(), tool.Name);
            }
        }
    }

    [Fact]
    public void TypedCreateAndUpdateExposeCollectionMembershipOutsideProperties()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeToolCatalog.Build(
            () => throw new InvalidOperationException("handlers are not invoked"));

        JsonObject createItem = BatchItemSchema(tools, "device_create");
        Assert.Contains("collections", Assert.IsType<JsonObject>(createItem["properties"]).Select(pair => pair.Key));

        JsonObject update = tools.Single(tool => tool.Name == "device_update").InputSchema;
        JsonObject updateProperties = Assert.IsType<JsonObject>(update["properties"]);
        Assert.Contains("add_to_collections", updateProperties.Select(pair => pair.Key));
        Assert.Contains("remove_from_collections", updateProperties.Select(pair => pair.Key));
    }

    [Fact]
    public void EveryToolDeclaresItsAmbiguousOutcomePolicy()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeToolCatalog.Build(
            () => throw new InvalidOperationException("handlers are not invoked"));
        var expected = new Dictionary<string, AmbiguousOutcomePolicy>(StringComparer.Ordinal)
        {
            ["get"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["search"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["delete"] = AmbiguousOutcomePolicy.VerifyBeforeRetry,
            ["component_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["plant_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["worksite_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["todo_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["device_update"] = AmbiguousOutcomePolicy.Uncertain,
            ["complete"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["worksite_overview"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["archive"] = AmbiguousOutcomePolicy.SafeToRetry,
        };

        Assert.Equal(expected, tools.Where(tool => expected.ContainsKey(tool.Name)).ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.AmbiguousOutcome,
            StringComparer.Ordinal));
        Assert.Equal(ToolEffect.Destructive, tools.Single(tool => tool.Name == "delete").Execution.Effect);
        var expectedChanges = new Dictionary<string, ToolChangeKind>(StringComparer.Ordinal)
        {
            ["get"] = ToolChangeKind.None,
            ["search"] = ToolChangeKind.None,
            ["delete"] = ToolChangeKind.Destructive,
            ["component_create"] = ToolChangeKind.Additive,
            ["plant_create"] = ToolChangeKind.Additive,
            ["worksite_create"] = ToolChangeKind.Additive,
            ["todo_create"] = ToolChangeKind.Additive,
            ["device_update"] = ToolChangeKind.Overwriting,
            ["complete"] = ToolChangeKind.Overwriting,
            ["worksite_overview"] = ToolChangeKind.None,
            ["archive"] = ToolChangeKind.Overwriting,
        };
        Assert.Equal(expectedChanges, tools.Where(tool => expectedChanges.ContainsKey(tool.Name)).ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.Change,
            StringComparer.Ordinal));
    }

    private static JsonObject BatchItemSchema(IReadOnlyList<ToolDescriptor> tools, string name)
    {
        JsonObject schema = tools.Single(tool => tool.Name == name).InputSchema;
        JsonObject properties = Assert.IsType<JsonObject>(schema["properties"]);
        JsonObject items = Assert.IsType<JsonObject>(properties["items"]);
        return Assert.IsType<JsonObject>(items["items"]);
    }
}
