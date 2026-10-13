using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// The client is real but built from dummy credentials: Build does no I/O, so the
// catalog materializes without a network call.
[Trait("Category", "unit")]
public class ManagementToolCatalogTests
{
    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        var client = new AnytypeApiClient(credentials);
        var resolver = new NameResolver(client);

        return ManagementToolCatalog.Build(new ManagementGestures(client, resolver));
    }

    [Fact]
    public void BuildExposesExactlyTheDeleteTool()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "delete" }, names);
    }

    [Fact]
    public void DeleteSchemaRequiresTargetAndForbidsExtraProperties()
    {
        ToolDescriptor delete = BuildCatalog().Single();
        JsonObject schema = delete.InputSchema;

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var required = Assert.IsType<JsonArray>(schema["required"]);
        Assert.Contains(required, n => n!.GetValue<string>() == "target");
        Assert.Equal(ToolEffect.Destructive, delete.Execution.Effect);
        Assert.Equal(AmbiguousOutcomePolicy.VerifyBeforeRetry, delete.Execution.AmbiguousOutcome);
        Assert.True(delete.Execution.RequiresStableTarget);
        Assert.Equal(ToolChangeKind.Destructive, delete.Execution.Change);
    }
}
