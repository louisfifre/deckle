using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Project-level gestures: a project's overview digest, the project list grouped
// by état, and creation of projects and their epic containers.
//
// Every digest is a terse French plain-string — the LLM-facing product. One line
// per fact, no banner, no markdown decoration beyond what carries meaning.
//
// Reads go through the v2 client: the lists are filtered by the server (tasks
// by their project, reports by their tasks, projects by état) instead of paging
// the whole space and filtering here. Creation stays on v1.
public sealed class ProjectGestures(
    AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;

    // Reports shown under « Derniers rapports ».
    const int RecentReports = 3;
    // Length of a report's latest entry shown beside it.
    const int LastEntryChars = 120;

    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Searcher _searcher = new(v2);

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
        sb.Append(props.Checkbox(DevSpace.Props.Done) ? "[x] " : "[ ] ");
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

        string? wantedEtat = state is null
            ? null
            : DevSpace.Etat.Resolve(state)
                ?? throw new ArgumentException($"État inconnu « {state} ».", nameof(state));

        // A given état is filtered by the server, by its option name (a select
        // is addressed by name on v2). Archived projects are dropped here: the
        // checkbox is served only when ticked, and how a filter treats its
        // absence is not measured.
        IReadOnlyList<AnytypeV2SearchRow> hits = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Project,
                Filters = wantedEtat is null
                    ? null
                    : [AnytypeV2SearchFilter.In(DevSpace.Props.Etat, [DevSpace.Etat.NameFor(wantedEtat)!])],
                Fields =
                [
                    DevSpace.Props.Done, DevSpace.Props.Archive, DevSpace.Props.Etat,
                    DevSpace.Props.PhaseProjet, DevSpace.Props.Priorite,
                ],
            }, ct);

        var byEtat = new Dictionary<string, List<AnytypeV2SearchRow>>(StringComparer.Ordinal);
        foreach (AnytypeV2SearchRow p in hits)
        {
            if (p.Properties.Checkbox(DevSpace.Props.Archive)) continue;

            // v2 serves the option name; the order and the grouping run on keys.
            string? etatName = p.Properties.Select(DevSpace.Props.Etat);
            string etat = etatName is null ? "" : DevSpace.Etat.Resolve(etatName) ?? etatName;
            if (wantedEtat is not null && etat != wantedEtat) continue;

            (byEtat.TryGetValue(etat, out var list) ? list : byEtat[etat] = []).Add(p);
        }

        var sb = new StringBuilder();
        IEnumerable<string> order = wantedEtat is not null
            ? [wantedEtat]
            : DevSpace.Etat.All.Select(o => o.Key).Where(byEtat.ContainsKey);

        foreach (string etatKey in order)
        {
            if (!byEtat.TryGetValue(etatKey, out var projects) || projects.Count == 0) continue;

            if (wantedEtat is null) sb.Append('\n').Append(DevSpace.Etat.NameFor(etatKey) ?? etatKey).Append('\n');
            foreach (AnytypeV2SearchRow p in projects) AppendProjectLine(sb, p);
        }

        if (wantedEtat is null)
            foreach (var (etat, projects) in byEtat)
                if (DevSpace.Etat.NameFor(etat) is null)
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
                    DevSpace.Props.Done, DevSpace.Props.Archive, DevSpace.Props.Etat,
                    DevSpace.Props.Priorite, DevSpace.Props.DueDate,
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
                    DevSpace.Props.RelationProjet, DevSpace.Props.TypeDeTache, DevSpace.Props.Priorite,
                    DevSpace.Props.DefinitionDeFini, DevSpace.Props.ChargeEstimee,
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
            .Where(p => !p.Properties.Checkbox(DevSpace.Props.Archive) && !p.Properties.Checkbox(DevSpace.Props.Done))
            .Where(p => p.Properties.Select(DevSpace.Props.Etat) is { } etat
                && DevSpace.Etat.Resolve(etat) == DevSpace.Etat.EnCours)
            .ToList();

        sb.Append("\n\nProjets en cours :");
        if (running.Count == 0) sb.Append("\nAucun.");
        foreach (AnytypeV2SearchRow p in running)
        {
            sb.Append('\n').Append(p.Name);
            int? level = DevSpace.Priority.LevelForName(p.Properties.Select(DevSpace.Props.Priorite));
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

        string names = string.Join(", ", t.Properties.ObjectIds(DevSpace.Props.RelationProjet)
            .Select(id => projectNames.TryGetValue(id, out string? n) && n.Length > 0 ? n : "(projet non lu)"));
        if (names.Length > 0) sb.Append("\nProjet : ").Append(names);

        var facts = new List<string>();
        if (t.Properties.Select(DevSpace.Props.TypeDeTache) is { } type) facts.Add(type);
        int? level = DevSpace.Priority.LevelForName(t.Properties.Select(DevSpace.Props.Priorite));
        if (level is not null) facts.Add($"P{level}");
        if (t.Properties.Number(DevSpace.Props.ChargeEstimee) is { } estimate
            && double.TryParse(estimate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hours))
            facts.Add($"{hours.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} h estimée(s)");
        if (facts.Count > 0) sb.Append('\n').Append(string.Join(" · ", facts));

        AppendFact(sb, "Définition de fini", t.Properties.Text(DevSpace.Props.DefinitionDeFini));
    }

    public async Task<string> CreateEpicAsync(
        string name, string? state = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string etatKey = state is null
            ? DevSpace.Etat.EnAttente
            : DevSpace.Etat.Resolve(state)
                ?? throw new ArgumentException($"État inconnu « {state} ».", nameof(state));

        var payload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Epic,
            ["name"] = name,
            ["template_id"] = DevSpace.Templates.Epic,
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Etat, ["select"] = etatKey },
            },
        };

        JsonObject created = await api.CreateObjectAsync(payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("epic_create", Elapsed(started));
        return $"Epic créé : {StringOf(created, "name")} ({DevSpace.Etat.NameFor(etatKey) ?? etatKey})";
    }

    // Epic↔project is collection membership, not a property link.
    public async Task<string> CreateAsync(string name, string? epic = null, string? state = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string etatKey = state is null
            ? DevSpace.Etat.EnAttente
            : DevSpace.Etat.Resolve(state)
                ?? throw new ArgumentException($"État inconnu « {state} ».", nameof(state));

        var payload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Project,
            ["name"] = name,
            ["template_id"] = DevSpace.Templates.Project,
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Etat, ["select"] = etatKey },
            },
        };

        JsonObject created = await api.CreateObjectAsync(payload, ct);
        string projectId = StringOf(created, "id");

        var sb = new StringBuilder();
        sb.Append("Projet créé : ").Append(name);
        sb.Append(" (").Append(DevSpace.Etat.NameFor(etatKey) ?? etatKey).Append(')');

        if (epic is not null)
        {
            string epicId = await resolver.ResolveAsync(
                epic, [DevSpace.Types.Epic], NameMatch.Exact, ct);
            await api.AddToCollectionAsync(epicId, [projectId], ct);
            sb.Append("\nAjouté à l'epic ").Append(StringOf(await api.GetObjectAsync(epicId, ct), "name"));
        }

        DeckleAnytypeSource.Log.GestureCompleted("project_create", Elapsed(started));
        return sb.ToString();
    }

    // ── Digest building ───────────────────────────────────────────────────

    static string StringOf(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";

    static void AppendHeaderFacts(StringBuilder sb, AnytypeV2PropertyMap project)
    {
        AppendFact(sb, "Description", project.Text(DevSpace.Props.Description));
        // v2 serves a select as its option name, ready to show.
        AppendFact(sb, "État", project.Select(DevSpace.Props.Etat));
        AppendFact(sb, "Phase", project.Select(DevSpace.Props.PhaseProjet));

        int? level = DevSpace.Priority.LevelForName(project.Select(DevSpace.Props.Priorite));
        if (level is not null) AppendFact(sb, "Priorité", level.ToString());

        AppendFact(sb, "Version", project.Text(DevSpace.Props.Version));
        AppendFact(sb, "Définition de fini", project.Text(DevSpace.Props.DefinitionDeFini));
        AppendFact(sb, "Date cible", project.Day(DevSpace.Props.DueDate));
    }

    // Returns every related task id for the report join, archived tasks
    // included: archiving a task does not archive the reports linked to it.
    async Task<IReadOnlyList<string>> AppendTasksAsync(StringBuilder sb, string projectId, CancellationToken ct)
    {
        IReadOnlyList<AnytypeV2SearchRow> hits = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Task,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.RelationProjet, [projectId])],
                Fields =
                [
                    DevSpace.Props.Done, DevSpace.Props.Archive,
                    DevSpace.Props.TypeDeTache, DevSpace.Props.Priorite,
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
    // (« Tâche(s) liée(s) »), the project itself has no report link. The server
    // filters on the link and sorts by journal date, most recent first. A search
    // row carries no body, so each shown report costs one markdown read for its
    // first line.
    async Task AppendRecentReportsAsync(StringBuilder sb, IReadOnlyList<string> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0) return;

        // Sorted newest first by the server: one page of the size shown, not
        // every report of the project.
        AnytypeV2SearchPage page = await _searcher.SearchPageAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Rapport,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.TachesLiees, taskIds)],
                Sorts = [new AnytypeV2SearchSort(DevSpace.Props.DateDuJournal, Descending: true)],
                Fields = [DevSpace.Props.DateDuJournal],
            }, offset: 0, limit: RecentReports, ct);
        IReadOnlyList<AnytypeV2SearchRow> reports = page.Rows;

        if (reports.Count == 0) return;

        // Each line carries the report id: report_journal reads the rest on demand.
        sb.Append("Derniers rapports :\n");
        foreach (AnytypeV2SearchRow r in reports.Take(RecentReports))
        {
            string date = r.Properties.Day(DevSpace.Props.DateDuJournal) ?? "?";
            AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                r.Id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
            // The latest entry says where the work stopped; the « # Journal » heading
            // would only repeat the date. Clipped, report_journal reads it whole.
            string? last = MarkdownBody.JournalEntries(md.Body["markdown"]?.GetValue<string>() ?? "")
                .LastOrDefault()?.Split('\n')[0];
            sb.Append(date).Append(" · ").Append(r.Id);
            if (last is not null)
                sb.Append(" — ").Append(last.Length <= LastEntryChars ? last : last[..LastEntryChars] + "…");
            sb.Append('\n');
        }
    }

    static void AppendProjectLine(StringBuilder sb, AnytypeV2SearchRow p)
    {
        sb.Append(p.Properties.Checkbox(DevSpace.Props.Done) ? "[x] " : "[ ] ");
        sb.Append(p.Name);

        string? phase = p.Properties.Select(DevSpace.Props.PhaseProjet);
        if (phase is not null) sb.Append(" · ").Append(phase);

        int? level = DevSpace.Priority.LevelForName(p.Properties.Select(DevSpace.Props.Priorite));
        if (level is not null) sb.Append(" · P").Append(level);

        sb.Append('\n');
    }

    static void AppendTaskLine(StringBuilder sb, AnytypeV2SearchRow t)
    {
        sb.Append(t.Properties.Checkbox(DevSpace.Props.Done) ? "[x] " : "[ ] ");
        sb.Append(t.Name);

        string? type = t.Properties.Select(DevSpace.Props.TypeDeTache);
        if (type is not null) sb.Append(" · ").Append(type);

        int? level = DevSpace.Priority.LevelForName(t.Properties.Select(DevSpace.Props.Priorite));
        if (level is not null) sb.Append(" · P").Append(level);

        sb.Append('\n');
    }

    static void AppendFact(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append('\n').Append(label).Append(" : ").Append(value);
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}
