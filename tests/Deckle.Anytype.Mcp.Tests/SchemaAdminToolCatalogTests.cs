using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class SchemaAdminToolCatalogTests
{
    static readonly string[] ExpectedToolNames =
    {
        "schema_inspect_space", "schema_preview", "schema_apply",
    };

    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        var client = new AnytypeApiClient(credentials);
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = "dummy-space",
            });
        return SchemaAdminToolCatalog.Build(new SchemaAdminGestures(client, aliases));
    }

    [Fact]
    public void BuildExposesOnlySchemaAdminTools()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.Equal(
            ExpectedToolNames.OrderBy(n => n),
            names.OrderBy(n => n));
        Assert.DoesNotContain("create_task", names);
        Assert.DoesNotContain("delete", names);
    }

    [Fact]
    public void EveryInputSchemaIsStrictObjectSchema()
    {
        foreach (ToolDescriptor tool in BuildCatalog())
        {
            JsonObject schema = tool.InputSchema;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.False(schema["additionalProperties"]!.GetValue<bool>(),
                $"Tool '{tool.Name}' schema must forbid additional properties.");
        }
    }

    [Fact]
    public void PreviewManifestInputSchemaIsClosed()
    {
        ToolDescriptor preview = BuildCatalog().Single(t => t.Name == "schema_preview");

        JsonObject manifest = Assert.IsType<JsonObject>(
            Assert.IsType<JsonObject>(preview.InputSchema["properties"])["manifest"]);

        Assert.False(manifest["additionalProperties"]!.GetValue<bool>());
        JsonObject properties = Assert.IsType<JsonObject>(manifest["properties"]);
        Assert.Contains("types", properties.Select(p => p.Key));
        Assert.Contains("properties", properties.Select(p => p.Key));
    }
}
