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

        // The fragment is the delete contract the model reads, so match the constant.
        Assert.Contains(ManagementToolCatalog.Instructions, descriptor.Instructions);
    }

    [Fact]
    public void ProjectManagementWithoutManagementMountsPmToolsAndNoDelete()
    {
        var (tools, descriptor) = McpToolset.Build(
            DummyClient(), ToolProfile.ProjectManagement, management: false);
        var names = Names(tools);

        Assert.Contains("open_report", names);
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
        Assert.DoesNotContain("delete", names);
        Assert.Contains("schema administration", descriptor.Instructions);
    }

    static readonly string[] SpacePlanTools =
        ["space_plan_preview", "space_plan_apply", "space_plan_read", "create_type_preview", "create_type"];

    [Fact]
    public void SchemaAdminMountsTheSpacePlanToolsWithUniqueNames()
    {
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dev"] = "dummy-space" });
        var (tools, _) = McpToolset.Build(DummyClient(), ToolProfile.SchemaAdmin, management: false, aliases);
        var names = Names(tools);

        foreach (string tool in SpacePlanTools) Assert.Contains(tool, names);
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectManagementNeverMountsTheSpacePlanTools(bool management)
    {
        var (tools, _) = McpToolset.Build(DummyClient(), ToolProfile.ProjectManagement, management);
        foreach (string tool in SpacePlanTools) Assert.DoesNotContain(tool, Names(tools));
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
