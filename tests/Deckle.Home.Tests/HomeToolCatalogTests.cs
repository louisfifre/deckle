using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public class HomeToolCatalogTests
{
    private static readonly string[] ExpectedNames =
    [
        "create", "update", "get", "search", "delete",
        "component_create", "plant_create",
        "worksite_create", "todo_create", "complete", "worksite_overview",
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
        }

        foreach (string toolName in new[] { "create", "update" })
        {
            JsonObject schema = tools.Single(tool => tool.Name == toolName).InputSchema;
            JsonObject properties = (JsonObject)schema["properties"]!;
            JsonObject items = (JsonObject)((JsonObject)properties["items"]!)["items"]!;
            Assert.False(items["additionalProperties"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void CreateAndUpdateExposeCollectionMembershipOutsideProperties()
    {
        IReadOnlyList<ToolDescriptor> tools = HomeToolCatalog.Build(
            () => throw new InvalidOperationException("handlers are not invoked"));

        JsonObject createItem = BatchItemSchema(tools, "create");
        JsonObject createProperties = Assert.IsType<JsonObject>(createItem["properties"]);
        Assert.Contains("collections", createProperties.Select(pair => pair.Key));

        JsonObject updateItem = BatchItemSchema(tools, "update");
        JsonObject updateProperties = Assert.IsType<JsonObject>(updateItem["properties"]);
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
            ["create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["update"] = AmbiguousOutcomePolicy.Uncertain,
            ["get"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["search"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["delete"] = AmbiguousOutcomePolicy.VerifyBeforeRetry,
            ["component_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["plant_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["worksite_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["todo_create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["complete"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["worksite_overview"] = AmbiguousOutcomePolicy.SafeToRetry,
        };

        Assert.Equal(expected, tools.ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.AmbiguousOutcome,
            StringComparer.Ordinal));
        Assert.Equal(ToolEffect.Destructive, tools.Single(tool => tool.Name == "delete").Execution.Effect);
        var expectedChanges = new Dictionary<string, ToolChangeKind>(StringComparer.Ordinal)
        {
            ["create"] = ToolChangeKind.Additive,
            ["update"] = ToolChangeKind.Overwriting,
            ["get"] = ToolChangeKind.None,
            ["search"] = ToolChangeKind.None,
            ["delete"] = ToolChangeKind.Destructive,
            ["component_create"] = ToolChangeKind.Additive,
            ["plant_create"] = ToolChangeKind.Additive,
            ["worksite_create"] = ToolChangeKind.Additive,
            ["todo_create"] = ToolChangeKind.Additive,
            ["complete"] = ToolChangeKind.Overwriting,
            ["worksite_overview"] = ToolChangeKind.None,
        };
        Assert.Equal(expectedChanges, tools.ToDictionary(
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
