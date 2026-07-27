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
public sealed class ProjectGestures(AnytypeApiClient api, NameResolver resolver)
{
    // Project header digest + the project's tasks + its last 3 reports. Reports
    // are joined through the tasks — the link lives on the report side now, the
    // project itself has no report link.
    public async Task<string> OverviewAsync(string project, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string projectId = await resolver.ResolveAsync(project, [DevSpace.Types.Project], ct);
        JsonObject obj = await api.GetObjectAsync(projectId, ct);

        var sb = new StringBuilder();
        sb.Append(PropReader.Done(obj) ? "[x] " : "[ ] ");
        sb.Append(PropReader.Name(obj));

        AppendHeaderFacts(sb, obj);

        sb.Append('\n');
        IReadOnlyList<string> taskIds = await AppendTasksAsync(sb, projectId, ct);
        await AppendRecentReportsAsync(sb, taskIds, reportCount: 3, fullBody: false, ct);

        DeckleAnytypeSource.Log.GestureCompleted("project_overview", Elapsed(started));
        return sb.ToString().TrimEnd();
    }

    // state: état tag key or display name (resolved either way). null → every
    // non-archived project, grouped by état in the canonical état order.
    public async Task<string> ListAsync(string? state = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        JsonObject root = await api.SearchAsync(string.Empty, [DevSpace.Types.Project], limit: 200, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        string? wantedEtat = state is null
            ? null
            : DevSpace.Etat.Resolve(state)
                ?? throw new ArgumentException($"État inconnu « {state} ».", nameof(state));

        // Group projects by their état key; keep insertion order per group.
        var byEtat = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (JsonNode? node in hits)
        {
            if (node is not JsonObject p) continue;
            if (PropReader.Checkbox(p, DevSpace.Props.Archive)) continue;

            string etat = PropReader.Select(p, DevSpace.Props.Etat) ?? "";
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
            foreach (JsonObject p in projects) AppendProjectLine(sb, p);
        }

        // Projects whose état is unset or unknown land in a trailing bucket.
        if (wantedEtat is null)
            foreach (var (etat, projects) in byEtat)
                if (DevSpace.Etat.NameFor(etat) is null)
                {
                    sb.Append('\n').Append("Sans état").Append('\n');
                    foreach (JsonObject p in projects) AppendProjectLine(sb, p);
                }

        string digest = sb.ToString().Trim();
        if (digest.Length == 0) digest = "Aucun projet.";

        DeckleAnytypeSource.Log.GestureCompleted("project_list", Elapsed(started));
        return digest;
    }

    // Creates the permanent epic container at the top of the planning model.
    // Epic is a measured custom type in the Dev space; unlike projects and tasks,
    // it carries no default template id.
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
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Etat, ["select"] = etatKey },
            },
        };

        JsonObject created = await api.CreateObjectAsync(payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("epic_create", Elapsed(started));
        return $"Epic créé : {PropReader.Name(created)} ({DevSpace.Etat.NameFor(etatKey) ?? etatKey})";
    }

    // Creates the project (état tag from state or en_attente by default), then,
    // if an epic is given, resolves the epic collection and adds the project to
    // it (epic↔project is collection membership, not a property link).
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
            // The API ignores the type's default template unless we name it; pass
            // it so the project is born with the template's blocks and views.
            ["template_id"] = DevSpace.Templates.Project,
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Etat, ["select"] = etatKey },
            },
        };

        JsonObject created = await api.CreateObjectAsync(payload, ct);
        string projectId = PropReader.Id(created);

        var sb = new StringBuilder();
        sb.Append("Projet créé : ").Append(name);
        sb.Append(" (").Append(DevSpace.Etat.NameFor(etatKey) ?? etatKey).Append(')');

        if (epic is not null)
        {
            string epicId = await resolver.ResolveAsync(epic, [DevSpace.Types.Epic], ct);
            await api.AddToCollectionAsync(epicId, [projectId], ct);
            sb.Append("\nAjouté à l'epic ").Append(PropReader.Name(await api.GetObjectAsync(epicId, ct)));
        }

        DeckleAnytypeSource.Log.GestureCompleted("project_create", Elapsed(started));
        return sb.ToString();
    }

    // ── Digest building ───────────────────────────────────────────────────

    // Header facts, one per line, only when present. Priorité is stored as a
    // tag key; surface its numeric level.
    static void AppendHeaderFacts(StringBuilder sb, JsonObject project)
    {
        AppendFact(sb, "État", DevSpace.Etat.NameFor(PropReader.Select(project, DevSpace.Props.Etat)));
        AppendFact(sb, "Phase", DevSpace.PhaseProjet.NameFor(PropReader.Select(project, DevSpace.Props.PhaseProjet)));

        string? prioKey = PropReader.Select(project, DevSpace.Props.Priorite);
        if (prioKey is not null) AppendFact(sb, "Priorité", DevSpace.Priority.LevelFor(prioKey).ToString());

        AppendFact(sb, "Version", PropReader.Text(project, DevSpace.Props.Version));
        AppendFact(sb, "Définition de fini", PropReader.Text(project, DevSpace.Props.DefinitionDeFini));
    }

    // Appends the project's active task lines and returns every related task id
    // for the report join. Archiving a task removes that task from the active
    // view, but does not archive the separate reports linked to it.
    // Tasks are searched by type then filtered client-side on relation_projet
    // containing this project id — the search API has no relation filter.
    async Task<IReadOnlyList<string>> AppendTasksAsync(StringBuilder sb, string projectId, CancellationToken ct)
    {
        JsonObject root = await api.SearchAsync(string.Empty, [DevSpace.Types.Task], limit: 200, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var taskIds = new List<string>();
        bool any = false;
        foreach (JsonNode? node in hits)
        {
            if (node is not JsonObject t) continue;
            if (!PropReader.ObjectRefs(t, DevSpace.Props.RelationProjet).Contains(projectId)) continue;

            taskIds.Add(PropReader.Id(t));
            if (PropReader.Checkbox(t, DevSpace.Props.Archive)) continue;
            if (!any) { sb.Append("Tâches :\n"); any = true; }
            AppendTaskLine(sb, t);
        }

        if (!any) sb.Append("Aucune tâche.\n");
        return taskIds;
    }

    // Reports of the project = reports linked to any of the project's tasks (the
    // link lives on the report side, « Tâche(s) liée(s) »; the project itself has
    // no report link). Page reports, keep those touching a project task, most
    // recent journal date first.
    async Task AppendRecentReportsAsync(StringBuilder sb, IReadOnlyList<string> taskIds, int reportCount, bool fullBody, CancellationToken ct)
    {
        if (taskIds.Count == 0) return;
        var taskSet = new HashSet<string>(taskIds, StringComparer.Ordinal);

        JsonObject root = await api.SearchAsync(string.Empty, [DevSpace.Types.Rapport], limit: 200, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var reports = new List<JsonObject>();
        foreach (JsonNode? node in hits)
            if (node is JsonObject r && PropReader.ObjectRefs(r, DevSpace.Props.TachesLiees).Any(taskSet.Contains))
                reports.Add(r);

        // Most recent journal date first; missing dates sort last.
        reports.Sort((a, b) => string.CompareOrdinal(
            PropReader.Date(b, DevSpace.Props.DateDuJournal) ?? "",
            PropReader.Date(a, DevSpace.Props.DateDuJournal) ?? ""));

        if (reports.Count == 0) return;

        sb.Append("Derniers rapports :\n");
        foreach (JsonObject r in reports.Take(reportCount))
        {
            string date = PropReader.Date(r, DevSpace.Props.DateDuJournal) ?? "?";
            string body = fullBody ? PropReader.Markdown(r) : PropReader.FirstBodyLine(r);
            sb.Append(date).Append(" — ").Append(body).Append('\n');
        }
    }

    static void AppendProjectLine(StringBuilder sb, JsonObject p)
    {
        sb.Append(PropReader.Done(p) ? "[x] " : "[ ] ");
        sb.Append(PropReader.Name(p));

        string? phase = DevSpace.PhaseProjet.NameFor(PropReader.Select(p, DevSpace.Props.PhaseProjet));
        if (phase is not null) sb.Append(" · ").Append(phase);

        string? prioKey = PropReader.Select(p, DevSpace.Props.Priorite);
        if (prioKey is not null) sb.Append(" · P").Append(DevSpace.Priority.LevelFor(prioKey));

        sb.Append('\n');
    }

    static void AppendTaskLine(StringBuilder sb, JsonObject t)
    {
        // PropReader.Done centralizes the action-layout completion convention.
        sb.Append(PropReader.Done(t) ? "[x] " : "[ ] ");
        sb.Append(PropReader.Name(t));

        string? type = DevSpace.TypeDeTache.NameFor(PropReader.Select(t, DevSpace.Props.TypeDeTache));
        if (type is not null) sb.Append(" · ").Append(type);

        string? prioKey = PropReader.Select(t, DevSpace.Props.Priorite);
        if (prioKey is not null) sb.Append(" · P").Append(DevSpace.Priority.LevelFor(prioKey));

        sb.Append('\n');
    }

    static void AppendFact(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append('\n').Append(label).Append(" : ").Append(value);
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}

// Reads typed values out of a GET'd object's `properties` array, which the API
// returns as [{key, format, <format>:value}]. `file`-scoped on purpose: the same
// reader is needed across gesture files but must stay private to each, so sibling
// files own their own copy with no shared-type collision.
file static class PropReader
{
    public static string Id(JsonObject obj) => obj["id"]?.GetValue<string>() ?? "";

    // Note-layout objects (rapport) carry an empty name; their title is the first
    // snippet line. Mirrors the vendor helper's getDisplayName.
    public static string Name(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        return FirstLine(obj["snippet"]?.GetValue<string>());
    }

    public static string Markdown(JsonObject obj) => obj["markdown"]?.GetValue<string>() ?? "";

    public static string FirstBodyLine(JsonObject obj)
    {
        string md = Markdown(obj);
        return md.Length > 0 ? FirstLine(md) : FirstLine(obj["snippet"]?.GetValue<string>());
    }

    public static string? Text(JsonObject obj, string key) => Prop(obj, key)?["text"]?.GetValue<string>();

    public static string? Select(JsonObject obj, string key)
    {
        JsonNode? sel = Prop(obj, key)?["select"];
        // select can serialize as a bare key string or as {key, name}.
        return sel switch
        {
            JsonValue v => v.GetValue<string>(),
            JsonObject o => o["key"]?.GetValue<string>(),
            _ => null,
        };
    }

    public static string? Date(JsonObject obj, string key) => Prop(obj, key)?["date"]?.GetValue<string>();

    public static bool Checkbox(JsonObject obj, string key) => Prop(obj, key)?["checkbox"]?.GetValue<bool>() ?? false;

    public static IReadOnlyList<string> ObjectRefs(JsonObject obj, string key)
    {
        if (Prop(obj, key)?["objects"] is not JsonArray arr) return [];
        var ids = new List<string>(arr.Count);
        foreach (JsonNode? n in arr)
            if (n?.GetValue<string>() is { } id) ids.Add(id);
        return ids;
    }

    // Done on the action layout: Anytype's built-in action checkbox, key
    // DevSpace.Props.Done. Read-only here; TaskGestures owns writing it.
    public static bool Done(JsonObject obj) => Checkbox(obj, DevSpace.Props.Done);

    static JsonObject? Prop(JsonObject obj, string key)
    {
        if (obj["properties"] is not JsonArray props) return null;
        foreach (JsonNode? n in props)
            if (n is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        return null;
    }

    static string FirstLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOf('\n');
        return nl < 0 ? s : s[..nl];
    }
}
