using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class McpToolsetTests
{
    static AnytypeApiClient DummyClient()
    {
        // No request is ever made: the constructor only sets HttpClient state and
        // Build materializes descriptors without I/O.
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        return new AnytypeApiClient(credentials);
    }

    static string[] Names(IReadOnlyList<ToolDescriptor> tools) =>
        tools.Select(t => t.Name).ToArray();

    [Fact]
    public void ProjectManagementWithManagementMountsPmToolsPlusDelete()
    {
        var (tools, descriptor) = McpToolset.Build(
            DummyClient(), ToolProfile.ProjectManagement, management: true);
        var names = Names(tools);

        Assert.Contains("open_report", names);
        Assert.Contains("create_task", names);
        Assert.Contains("create_document", names);
        Assert.Contains("delete", names);
        Assert.DoesNotContain("dialogue_create", names);

        // The fragment is the delete contract the model reads, so match the constant.
        Assert.Contains(ManagementToolCatalog.Instructions, descriptor.Instructions);
    }

    [Fact]
    public void AllWithoutManagementMountsPmAndDialogueToolsAndNoDelete()
    {
        var (tools, _) = McpToolset.Build(DummyClient(), ToolProfile.All, management: false);
        var names = Names(tools);

        Assert.Contains("open_report", names);
        Assert.Contains("dialogue_create", names);
        Assert.DoesNotContain("delete", names);
    }

    [Fact]
    public void DialoguesWithManagementMountsNoDeleteTheFlagIsANoOp()
    {
        // The Dialogues-only surface has no object to delete, so the management flag
        // is a deliberate no-op there rather than an error.
        var (tools, descriptor) = McpToolset.Build(
            DummyClient(), ToolProfile.Dialogues, management: true);
        var names = Names(tools);

        Assert.Contains("dialogue_create", names);
        Assert.DoesNotContain("delete", names);
        Assert.DoesNotContain(ManagementToolCatalog.Instructions, descriptor.Instructions);
    }

    [Fact]
    public void SchemaAdminMountsSchemaAndBoundedAnytypeUtilities()
    {
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = "dummy-space",
            });
        var (tools, descriptor) = McpToolset.Build(
            DummyClient(), ToolProfile.SchemaAdmin, management: true, aliases);
        var names = Names(tools);

        Assert.Contains("schema_preview", names);
        Assert.Contains("schema_apply", names);
        Assert.Contains("anytype_collection_add", names);
        Assert.Contains("anytype_select_set", names);
        Assert.DoesNotContain("create_task", names);
        Assert.DoesNotContain("dialogue_create", names);
        Assert.DoesNotContain("delete", names);
        Assert.Contains("schema administration", descriptor.Instructions);
    }

    [Fact]
    public void EachBuildYieldsFreshDescriptorInstances()
    {
        // The gesture graph is request-scoped, so every Build must rebuild it: two
        // calls must not share descriptor instances or mutable gesture state.
        var client = DummyClient();
        var (first, _) = McpToolset.Build(client, ToolProfile.ProjectManagement, management: false);
        var (second, _) = McpToolset.Build(client, ToolProfile.ProjectManagement, management: false);

        Assert.Equal(Names(first), Names(second));
        foreach (ToolDescriptor tool in first)
            Assert.DoesNotContain(tool, second);
    }
}
