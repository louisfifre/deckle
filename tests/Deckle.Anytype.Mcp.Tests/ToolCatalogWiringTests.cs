using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// What the tools of the real catalog do to the Anytype space when an agent
// calls them. Each test calls a tool by its name with its JSON arguments, as a
// client does, against a fake space that keeps its objects, then reads what the
// space holds. An argument sent to the wrong place (two swapped in a handler)
// shows as an object that did not change, or one that changed when it should
// not have.
[Trait("Category", "integration")]
public sealed class ToolCatalogWiringTests : IDisposable
{
    const string TaskId = "bafyreitaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherTaskId = "bafyreitaskbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string ProjectId = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ReportId = "bafyreireportaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherReportId = "bafyreireportbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeAnytypeSpace _space = new();
    readonly AnytypeApiClient _client;
    readonly Clock _clock = new(new DateTime(2026, 11, 24, 9, 0, 0));
    readonly IReadOnlyList<ToolDescriptor> _tools;

    public ToolCatalogWiringTests()
    {
        _client = new AnytypeApiClient(_space.Credentials);
        var resolver = new NameResolver(_client);
        _tools = ToolCatalog.Build(
            new SessionGestures(_client, resolver, _client.V2, _clock),
            new TaskGestures(_client, resolver, _client.V2),
            new ProjectGestures(_client, resolver, _client.V2),
            new QueryGestures(_client, resolver, _client.V2),
            new ReadGestures(resolver, _client.V2),
            new DocumentGestures(_client),
            new ContactGestures(_client, resolver, _client.V2));
    }

    public void Dispose()
    {
        _client.Dispose();
        _space.Dispose();
    }

    // A clock the test moves, whose local zone is UTC so the journal stamps do
    // not depend on the machine running the tests.
    sealed class Clock(DateTime start) : TimeProvider
    {
        DateTime _now = start;

        public void At(int hour, int minute) => _now = _now.Date.AddHours(hour).AddMinutes(minute);

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(_now, DateTimeKind.Utc));

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    // The arguments travel as JSON text and are parsed back, as a client's
    // request reaches the host: numbers come out of the parser, not out of C#.
    async Task<string> Call(string tool, JsonObject arguments) =>
        (await _tools.Single(t => t.Name == tool)
            .Handler((JsonObject)JsonNode.Parse(arguments.ToJsonString())!, Ct)).Text;

    FakeAnytypeSpace.Stored AddReport(string id, params string[] taskIds) =>
        _space.Add(id, DevSpace.Types.Report, "", "# Journal 2026-11-24",
            (DevSpace.Props.Tasks, FakeAnytypeSpace.Ids(taskIds)));

    // ── open_report ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenReportOpensAReportOnTheTaskItIsGivenAndNamesTheTaskAndItsProject()
    {
        _space.Add(ProjectId, DevSpace.Types.Project, "Anytype");
        _space.Add(TaskId, DevSpace.Types.Task, "Écrire les tests",
            properties: (DevSpace.Props.Projects, FakeAnytypeSpace.Ids(ProjectId)));
        _space.Add(OtherTaskId, DevSpace.Types.Task, "Une autre tâche");

        string answer = await Call("open_report", new JsonObject { ["task"] = TaskId });

        FakeAnytypeSpace.Stored report = Assert.Single(
            _space.Objects, o => o.Type == DevSpace.Types.Report);
        Assert.Equal([TaskId], report.Ids(DevSpace.Props.Tasks));
        Assert.Equal($"report_id : {report.Id}\nÉcrire les tests · Anytype", answer);
    }

    // ── log ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogWritesTheLineInTheReportItIsGivenAndInNoOtherOne()
    {
        FakeAnytypeSpace.Stored target = AddReport(ReportId, TaskId);
        FakeAnytypeSpace.Stored other = AddReport(OtherReportId, TaskId);
        string otherBefore = other.Markdown;

        string answer = await Call("log", new JsonObject
        {
            ["line"] = "Choisi : un faux serveur Anytype.",
            ["report"] = ReportId,
        });

        Assert.Contains("Choisi : un faux serveur Anytype.", target.Markdown);
        Assert.Equal(otherBefore, other.Markdown);
        Assert.Equal("Noté.", answer);
    }

    // ── close_report ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseReportWritesItsClosingEntryInTheReportAndTheTimeOnEachTaskItCarries()
    {
        _space.Add(TaskId, DevSpace.Types.Task, "Écrire les tests");
        _space.Add(OtherTaskId, DevSpace.Types.Task, "Relire");
        FakeAnytypeSpace.Stored report = AddReport(ReportId, TaskId, OtherTaskId);
        FakeAnytypeSpace.Stored elsewhere = AddReport(OtherReportId, TaskId);
        string elsewhereBefore = elsewhere.Markdown;

        string answer = await Call("close_report", new JsonObject
        {
            ["report"] = ReportId,
            ["summary"] = "Fini : les cinq outils. Reste : rien.",
            ["done"] = new JsonArray(TaskId),
            ["corrected_hours"] = 2,
        });

        Assert.Contains(
            ReportTime.ClosingLine(2, "Fini : les cinq outils. Reste : rien.", corrected: true),
            report.Markdown);
        Assert.Equal(elsewhereBefore, elsewhere.Markdown);

        // Only the task named in done is ticked; the two hours are shared by
        // the two tasks the report carries.
        Assert.True(_space[TaskId].Checkbox(DevSpace.Props.Done));
        Assert.False(_space[OtherTaskId].Checkbox(DevSpace.Props.Done));
        Assert.Equal(1, _space[TaskId].Number(DevSpace.Props.TimeActual));
        Assert.Equal(1, _space[OtherTaskId].Number(DevSpace.Props.TimeActual));

        Assert.Contains("[x] Écrire les tests", answer);
        Assert.Contains("[ ] Relire", answer);
    }

