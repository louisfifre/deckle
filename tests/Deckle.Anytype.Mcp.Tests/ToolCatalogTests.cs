using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for ToolCatalog.Build. The gestures wrap a real AnytypeApiClient
// built from dummy credentials, but Build only constructs descriptors (the
// handlers are lazy lambdas) so no HTTP call is ever made here. These pin the
// advertised surface: exactly the 17 base named tools, each with a well-formed
// object input schema that forbids extra properties.
[Trait("Category", "unit")]
public class ToolCatalogTests
{
    static readonly string[] ExpectedToolNames =
    {
        "session_start", "log", "get", "project_overview", "create_task",
        "complete", "archive", "link", "list_projects", "search", "subtask",
        "create_epic", "create_project", "create_idea", "create_document", "update", "replace_section",
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
            new QueryGestures(client, resolver),
            new DocumentGestures(client));
    }

    [Fact]
    public void BuildExposesExactlyTheSeventeenNamedTools()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.Equal(17, names.Length);
        Assert.Equal(
            ExpectedToolNames.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public void LinkDescriptionAdvertisesProjectToEpicMembership()
    {
        ToolDescriptor link = BuildCatalog().Single(t => t.Name == "link");

        Assert.Contains("project -> epic", link.Description);
        Assert.DoesNotContain("cannot attach anything to an epic", link.Description);
    }

    [Fact]
    public void ProjectManagementToolsAdvertiseTheEpicChantierTaskModel()
    {
        var tools = BuildCatalog();
        ToolDescriptor complete = tools.Single(t => t.Name == "complete");
        ToolDescriptor createTask = tools.Single(t => t.Name == "create_task");
        ToolDescriptor createProject = tools.Single(t => t.Name == "create_project");

        Assert.Contains("project or task", complete.Description);
        Assert.True(((JsonObject)complete.InputSchema["properties"]!).ContainsKey("object"));
        Assert.Contains("surveillance", PropertyDescription(createTask, "type"));
        Assert.Contains("Existing projects", PropertyDescription(createProject, "epic"));
        Assert.Contains("permanent Epic", McpSurfaceDescriptor.ProjectManagement.Instructions);
        Assert.Contains("Project (chantier)", McpSurfaceDescriptor.ProjectManagement.Instructions);
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
    public void StatelessMutationsRequireTheirExplicitRecoveryCoordinates()
    {
        IReadOnlyList<ToolDescriptor> tools = BuildCatalog();

        AssertRequired(tools.Single(tool => tool.Name == "log"), "report");
        AssertRequired(tools.Single(tool => tool.Name == "subtask"), "done");
        Assert.Contains("report_id", tools.Single(tool => tool.Name == "session_start").Description);
        Assert.True(tools.Single(tool => tool.Name == "log").Execution.RequiresStableTarget);
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

    [Fact]
    public void EveryToolDeclaresItsAmbiguousOutcomePolicy()
    {
        var expected = new Dictionary<string, AmbiguousOutcomePolicy>(StringComparer.Ordinal)
        {
            ["session_start"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["log"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["get"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["project_overview"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["create_task"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["complete"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["archive"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["link"] = AmbiguousOutcomePolicy.Uncertain,
            ["list_projects"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["search"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["subtask"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["create_epic"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_project"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_idea"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_document"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["update"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["replace_section"] = AmbiguousOutcomePolicy.SafeToRetry,
        };

        Assert.Equal(expected, BuildCatalog().ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.AmbiguousOutcome,
            StringComparer.Ordinal));
        Assert.True(BuildCatalog().Single(tool => tool.Name == "update").Execution.RequiresStableTarget);

        var expectedChanges = new Dictionary<string, ToolChangeKind>(StringComparer.Ordinal)
        {
            ["session_start"] = ToolChangeKind.Additive,
            ["log"] = ToolChangeKind.Additive,
            ["get"] = ToolChangeKind.None,
            ["project_overview"] = ToolChangeKind.None,
            ["create_task"] = ToolChangeKind.Additive,
            ["complete"] = ToolChangeKind.Overwriting,
            ["archive"] = ToolChangeKind.Overwriting,
            ["link"] = ToolChangeKind.Additive,
            ["list_projects"] = ToolChangeKind.None,
            ["search"] = ToolChangeKind.None,
            ["subtask"] = ToolChangeKind.Overwriting,
            ["create_epic"] = ToolChangeKind.Additive,
            ["create_project"] = ToolChangeKind.Additive,
            ["create_idea"] = ToolChangeKind.Additive,
            ["create_document"] = ToolChangeKind.Additive,
            ["update"] = ToolChangeKind.Overwriting,
            ["replace_section"] = ToolChangeKind.Overwriting,
        };
        Assert.Equal(expectedChanges, BuildCatalog().ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.Change,
            StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskWithoutDoneIsRejectedBeforeAnytypeIo()
    {
        ToolDescriptor subtask = BuildCatalog().Single(tool => tool.Name == "subtask");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => subtask.Handler(
            new JsonObject { ["task"] = "task-id", ["label"] = "check me" },
            TestContext.Current.CancellationToken));

        Assert.Contains("done", error.Message, StringComparison.Ordinal);
    }

    static string PropertyDescription(ToolDescriptor tool, string property)
    {
        var properties = Assert.IsType<JsonObject>(tool.InputSchema["properties"]);
        var schema = Assert.IsType<JsonObject>(properties[property]);
        return schema["description"]!.GetValue<string>();
    }

    static void AssertRequired(ToolDescriptor tool, string property)
    {
        JsonArray required = Assert.IsType<JsonArray>(tool.InputSchema["required"]);
        Assert.Contains(required, value => value?.GetValue<string>() == property);
    }
}
