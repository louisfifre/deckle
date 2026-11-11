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
    public void InspectSaysItRendersOptionsAndViewsAndWhatTheApiLeavesOut()
    {
        ToolDescriptor inspect = BuildCatalog().Single(t => t.Name == "schema_inspect_space");

        Assert.Contains("options (key, name, color", inspect.Description);
        Assert.Contains("views of every type and of every query or collection", inspect.Description);
        Assert.Contains("does not render", inspect.Description);
        Assert.Equal(ToolEffect.ReadOnly, inspect.Execution.Effect);
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

        JsonObject typeArray = Assert.IsType<JsonObject>(properties["types"]);
        JsonObject typeSchema = Assert.IsType<JsonObject>(typeArray["items"]);
        JsonObject typeProperties = Assert.IsType<JsonObject>(typeSchema["properties"]);
        Assert.Contains("plural_name", typeProperties.Select(p => p.Key));
        Assert.Contains("icon", typeProperties.Select(p => p.Key));

        JsonObject layout = Assert.IsType<JsonObject>(typeProperties["layout"]);
        JsonArray values = Assert.IsType<JsonArray>(layout["enum"]);
        Assert.Contains(values, node => node?.GetValue<string>() == "basic");
        Assert.Contains(values, node => node?.GetValue<string>() == "collection");
        Assert.DoesNotContain(values, node => node?.GetValue<string>() == "page");

        JsonObject icon = Assert.IsType<JsonObject>(typeProperties["icon"]);
        JsonArray iconVariants = Assert.IsType<JsonArray>(icon["oneOf"]);
        Assert.Equal(2, iconVariants.Count);
    }

    [Fact]
    public void PreviewAdvertisesAClosedStructuredOutput()
    {
        ToolDescriptor preview = BuildCatalog().Single(tool => tool.Name == "schema_preview");

        JsonObject output = Assert.IsType<JsonObject>(preview.OutputSchema);
        Assert.Equal("object", output["type"]!.GetValue<string>());
        Assert.False(output["additionalProperties"]!.GetValue<bool>());

        JsonObject properties = Assert.IsType<JsonObject>(output["properties"]);
        Assert.Equal(
            ["actions", "conflicts", "preview_id", "skipped_conflicts", "space"],
            properties.Select(property => property.Key).OrderBy(key => key).ToArray());

        JsonObject actions = Assert.IsType<JsonObject>(properties["actions"]);
        JsonObject action = Assert.IsType<JsonObject>(actions["items"]);
        Assert.False(action["additionalProperties"]!.GetValue<bool>());
    }

    // A fresh space ships status, done and due_date under English names, so the
    // first preview of the model v6 manifest plans renames. The advertised
    // output left that kind out and the host refused the whole preview
    // ("Tool output does not match the advertised JSON Schema", 2026-11-11).
    [Theory]
    [InlineData("rename", "property:status")]
    [InlineData("rename", "type:task")]
    [InlineData("create_property", "priority")]
    [InlineData("create_tag", "priority:p_0")]
    [InlineData("create_type", "project")]
    [InlineData("set_icon", "project")]
    [InlineData("set_description", "project")]
    [InlineData("attach_property", "project:status")]
    [InlineData("detach_property", "project:tag")]
    [InlineData("order_properties", "project")]
    [InlineData("create_section", "Work")]
    [InlineData("add_to_section", "Work:project")]
    public void EveryPlannedActionKindIsInTheAdvertisedOutputSchema(string kind, string key)
    {
        var result = new SchemaPreviewResult(
            new string('a', 64), "dev", [new SchemaPreviewAction(kind, key, "Name")], [], [], "digest");

        Assert.True(new McpJsonSchemaContract(
            SchemaAdminToolCatalog.PreviewOutputSchema()).Accepts(SchemaAdminToolCatalog.PreviewContent(result)));
    }

    [Fact]
    public void RealPreviewProjectionMatchesItsAdvertisedOutputSchema()
    {
        var result = new SchemaPreviewResult(
            new string('a', 64),
            "dev",
            [new SchemaPreviewAction("create_type", "project", "Project")],
            ["conflict"],
            ["skipped"],
            "digest");

        JsonObject content = SchemaAdminToolCatalog.PreviewContent(result);

        Assert.True(new McpJsonSchemaContract(
            SchemaAdminToolCatalog.PreviewOutputSchema()).Accepts(content));
        Assert.Equal(result.PreviewId, content["preview_id"]!.GetValue<string>());
        Assert.Equal(result.SpaceAlias, content["space"]!.GetValue<string>());
        JsonObject action = Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(content["actions"])[0]);
        Assert.Equal("create_type", action["kind"]!.GetValue<string>());
        Assert.Equal("project", action["key"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyCarriesTheManifestAgainAndDeclaresVerification()
    {
        ToolDescriptor apply = BuildCatalog().Single(tool => tool.Name == "schema_apply");
        JsonObject properties = Assert.IsType<JsonObject>(apply.InputSchema["properties"]);

        Assert.Contains("manifest", properties.Select(property => property.Key));
        Assert.Equal(AmbiguousOutcomePolicy.VerifyBeforeRetry, apply.Execution.AmbiguousOutcome);
        Assert.True(apply.Execution.RequiresStableTarget);
        Assert.All(
            BuildCatalog().Where(tool => tool.Name != "schema_apply"),
            tool => Assert.Equal(ToolEffect.ReadOnly, tool.Execution.Effect));
        Assert.Equal(ToolChangeKind.Additive, apply.Execution.Change);
        Assert.All(
            BuildCatalog().Where(tool => tool.Name != "schema_apply"),
            tool => Assert.Equal(ToolChangeKind.None, tool.Execution.Change));
    }

    [Fact]
    public void PreviewManifestSectionsSchemaIsClosedAndRequiresNameAndTypes()
    {
        ToolDescriptor preview = BuildCatalog().Single(t => t.Name == "schema_preview");

        JsonObject manifest = Assert.IsType<JsonObject>(
            Assert.IsType<JsonObject>(preview.InputSchema["properties"])["manifest"]);
        JsonObject properties = Assert.IsType<JsonObject>(manifest["properties"]);
        Assert.Contains("sections", properties.Select(p => p.Key));

        JsonObject sectionArray = Assert.IsType<JsonObject>(properties["sections"]);
        JsonObject sectionSchema = Assert.IsType<JsonObject>(sectionArray["items"]);
        Assert.False(sectionSchema["additionalProperties"]!.GetValue<bool>());

        JsonObject sectionProperties = Assert.IsType<JsonObject>(sectionSchema["properties"]);
        Assert.Contains("icon", sectionProperties.Select(p => p.Key));
        JsonObject icon = Assert.IsType<JsonObject>(sectionProperties["icon"]);
        JsonObject iconProperties = Assert.IsType<JsonObject>(icon["properties"]);
        JsonArray iconFormats = Assert.IsType<JsonArray>(
            Assert.IsType<JsonObject>(iconProperties["format"])["enum"]);
        Assert.Equal(["emoji"], iconFormats.Select(node => node!.GetValue<string>()));
        JsonObject types = Assert.IsType<JsonObject>(sectionProperties["types"]);
        Assert.Equal(1, types["minItems"]!.GetValue<int>());

        JsonArray required = Assert.IsType<JsonArray>(sectionSchema["required"]);
        Assert.Contains(required, node => node?.GetValue<string>() == "name");
        Assert.Contains(required, node => node?.GetValue<string>() == "types");
    }
}