    // ── closing twice ────────────────────────────────────────────────────────

    // The tool says « Closing again replaces the figure; a report never counts
    // twice »: a report worked from 9:00 to 10:00, then closed at 11:00 is
    // worth two hours, and closing it again to mend the summary leaves it so.
    [Fact]
    public async Task ClosingAReportAgainKeepsItsFigureAndDoesNotCountItTwice()
    {
        _space.Add(TaskId, DevSpace.Types.Task, "Écrire les tests");
        FakeAnytypeSpace.Stored report = AddReport(ReportId, TaskId);

        await WorkAndCloseOnce();
        Assert.Equal(2, _space[TaskId].Number(DevSpace.Props.TimeActual));

        _clock.At(11, 30);
        string answer = await Call("close_report", new JsonObject
        {
            ["report"] = ReportId, ["summary"] = "Résumé corrigé.",
        });

        Assert.Equal(2, _space[TaskId].Number(DevSpace.Props.TimeActual));
        Assert.Contains(ReportTime.ClosingLine(2, "Résumé corrigé.", corrected: false), report.Markdown);
        Assert.StartsWith("Rapport clos : temps réel 2 h", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingAReportAgainWithACorrectedTimeReplacesTheFigureInsteadOfAddingToIt()
    {
        _space.Add(TaskId, DevSpace.Types.Task, "Écrire les tests");
        AddReport(ReportId, TaskId);

        await WorkAndCloseOnce();
        _clock.At(11, 30);
        await Call("close_report", new JsonObject
        {
            ["report"] = ReportId, ["summary"] = "Temps corrigé.", ["corrected_hours"] = 3,
        });

        Assert.Equal(3, _space[TaskId].Number(DevSpace.Props.TimeActual));
    }

    async Task WorkAndCloseOnce()
    {
        _clock.At(9, 0);
        await Call("log", new JsonObject { ["line"] = "Début.", ["report"] = ReportId });
        _clock.At(10, 0);
        await Call("log", new JsonObject { ["line"] = "Livré.", ["report"] = ReportId });
        _clock.At(11, 0);
        await Call("close_report", new JsonObject { ["report"] = ReportId, ["summary"] = "Fini." });
    }

    // ── update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRenamesAndSetsThePropertiesOfTheObjectItIsGivenAndOfNoOtherOne()
    {
        _space.Add(TaskId, DevSpace.Types.Task, "Ancien titre");
        _space.Add(OtherTaskId, DevSpace.Types.Task, "Autre tâche");

        await Call("update", new JsonObject
        {
            ["object"] = TaskId,
            ["name"] = "Nouveau titre",
            ["properties"] = new JsonObject { [DevSpace.Props.DueDate] = "2026-12-01" },
        });

        Assert.Equal("Nouveau titre", _space[TaskId].Name);
        Assert.Equal("2026-12-01", _space[TaskId].Text(DevSpace.Props.DueDate));
        Assert.Equal("Autre tâche", _space[OtherTaskId].Name);
        Assert.Empty(_space[OtherTaskId].Properties);
    }

    // ── create_task ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTaskMakesATaskUnderTheProjectWithEachValueInItsOwnProperty()
    {
        _space.Add(ProjectId, DevSpace.Types.Project, "Anytype");
        _space.Add(OtherTaskId, DevSpace.Types.Task, "Orchestration");

        string answer = await Call("create_task", new JsonObject
        {
            ["project"] = ProjectId,
            ["name"] = "Écrire les tests des outils",
            ["type"] = "production",
            ["priority"] = 3,
            ["due_date"] = "2026-12-01",
            ["time_estimated"] = 1.5,
            ["done_definition"] = "Les cinq outils ont leur test.",
            ["orchestrated_by"] = OtherTaskId,
        });

        FakeAnytypeSpace.Stored task = Assert.Single(
            _space.Objects, o => o.Type == DevSpace.Types.Task && o.Id != OtherTaskId);
        Assert.Equal("Écrire les tests des outils", task.Name);
        Assert.Equal([ProjectId], task.Ids(DevSpace.Props.Projects));
        Assert.Equal([OtherTaskId], task.Ids(DevSpace.Props.OrchestratedBy));
        Assert.Equal(
            [DevSpace.TaskType.NameFor(DevSpace.TaskType.Resolve("production")!)!],
            task.Ids(DevSpace.Props.TaskType));
        Assert.Equal(["3"], task.Ids(DevSpace.Props.Priority));
        Assert.Equal("2026-12-01", task.Text(DevSpace.Props.DueDate));
        Assert.Equal(1.5, task.Number(DevSpace.Props.TimeEstimated));
        Assert.Equal("Les cinq outils ont leur test.", task.Text(DevSpace.Props.DoneDefinition));
        Assert.Contains($"id : {task.Id}", answer);
    }
}
