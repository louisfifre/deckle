using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for ToolCatalog.Build. The gestures wrap a real AnytypeApiClient
// built from dummy credentials, but Build only constructs descriptors (the
// handlers are lazy lambdas) so no HTTP call is ever made here. These pin the
// advertised surface: exactly the 15 base named tools, each with a well-formed
// object input schema that forbids extra properties.
[Trait("Category", "unit")]
public class ToolCatalogTests
{
    static readonly string[] ExpectedToolNames =
    {
        "session_start", "log", "get", "project_overview", "create_task",
        "complete", "archive", "link", "list_projects", "search", "subtask",
        "create_project", "create_idea", "update", "replace_section",
    };

    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        // Dummy credentials: the client constructor only sets up HttpClient state
        // (base address + headers); it makes no request. ToolCatalog.Build does no
        // I/O either, so the catalog materializes without touching the network.
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        var client = new AnytypeApiClient(credentials);
        var resolver = new NameResolver(client);

        return ToolCatalog.Build(
            new SessionGestures(client, resolver),
            new TaskGestures(client, resolver),
            new ProjectGestures(client, resolver),
            new QueryGestures(client, resolver));
    }

    [Fact]
    public void BuildExposesExactlyTheFifteenNamedTools()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.Equal(15, names.Length);
        Assert.Equal(
            ExpectedToolNames.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public void BuildDoesNotExposeAnyDestructiveTool()
    {
        // delete lives only in the supervised ManagementToolCatalog; the base
        // surface must never carry it. task_done was folded into complete.
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.DoesNotContain("delete", names);
        Assert.DoesNotContain("task_done", names);
    }

    [Fact]
    public void EveryInputSchemaIsAnObjectSchemaForbiddingExtraProperties()
    {
        foreach (ToolDescriptor tool in BuildCatalog())
        {
            JsonObject schema = tool.InputSchema;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            // additionalProperties:false is the guard against unadvertised args.
            Assert.False(schema["additionalProperties"]!.GetValue<bool>(),
                $"Tool '{tool.Name}' schema must forbid additional properties.");
        }
    }
}
