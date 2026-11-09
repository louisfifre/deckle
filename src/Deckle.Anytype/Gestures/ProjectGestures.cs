using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Deckle.Anytype;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Project-level gestures: a project's overview digest, the project list grouped
// by état, and creation of projects and their epic containers.
//
// A project is a durable subject, not a chantier: it has no done box and is
// closed by archiving, so no digest here shows or writes one. Its état is the
// only état the space maintains (Prêt, En cours, En pause, Dormant).
//
// Every digest is a terse French plain-string — the LLM-facing product. One line
// per fact, no banner, no markdown decoration beyond what carries meaning.
//
// Reads go through the v2 client: the lists are filtered by the server (tasks
// by their project, reports by their tasks, projects by état) instead of paging
// the whole space and filtering here. Creation is on v2 too: the type's default
// template applies, so no template id is frozen in the code.
public sealed partial class ProjectGestures(
    AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;

    // Reports shown under « Derniers rapports ».
    const int RecentReports = 3;
    // Length of a report's latest entry shown beside it.
    const int LastEntryChars = 120;

    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Searcher _searcher = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);

    public async Task<string> OverviewAsync(string project, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string projectId = await resolver.ResolveAsync(
            project, [DevSpace.Types.Project], NameMatch.Tolerant, ct);
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
            projectId, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        var props = AnytypeV2PropertyMap.Of(doc);

        var sb = new StringBuilder();
        sb.Append("id : ").Append(projectId).Append('\n');
        sb.Append(props.Name);

        AppendHeaderFacts(sb, props);

        sb.Append('\n');
        IReadOnlyList<string> taskIds = await AppendTasksAsync(sb, projectId, ct);
        await AppendRecentReportsAsync(sb, taskIds, ct);

        DeckleAnytypeSource.Log.GestureCompleted("project_overview", Elapsed(started));
        return sb.ToString().TrimEnd();
    }

    public async Task<string> ListAsync(string? state = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string? wantedStatus = state is null
            ? null
            : DevSpace.Status.Resolve(state)
                ?? throw new ArgumentException(UnknownStatus(state), nameof(state));

        // A given état is filtered by the server, by its option name (a select
        // is addressed by name on v2). Archived projects are dropped here: the
        // checkbox is served only when ticked, and how a filter treats its
        // absence is not measured.
        IReadOnlyList<AnytypeV2SearchRow> hits = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Project,
                Filters = wantedStatus is null
                    ? null
                    : [AnytypeV2SearchFilter.In(DevSpace.Props.Status, [DevSpace.Status.NameFor(wantedStatus)!])],
                Fields = [DevSpace.Props.Archive, DevSpace.Props.Status, DevSpace.Props.Priority],
            }, ct);

        var byStatus = new Dictionary<string, List<AnytypeV2SearchRow>>(StringComparer.Ordinal);
        foreach (AnytypeV2SearchRow p in hits)
        {
            if (p.Properties.Checkbox(DevSpace.Props.Archive)) continue;

            // v2 serves the option name; the order and the grouping run on keys.
            string? statusName = p.Properties.Select(DevSpace.Props.Status);
            string status = statusName is null ? "" : DevSpace.Status.Resolve(statusName) ?? statusName;
            if (wantedStatus is not null && status != wantedStatus) continue;

            (byStatus.TryGetValue(status, out var list) ? list : byStatus[status] = []).Add(p);
        }

        var sb = new StringBuilder();
        IEnumerable<string> order = wantedStatus is not null
            ? [wantedStatus]
            : DevSpace.Status.All.Select(o => o.Key).Where(byStatus.ContainsKey);

        foreach (string statusKey in order)
        {
            if (!byStatus.TryGetValue(statusKey, out var projects) || projects.Count == 0) continue;

            if (wantedStatus is null) sb.Append('\n').Append(DevSpace.Status.NameFor(statusKey) ?? statusKey).Append('\n');
            foreach (AnytypeV2SearchRow p in projects) AppendProjectLine(sb, p);
        }

        if (wantedStatus is null)
            foreach (var (status, projects) in byStatus)
                if (DevSpace.Status.NameFor(status) is null)
                {
                    sb.Append('\n').Append("Sans état").Append('\n');
                    foreach (AnytypeV2SearchRow p in projects) AppendProjectLine(sb, p);
                }

        string digest = sb.ToString().Trim();
        if (digest.Length == 0) digest = "Aucun projet.";

        DeckleAnytypeSource.Log.GestureCompleted("project_list", Elapsed(started));
        return digest;
    }

    // The read « maintenant » of the model v6: what to do today, then what runs
    // elsewhere. Its rule is the home page's « Aujourd'hui »: the tasks neither
    // done nor archived whose due date is today or past, the late ones with
    // their date; no sort by slot or priority, the late ones first because they
    // are older. Each task comes with its detail so the session can start on
    // one without a second read. Then the projects « En cours », one line each.
    //
    // Two searches, both whole: the projects (their names serve the task lines
    // and their état the last list) and the tasks. The tasks are filtered here
    // on their date and boxes: a date comparison and a filter on an absent
    // checkbox are not measured on the v2 search, and a refused filter would
    // break the read. The cost is one page per hundred tasks of the space.
    public async Task<string> TodayAsync(CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string today = _time.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        IReadOnlyList<AnytypeV2SearchRow> projects = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Project,
                Fields =
                [
                    DevSpace.Props.Archive, DevSpace.Props.Status,
                    DevSpace.Props.Priority, DevSpace.Props.DueDate,
                ],
            }, ct);
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);

        IReadOnlyList<AnytypeV2SearchRow> tasks = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Task,
                Fields =
                [
                    DevSpace.Props.Done, DevSpace.Props.Archive, DevSpace.Props.DueDate,
                    DevSpace.Props.Projects, DevSpace.Props.TaskType, DevSpace.Props.Priority,
                    DevSpace.Props.DoneDefinition, DevSpace.Props.TimeEstimated,
                ],
            }, ct);

        var due = tasks
            .Where(t => !t.Properties.Checkbox(DevSpace.Props.Done) && !t.Properties.Checkbox(DevSpace.Props.Archive))
            .Select(t => (Task: t, Day: t.Properties.Day(DevSpace.Props.DueDate)))
            .Where(t => t.Day is not null && string.CompareOrdinal(t.Day, today) <= 0)
            .OrderBy(t => t.Day, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("Aujourd'hui, ").Append(today).Append(" :");
        if (due.Count == 0) sb.Append("\nAucune tâche du jour ni en retard.");
        foreach ((AnytypeV2SearchRow t, string? day) in due)
            AppendDueTask(sb, t, day!, today, projectNames);

        var running = projects
            .Where(p => !p.Properties.Checkbox(DevSpace.Props.Archive))
            .Where(p => p.Properties.Select(DevSpace.Props.Status) is { } status
                && DevSpace.Status.Resolve(status) == DevSpace.Status.Doing)
            .ToList();

        sb.Append("\n\nProjets en cours :");
        if (running.Count == 0) sb.Append("\nAucun.");
        foreach (AnytypeV2SearchRow p in running)
        {
            sb.Append('\n').Append(p.Name);
            int? level = DevSpace.Priority.LevelForName(p.Properties.Select(DevSpace.Props.Priority));
            if (level is not null) sb.Append(" · P").Append(level);
            if (p.Properties.Day(DevSpace.Props.DueDate) is { } target) sb.Append(" · cible ").Append(target);
        }

        DeckleAnytypeSource.Log.GestureCompleted("today_overview", Elapsed(started));
        return sb.ToString();
    }

    static void AppendDueTask(
        StringBuilder sb, AnytypeV2SearchRow t, string day, string today, IReadOnlyDictionary<string, string> projectNames)
    {
        sb.Append("\n\n[ ] ").Append(t.Name);
        if (string.CompareOrdinal(day, today) < 0) sb.Append(" · en retard, prévue le ").Append(day);

        string names = string.Join(", ", t.Properties.ObjectIds(DevSpace.Props.Projects)
            .Select(id => projectNames.TryGetValue(id, out string? n) && n.Length > 0 ? n : "(projet non lu)"));
        if (names.Length > 0) sb.Append("\nProjet : ").Append(names);

        var facts = new List<string>();
        if (t.Properties.Select(DevSpace.Props.TaskType) is { } type) facts.Add(type);
        int? level = DevSpace.Priority.LevelForName(t.Properties.Select(DevSpace.Props.Priority));
        if (level is not null) facts.Add($"P{level}");
        if (t.Properties.Number(DevSpace.Props.TimeEstimated) is { } estimate
            && double.TryParse(estimate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hours))
            facts.Add($"{hours.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} h estimée(s)");
        if (facts.Count > 0) sb.Append('\n').Append(string.Join(" · ", facts));

        AppendFact(sb, "Définition de fini", t.Properties.Text(DevSpace.Props.DoneDefinition));
    }

    // An epic groups projects and is not maintained: its état, its priority and
    // the rest are kept for whoever wants to manage it that way, and this tool
    // writes none of them.
    public async Task<string> CreateEpicAsync(string name, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        var payload = new JsonObject
        {
            ["type"] = DevSpace.Types.Epic,
            ["name"] = name,
        };
        await _writer.CreateObjectAsync(payload, ct: ct);

        DeckleAnytypeSource.Log.GestureCompleted("epic_create", Elapsed(started));
        return $"Epic créé : {name}";
    }

    // A project enters its epic twice, by the epic's collection and by its own
    // `epic` property: the property is what filters and what mobile shows, the
    // collection is what the epic's views list. Both are written in one gesture.
    //
    // The model asks, at creation, for the name, the état, the priority and the
    // done definition. The état starts at Prêt when none is given.
    public async Task<string> CreateAsync(
        string name, string? epic = null, string? state = null,
        int? priority = null, string? doneDefinition = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string statusKey = state is null
            ? DevSpace.Status.Ready
            : DevSpace.Status.Resolve(state)
                ?? throw new ArgumentException(UnknownStatus(state), nameof(state));
        if (priority is int level)
            DevSpace.Priority.KeyFor(level); // refuses a level outside 0-5
        string definition = (doneDefinition ?? "").Trim();

        // The epic is resolved before the create, so an unknown one is refused
        // with nothing made.
        string? epicId = epic is null
            ? null
            : await resolver.ResolveAsync(epic, [DevSpace.Types.Epic], NameMatch.Exact, ct);

        // A select is addressed by its option name on v2, a relation by a list
        // of ids.
        var props = new JsonObject
        {
            [DevSpace.Props.Status] = new JsonArray(DevSpace.Status.NameFor(statusKey)!),
        };
        if (priority is int p)
            props[DevSpace.Props.Priority] = new JsonArray(p.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (definition.Length > 0) props[DevSpace.Props.DoneDefinition] = definition;
        if (epicId is not null) props[DevSpace.Props.Epic] = new JsonArray(epicId);

        var payload = new JsonObject
        {
            ["type"] = DevSpace.Types.Project,
            ["name"] = name,
            ["properties"] = props,
        };
        AnytypeV2WriteReceipt receipt = await _writer.CreateObjectAsync(payload, ct: ct);
        string projectId = receipt.Body?["id"] is JsonValue idValue && idValue.TryGetValue(out string? created)
            ? created ?? ""
            : "";

        var sb = new StringBuilder();
        sb.Append("Projet créé : ").Append(name);
        sb.Append(" (").Append(DevSpace.Status.NameFor(statusKey)).Append(')');
        if (projectId.Length > 0) sb.Append("\nid : ").Append(projectId);

        if (epicId is not null)
        {
            if (projectId.Length == 0)
                throw new InvalidOperationException(
                    $"Projet créé ({name}) mais son identifiant n'a pas été renvoyé : il n'est pas ajouté à l'epic. "
                    + "Ne le recrée pas : rattache-le avec link.");
            await api.AddToCollectionAsync(epicId, [projectId], ct);
            sb.Append("\nAjouté à l'epic ").Append(StringOf(await api.GetObjectAsync(epicId, ct), "name"));
        }

        DeckleAnytypeSource.Log.GestureCompleted("project_create", Elapsed(started));
        return sb.ToString();
    }

    // ── Digest building ───────────────────────────────────────────────────

    static string StringOf(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";

    static string UnknownStatus(string state) =>
        $"État inconnu « {state} ». États : {string.Join(", ", DevSpace.Status.All.Select(o => $"{o.Name} ({o.Key})"))}.";

    static void AppendHeaderFacts(StringBuilder sb, AnytypeV2PropertyMap project)
    {
        // v2 serves a select as its option name, ready to show.
        AppendFact(sb, "État", project.Select(DevSpace.Props.Status));

        int? level = DevSpace.Priority.LevelForName(project.Select(DevSpace.Props.Priority));
        if (level is not null) AppendFact(sb, "Priorité", level.ToString());

        AppendFact(sb, "Définition de fini", project.Text(DevSpace.Props.DoneDefinition));
        AppendFact(sb, "Date cible", project.Day(DevSpace.Props.DueDate));
        if (project.Checkbox(DevSpace.Props.Archive)) AppendFact(sb, "Archivé", "oui");
    }

    // Returns every related task id for the report join, archived tasks
    // included: archiving a task does not archive the reports linked to it.
    async Task<IReadOnlyList<string>> AppendTasksAsync(StringBuilder sb, string projectId, CancellationToken ct)
    {
        IReadOnlyList<AnytypeV2SearchRow> hits = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Task,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.Projects, [projectId])],
                Fields =
                [
                    DevSpace.Props.Done, DevSpace.Props.Archive,
                    DevSpace.Props.TaskType, DevSpace.Props.Priority,
                ],
            }, ct);

        var taskIds = new List<string>();
        bool any = false;
        foreach (AnytypeV2SearchRow t in hits)
        {
            taskIds.Add(t.Id);
            if (t.Properties.Checkbox(DevSpace.Props.Archive)) continue;
            if (!any) { sb.Append("Tâches :\n"); any = true; }
            AppendTaskLine(sb, t);
        }

        if (!any) sb.Append("Aucune tâche.\n");
        return taskIds;
    }

    // Reports are joined through the tasks: the link lives on the report side
    // (« Tâches »), the project itself has no report link. The server filters
    // on the link and sorts by the report's native creation date, most recent
    // first: the model v6 has no journal-date property. The date shown is the
    // one the report's own heading carries, read with the entry below it.
    async Task AppendRecentReportsAsync(StringBuilder sb, IReadOnlyList<string> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0) return;

        // Sorted newest first by the server: one page of the size shown, not
        // every report of the project.
        AnytypeV2SearchPage page = await _searcher.SearchPageAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Report,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.Tasks, taskIds)],
                Sorts = [new AnytypeV2SearchSort(DevSpace.Native.CreatedDate, Descending: true)],
            }, offset: 0, limit: RecentReports, ct);
        IReadOnlyList<AnytypeV2SearchRow> reports = page.Rows;

        if (reports.Count == 0) return;

        // Each line carries the report id: report_journal reads the rest on demand.
        sb.Append("Derniers rapports :\n");
        foreach (AnytypeV2SearchRow r in reports.Take(RecentReports))
        {
            AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                r.Id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
            string markdown = md.Body["markdown"]?.GetValue<string>() ?? "";
            string date = JournalDay().Match(markdown) is { Success: true } m ? m.Groups[1].Value : "?";
            // The latest entry says where the work stopped; the « # Journal »
            // heading would only repeat the date. Clipped, report_journal reads
            // it whole.
            string? last = MarkdownBody.JournalEntries(markdown)
                .LastOrDefault()?.Split('\n')[0];
            sb.Append(date).Append(" · ").Append(r.Id);
            if (last is not null)
                sb.Append(" — ").Append(last.Length <= LastEntryChars ? last : last[..LastEntryChars] + "…");
            sb.Append('\n');
        }
    }

    [GeneratedRegex(@"Journal (\d{4}-\d{2}-\d{2})")]
    private static partial Regex JournalDay();

    static void AppendProjectLine(StringBuilder sb, AnytypeV2SearchRow p)
    {
        sb.Append(p.Name);

        int? level = DevSpace.Priority.LevelForName(p.Properties.Select(DevSpace.Props.Priority));
        if (level is not null) sb.Append(" · P").Append(level);

        sb.Append('\n');
    }

    static void AppendTaskLine(StringBuilder sb, AnytypeV2SearchRow t)
    {
        sb.Append(t.Properties.Checkbox(DevSpace.Props.Done) ? "[x] " : "[ ] ");
        sb.Append(t.Name);

        string? type = t.Properties.Select(DevSpace.Props.TaskType);
        if (type is not null) sb.Append(" · ").Append(type);

        int? level = DevSpace.Priority.LevelForName(t.Properties.Select(DevSpace.Props.Priority));
        if (level is not null) sb.Append(" · P").Append(level);

        sb.Append('\n');
    }

    static void AppendFact(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append('\n').Append(label).Append(" : ").Append(value);
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}
