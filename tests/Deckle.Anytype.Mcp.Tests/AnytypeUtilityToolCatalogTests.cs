using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class AnytypeUtilityToolCatalogTests
{
    private static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        var api = new AnytypeApiClient(new AnytypeCredentials(
            "http://localhost:31009", "2025-05-20", "dummy-key", "dummy-space"));
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = "dummy-space",
            });
        var resolver = new NameResolver(api);
        return AnytypeUtilityToolCatalog.Build(
            new CollectionMembershipGestures(api, aliases, resolver),
            new SelectValueGestures(api, aliases, resolver));
    }

    [Fact]
    public void BuildExposesOnlyTheTwoBoundedUtilities()
    {
        string[] names = BuildCatalog().Select(tool => tool.Name).OrderBy(name => name).ToArray();

        Assert.Equal(["anytype_collection_add", "anytype_select_set"], names);
    }

    [Fact]
    public void EveryInputSchemaIsClosedAndRequiresNonEmptyArrays()
    {
        foreach (ToolDescriptor tool in BuildCatalog())
        {
            Assert.Equal("object", tool.InputSchema["type"]!.GetValue<string>());
            Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
        }

        ToolDescriptor collection = BuildCatalog().Single(tool => tool.Name == "anytype_collection_add");
        JsonObject properties = Assert.IsType<JsonObject>(collection.InputSchema["properties"]);
        JsonObject objects = Assert.IsType<JsonObject>(properties["objects"]);
        Assert.Equal(1, objects["minItems"]!.GetValue<int>());
        Assert.True(objects["uniqueItems"]!.GetValue<bool>());
        Assert.Equal(AmbiguousOutcomePolicy.Uncertain, collection.Execution.AmbiguousOutcome);
        Assert.Equal(
            AmbiguousOutcomePolicy.SafeToRetry,
            BuildCatalog().Single(tool => tool.Name == "anytype_select_set").Execution.AmbiguousOutcome);
        Assert.Equal(ToolChangeKind.Additive, collection.Execution.Change);
        Assert.Equal(
            ToolChangeKind.Overwriting,
            BuildCatalog().Single(tool => tool.Name == "anytype_select_set").Execution.Change);
    }
}
