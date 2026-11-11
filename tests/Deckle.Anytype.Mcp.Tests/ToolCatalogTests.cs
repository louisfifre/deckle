using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class ToolCatalogTests
{
    static readonly string[] ExpectedToolNames =
    {
        "open_report", "log", "close_report",
        "task_properties", "task_checklist", "task_notes", "project_overview", "today_overview",
        "epic_properties", "report_journal", "idea_text", "document_outline", "document_section",
        "create_task", "complete", "archive", "link", "unlink", "list_projects", "search", "subtask",
        "create_epic", "create_project", "create_idea", "create_document", "update", "replace_section",
        "create_person", "create_company",
    };

    // Every read is typed and named by what it returns; a whole-object read
    // would bring the measured context cost back.
    static readonly string[] TypedReadNames =
    {
        "task_properties", "task_checklist", "task_notes", "project_overview", "today_overview",
        "epic_properties", "report_journal", "idea_text", "document_outline", "document_section",
    };

    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        // Dummy credentials suffice: the client constructor makes no request and
        // Build only constructs descriptors whose handlers are lazy lambdas.
        var credentials = new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space");
        var client = new AnytypeApiClient(credentials);
        var resolver = new NameResolver(client);

        return ToolCatalog.Build(
            new SessionGestures(client, resolver, client.V2),
            new TaskGestures(client, resolver, client.V2),
            new ProjectGestures(client, resolver, client.V2),
            new QueryGestures(client, resolver, client.V2),
            new ReadGestures(resolver, client.V2),
            new DocumentGestures(client),
            new ContactGestures(client, resolver, client.V2));
    }

    [Fact]
    public void BuildExposesExactlyTheNamedTools()
    {
        var names = BuildCatalog().Select(t => t.Name).ToArray();

        Assert.Equal(ExpectedToolNames.Length, names.Length);
        Assert.Equal(
            ExpectedToolNames.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void EveryReadIsTypedAndNoWholeObjectReadRemains()
    {
        IReadOnlyList<ToolDescriptor> tools = BuildCatalog();

        Assert.DoesNotContain("get", tools.Select(t => t.Name));
        // The only non-typed reads are the two finders: a list and a search.
        Assert.Equal(
            TypedReadNames.Concat(["list_projects", "search"]).OrderBy(n => n),
            tools.Where(t => t.Execution.Change == ToolChangeKind.None)
                .Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void CreateTaskFillsTheTemplateSectionsInsteadOfTakingARawBody()
    {
        ToolDescriptor createTask = BuildCatalog().Single(t => t.Name == "create_task");
        var properties = Assert.IsType<JsonObject>(createTask.InputSchema["properties"]);

        Assert.False(properties.ContainsKey("body"));
        Assert.True(properties.ContainsKey("checklist"));
        Assert.True(properties.ContainsKey("notes"));
    }

    [Fact]
    public void LinkDescriptionAdvertisesProjectToEpicMembership()
    {
        ToolDescriptor link = BuildCatalog().Single(t => t.Name == "link");

        Assert.Contains("project -> epic", link.Description);
        Assert.DoesNotContain("cannot attach anything to an epic", link.Description);
    }

    [Fact]
    public void ProjectManagementToolsAdvertiseTheEpicProjectTaskModel()
    {
        var tools = BuildCatalog();
        ToolDescriptor complete = tools.Single(t => t.Name == "complete");
        ToolDescriptor createTask = tools.Single(t => t.Name == "create_task");
        ToolDescriptor createProject = tools.Single(t => t.Name == "create_project");

        // The done box is a task's signal; a project is refused and sent to archive.
        Assert.Contains("a task's built-in done checkbox", complete.Description);
        Assert.Contains("closed with archive", complete.Description);
        Assert.True(((JsonObject)complete.InputSchema["properties"]!).ContainsKey("object"));
        Assert.Contains("monitoring", PropertyDescription(createTask, "type"));
        Assert.DoesNotContain("surveillance", PropertyDescription(createTask, "type"));
        Assert.Contains("Existing projects", PropertyDescription(createProject, "epic"));
        Assert.Contains("permanent Epic", McpSurfaceDescriptor.ProjectManagement.Instructions);
        Assert.Contains("A Project is a durable subject", McpSurfaceDescriptor.ProjectManagement.Instructions);
    }

    // The model v6 retired the chantier renamed at each stage and the done box of
    // a project: the words of the old model must not come back in what the server
    // tells a client.
    [Fact]
    public void ProjectManagementTextsSpeakTheV6ModelAndNotTheOldOne()
    {
        string instructions = McpSurfaceDescriptor.ProjectManagement.Instructions;
        string all = instructions + "\n" + string.Join(
            "\n", BuildCatalog().Select(t => t.Description + "\n" + t.InputSchema.ToJsonString()));

        Assert.Contains("closed by archiving", instructions);
        Assert.Contains("has no done checkbox", instructions);
        Assert.Contains("ready (Prêt), doing (En cours), paused (En pause), dormant (Dormant)", instructions);
        Assert.Contains("État is kept on projects only", instructions);

        foreach (string old in new[]
        {
            "chantier", "Chantier", "termine", "en_cours", "en_attente", "abandonne", "ouvert",
            "renamed after", "depend_de", "contact_lie", "entreprise", "rapport", "idee",
            "recherche", "organiser", "echanger", "gestion", "surveillance",
        })
            Assert.DoesNotContain(old, all);
    }

    [Fact]
    public void ListProjectsAndCreateProjectNameTheFourStatesOfAProject()
    {
        var tools = BuildCatalog();

        foreach ((string tool, string argument) in new[] { ("list_projects", "state"), ("create_project", "state") })
        {
            string text = PropertyDescription(tools.Single(t => t.Name == tool), argument);
            Assert.Contains("ready (Prêt)", text);
            Assert.Contains("dormant (Dormant)", text);
            Assert.DoesNotContain("termine", text);
        }
    }

    // An epic carries no état the tools write, and a project is created with its
    // état, priority and broad done definition (model v6, section Project).
    [Fact]
    public void CreateEpicTakesOnlyANameAndCreateProjectTakesTheProjectFacts()
    {
        var tools = BuildCatalog();

        Assert.Equal(
            ["name"],
            ((JsonObject)tools.Single(t => t.Name == "create_epic").InputSchema["properties"]!).Select(p => p.Key));
        var project = (JsonObject)tools.Single(t => t.Name == "create_project").InputSchema["properties"]!;
        Assert.Contains("priority", project.Select(p => p.Key));
        Assert.Contains("done_definition", project.Select(p => p.Key));
    }

    [Fact]
    public void SearchRestrictsItsTypesToTheV6TypeKeys()
    {
        ToolDescriptor search = BuildCatalog().Single(t => t.Name == "search");
        JsonArray types = (JsonArray)search.InputSchema["properties"]!["types"]!["items"]!["enum"]!;

        Assert.Equal(DevSpace.TypeKeys, types.Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void ProjectManagementInstructionsAskTheFirstAnswerToPlaceTheTaskInItsProject()
    {
        const string rule = "When the work belongs to a task of this space, open the first answer "
            + "by placing it in its project, so the person sees at once whether the session "
            + "aims at the right one.";

        Assert.Contains(rule, McpSurfaceDescriptor.ProjectManagement.Instructions);
        Assert.DoesNotContain(rule, McpSurfaceDescriptor.SchemaAdmin.Instructions);
    }

    // The ritual lives in the instructions, not in a tool: an agent that skips
    // it opens no report, or one with no task behind it.
    [Fact]
    public void ProjectManagementInstructionsCarryTheWorkRitual()
    {
        string instructions = McpSurfaceDescriptor.ProjectManagement.Instructions;

        Assert.Contains("open_report", instructions);
        Assert.Contains("no report without a task", instructions);
        Assert.DoesNotContain("session_start", instructions);
        Assert.Contains("create it first", instructions);
        // Typed reads take words, and the two writes that must not guess take an id.
        Assert.Contains("an exact name wins, else a single match wins", instructions);
        Assert.Contains("open_report and log take an id only", instructions);
    }

    // A compaction shrinks the conversation into a summary; the instructions
    // survive it, so they carry the resume rule for every client, where a
    // client hook would serve only one.
    [Fact]
    public void ProjectManagementInstructionsResumeFromTheReportAfterASummary()
    {
        string instructions = McpSurfaceDescriptor.ProjectManagement.Instructions;

        Assert.Contains("after a compaction", instructions);
        Assert.Contains("read the report's journal and the task's checklist before the next action", instructions);
        Assert.DoesNotContain("after a compaction", McpSurfaceDescriptor.SchemaAdmin.Instructions);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void OpenReportTakesATaskIdAndNothingElse()
    {
        ToolDescriptor open = BuildCatalog().Single(t => t.Name == "open_report");

        AssertRequired(open, "task");
        Assert.Equal(["task"], ((JsonObject)open.InputSchema["properties"]!).Select(p => p.Key));
    }

    [Fact]
    public void BuildDoesNotExposeAnyDestructiveTool()
    {
        // delete lives only in the supervised ManagementToolCatalog; the base
        // surface must never carry it. complete covers task_done.
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
        Assert.Contains("report_id", tools.Single(tool => tool.Name == "open_report").Description);
        Assert.True(tools.Single(tool => tool.Name == "log").Execution.RequiresStableTarget);
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

    [Fact]
    public void EveryToolDeclaresItsAmbiguousOutcomePolicy()
    {
        var expected = new Dictionary<string, AmbiguousOutcomePolicy>(StringComparer.Ordinal)
        {
            ["open_report"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["log"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["close_report"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["task_properties"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["task_checklist"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["task_notes"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["epic_properties"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["report_journal"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["idea_text"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["document_outline"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["document_section"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["project_overview"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["today_overview"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["create_task"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["complete"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["archive"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["link"] = AmbiguousOutcomePolicy.Uncertain,
            ["unlink"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["list_projects"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["search"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["subtask"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["create_epic"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_project"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_idea"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_document"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_person"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["create_company"] = AmbiguousOutcomePolicy.RequiresDeduplication,
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
            ["open_report"] = ToolChangeKind.Additive,
            ["log"] = ToolChangeKind.Additive,
            ["close_report"] = ToolChangeKind.Additive,
            ["task_properties"] = ToolChangeKind.None,
            ["task_checklist"] = ToolChangeKind.None,
            ["task_notes"] = ToolChangeKind.None,
            ["epic_properties"] = ToolChangeKind.None,
            ["report_journal"] = ToolChangeKind.None,
            ["idea_text"] = ToolChangeKind.None,
            ["document_outline"] = ToolChangeKind.None,
            ["document_section"] = ToolChangeKind.None,
            ["project_overview"] = ToolChangeKind.None,
            ["today_overview"] = ToolChangeKind.None,
            ["create_task"] = ToolChangeKind.Additive,
            ["complete"] = ToolChangeKind.Overwriting,
            ["archive"] = ToolChangeKind.Overwriting,
            ["link"] = ToolChangeKind.Additive,
            ["unlink"] = ToolChangeKind.Overwriting,
            ["list_projects"] = ToolChangeKind.None,
            ["search"] = ToolChangeKind.None,
            ["subtask"] = ToolChangeKind.Overwriting,
            ["create_epic"] = ToolChangeKind.Additive,
            ["create_project"] = ToolChangeKind.Additive,
            ["create_idea"] = ToolChangeKind.Additive,
            ["create_document"] = ToolChangeKind.Additive,
            ["create_person"] = ToolChangeKind.Additive,
            ["create_company"] = ToolChangeKind.Additive,
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
