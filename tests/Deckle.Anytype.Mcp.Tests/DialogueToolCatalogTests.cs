using System.Text.Json.Nodes;
using Deckle.Anytype.Api;
using Deckle.Anytype.Dialogues;
using Deckle.Anytype.Gestures;
using Deckle.Anytype.Mcp.Tools;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class DialogueToolCatalogTests
{
    static readonly string[] ExpectedToolNames =
    {
        "dialogue_create", "dialogue_post", "dialogue_read",
    };

    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        var client = new AnytypeApiClient(credentials);
        var resolver = new NameResolver(client);

        return DialogueToolCatalog.Build(new DialogueGestures(client, resolver));
    }

    [Fact]
    public void BuildExposesOnlyTheDialogueTools()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.Equal(3, names.Length);
        Assert.Equal(
            ExpectedToolNames.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public void EveryInputSchemaIsAnObjectSchemaForbiddingExtraProperties()
    {
        foreach (ToolDescriptor tool in BuildCatalog())
        {
            JsonObject schema = tool.InputSchema;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.False(schema["additionalProperties"]!.GetValue<bool>(),
                $"Tool '{tool.Name}' schema must forbid additional properties.");
        }
    }
}
