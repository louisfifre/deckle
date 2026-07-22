using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for the composition seam McpToolset.Build. Like the catalog tests,
// the client is built from dummy credentials — the ctor only sets up HttpClient
// state and Build materializes descriptors without any I/O — so the whole seam
// resolves offline. These pin the profile/management matrix: which tools each
// profile mounts, that the management flag is additive on object surfaces and a
// documented no-op on the dialogue-only surface, and that every call yields a
// fresh gesture graph (distinct descriptor instances).
[Trait("Category", "unit")]
public class McpToolsetTests
{
    static AnytypeApiClient DummyClient()
    {
        // Dummy credentials: no request is ever made, only HttpClient base-address
        // and header state is set (same pattern as the catalog tests).
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

        // The PM surface is present…
        Assert.Contains("session_start", names);
        Assert.Contains("create_task", names);
        Assert.Contains("create_document", names);
        // …the management catalog is mounted additively…
        Assert.Contains("delete", names);
        // …and no dialogue tool leaked into the PM profile.
        Assert.DoesNotContain("dialogue_create", names);

        // The management instructions are appended so the model learns the delete
        // contract; the fragment is the contract, so match against the constant.
        Assert.Contains(ManagementToolCatalog.Instructions, descriptor.Instructions);
    }

    [Fact]
    public void AllWithoutManagementMountsPmAndDialogueToolsAndNoDelete()
    {
        var (tools, _) = McpToolset.Build(DummyClient(), ToolProfile.All, management: false);
        var names = Names(tools);

        // The All profile is the union of PM and dialogue surfaces…
        Assert.Contains("session_start", names);
        Assert.Contains("dialogue_create", names);
        // …with no management tool, since the flag is off.
        Assert.DoesNotContain("delete", names);
    }

    [Fact]
    public void DialoguesWithManagementMountsNoDeleteTheFlagIsANoOp()
    {
        // Documented behaviour: the Dialogues-only surface has no object to delete,
        // so the management flag is a deliberate no-op there rather than an error.
        var (tools, descriptor) = McpToolset.Build(
            DummyClient(), ToolProfile.Dialogues, management: true);
        var names = Names(tools);

        Assert.Contains("dialogue_create", names);
        Assert.DoesNotContain("delete", names);
        // No management tool means the delete contract is not appended either.
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
        // The gesture graph is session-scoped, so every Build must rebuild it: two
        // calls must not share a descriptor instance, or two sessions would share the
        // current-report default that log targets.
        var client = DummyClient();
        var (first, _) = McpToolset.Build(client, ToolProfile.ProjectManagement, management: false);
        var (second, _) = McpToolset.Build(client, ToolProfile.ProjectManagement, management: false);

        // Same names, but no descriptor object is reused across the two builds.
        Assert.Equal(Names(first), Names(second));
        foreach (ToolDescriptor tool in first)
            Assert.DoesNotContain(tool, second);
    }
}
