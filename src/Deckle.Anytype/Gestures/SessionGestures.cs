using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Session gestures: open a daily report anchored to a task, append journal lines
// to it, and link further tasks to the open report. Anchoring is done from the
// report side — the rapport carries journal date + the linked task(s) in its
// « Tâche(s) liée(s) » property; its project is derived through those tasks.
//
// All schema keys go through Deckle.Anytype.DevSpace — the single source
// of truth for the wire keys, including the trap keys (the malformed
// « tache(s)_liee(s) », the opaque Priorité id).
public sealed class SessionGestures(AnytypeApiClient api, NameResolver resolver)
{
    readonly AnytypeApiClient _api = api;
    readonly NameResolver _resolver = resolver;

    // ── session_start ────────────────────────────────────────────────────────

    public async Task<string> StartAsync(string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string taskId = await _resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);
        JsonObject taskObj = await _api.GetObjectAsync(taskId, ct);

        // Body first line is the title (note layout has no name). Keep it terse —
        // the journal date is its own line so the digest reads it back cheaply.
        string today = Today();
        string body = $"# Journal {today}";

        // The link lives on the report side: the rapport carries the anchor task in
        // « Tâche(s) liée(s) » (it can link several), and its project is derived
        // through those tasks — the report itself has no project property.
        var reportPayload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Rapport,
            ["body"] = body,
            ["properties"] = new JsonArray(
                DateProp(DevSpace.Props.DateDuJournal, today),
                ObjectsProp(DevSpace.Props.TachesLiees, new[] { taskId })),
        };

        JsonObject report = await _api.CreateObjectAsync(reportPayload, ct);
        string reportId = report["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Le rapport créé n'a pas d'id.");

        DeckleAnytypeSource.Log.SessionStarted();
        DeckleAnytypeSource.Log.SessionReportCreated(reportId);

        string digest = await BuildStartDigestAsync(taskObj, ct);
        DeckleAnytypeSource.Log.GestureCompleted("session_start", Elapsed(t0));
        return $"report_id : {reportId}\n{digest}";
    }

    // ── session_log ──────────────────────────────────────────────────────────

    public async Task<string> LogAsync(
        string line, string reportSelector, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        // session_start returns this provider handle. Accepting a report title
        // here would reintroduce ambiguous recovery after a lost append result.
        string reportId = AnytypeObjectId.Require(reportSelector, "report");

        using var _ = await _api.AcquireWriteScopeAsync("session_log", reportId, ct);
        JsonObject report = await _api.GetObjectAsync(reportId, ct);
        string current = report["markdown"]?.GetValue<string>() ?? "";

        // Body markdown PATCH is a full replacement at the API level — append
        // locally then write the whole document back.
        string entry = $"- {line.Trim()}";
        string updated = current.Length == 0 ? entry : current + "\n" + entry;

        await _api.UpdateObjectAsync(reportId, MarkdownPayload(updated), ct);

        DeckleAnytypeSource.Log.GestureCompleted("session_log", Elapsed(t0));
        return $"Noté dans {ReportTitle(report)} : {line.Trim()}";
    }

    // ── session_touch ────────────────────────────────────────────────────────

    public async Task<string> TouchTaskAsync(
        string report, string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string reportId = await _resolver.ResolveAsync(
            report, new[] { DevSpace.Types.Rapport }, ct);
        string taskId = await _resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);
        JsonObject taskObj = await _api.GetObjectAsync(taskId, ct);

        await AppendTaskToReportAsync(reportId, taskId, ct);

        DeckleAnytypeSource.Log.GestureCompleted("session_touch", Elapsed(t0));
        return $"Tâche « {DisplayName(taskObj)} » liée au rapport courant.";
    }

    // ── anchoring ────────────────────────────────────────────────────────────

    // Appends taskId to the report's « Tâche(s) liée(s) ». The objects array is
    // replaced wholesale by the PATCH, so read the report's current ids first;
    // idempotent when the task is already linked.
    async Task AppendTaskToReportAsync(string reportId, string taskId, CancellationToken ct)
    {
        using var _ = await _api.AcquireWriteScopeAsync("session_touch", reportId, ct);
        JsonObject report = await _api.GetObjectAsync(reportId, ct);

        var ids = ReadObjectIds(report, DevSpace.Props.TachesLiees).ToList();
        if (ids.Contains(taskId)) return;
        ids.Add(taskId);

        var payload = new JsonObject
        {
            ["properties"] = new JsonArray(ObjectsProp(DevSpace.Props.TachesLiees, ids)),
        };
        await _api.UpdateObjectAsync(reportId, payload, ct);
    }

    // ── digest ───────────────────────────────────────────────────────────────

    // Task header + body checklist + the last 3 linked reports (journal date +
    // full body — reports are 3-5 lines). Terse, French, no banner lines.
    async Task<string> BuildStartDigestAsync(JsonObject taskObj, CancellationToken ct)
    {
        var sb = new StringBuilder();

        string name = DisplayName(taskObj);
        bool done = ReadCheckbox(taskObj, DevSpace.Props.Done);
        string? type = ReadSelectKey(taskObj, DevSpace.Props.TypeDeTache);
        string? priority = DevSpace.Priority.NameFor(ReadSelectKey(taskObj, DevSpace.Props.Priorite));
        string project = (await ResolveNamesAsync(ReadObjectIds(taskObj, DevSpace.Props.RelationProjet), ct))
            is { Count: > 0 } projects ? string.Join(", ", projects) : "—";

        var header = new List<string> { (done ? "[x] " : "[ ] ") + name };
        if (type is not null) header.Add($"type {type}");
        if (priority is not null) header.Add($"prio {priority}");
        header.Add($"projet {project}");
        sb.Append(string.Join(" · ", header));

        string checklist = Checklist(taskObj["markdown"]?.GetValue<string>());
        if (checklist.Length > 0)
        {
            sb.Append('\n');
            sb.Append(checklist);
        }

        // Reports of this task: the link lives on the report side now, so query
        // reports and keep those whose « Tâche(s) liée(s) » contains this task id,
        // then fetch each for its full body (search hits carry no markdown).
        string taskId = taskObj["id"]?.GetValue<string>() ?? "";
        foreach (JsonObject hit in (await ReportsForTaskAsync(taskId, ct)).Take(3))
        {
            string rid = hit["id"]?.GetValue<string>() ?? "";
            JsonObject report = rid.Length > 0 ? await _api.GetObjectAsync(rid, ct) : hit;
            string date = ReadDate(report, DevSpace.Props.DateDuJournal) ?? "?";
            string rbody = (report["markdown"]?.GetValue<string>() ?? "").Trim();
            sb.Append($"\n— {date}");
            if (rbody.Length > 0) sb.Append('\n').Append(rbody);
        }

        return sb.ToString();
    }

    // Reports linking this task, most-recent journal date first. The link lives on
    // the report side (« Tâche(s) liée(s) »); search has no relation filter, so we
    // page reports and filter client-side (same shape as ProjectGestures).
    async Task<List<JsonObject>> ReportsForTaskAsync(string taskId, CancellationToken ct)
    {
        if (taskId.Length == 0) return [];

        JsonObject root = await _api.SearchAsync(string.Empty, new[] { DevSpace.Types.Rapport }, limit: 200, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var reports = new List<JsonObject>();
        foreach (JsonNode? node in hits)
            if (node is JsonObject r && ReadObjectIds(r, DevSpace.Props.TachesLiees).Contains(taskId))
                reports.Add(r);

        reports.Sort((a, b) => string.CompareOrdinal(
            ReadDate(b, DevSpace.Props.DateDuJournal) ?? "",
            ReadDate(a, DevSpace.Props.DateDuJournal) ?? ""));
        return reports;
    }

    async Task<List<string>> ResolveNamesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var names = new List<string>(ids.Count);
        foreach (string id in ids)
        {
            JsonObject obj = await _api.GetObjectAsync(id, ct);
            names.Add(DisplayName(obj));
        }
        return names;
    }

    // Keep only the markdown checklist lines from the task body — the rest is
    // free notes, noise for the digest.
    static string Checklist(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var lines = markdown.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l =>
            {
                string t = l.TrimStart();
                return t.StartsWith("- [ ]", StringComparison.Ordinal)
                    || t.StartsWith("- [x]", StringComparison.Ordinal)
                    || t.StartsWith("- [X]", StringComparison.Ordinal);
            });
        return string.Join("\n", lines);
    }

    // ── property read helpers (raw API property-array shape) ──────────────────
    // GetObjectAsync returns the inner object node with `properties` as an array
    // of { key, format, <format>: value } — the C# client does not flatten it.

    static JsonObject? FindProp(JsonObject obj, string key)
    {
        if (obj["properties"] is not JsonArray props) return null;
        foreach (var node in props)
            if (node is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        return null;
    }

    static IReadOnlyList<string> ReadObjectIds(JsonObject obj, string key)
    {
        if (FindProp(obj, key)?["objects"] is not JsonArray arr) return Array.Empty<string>();
        var ids = new List<string>(arr.Count);
        foreach (var n in arr)
        {
            // objects entries are raw id strings; tolerate an { id } object shape.
            string? id = n is JsonObject o ? o["id"]?.GetValue<string>() : n?.GetValue<string>();
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    static bool ReadCheckbox(JsonObject obj, string key) =>
        FindProp(obj, key)?["checkbox"]?.GetValue<bool>() ?? false;

    static string? ReadDate(JsonObject obj, string key) =>
        FindProp(obj, key)?["date"]?.GetValue<string>();

    // select values come back either as a bare key string or as { key }.
    static string? ReadSelectKey(JsonObject obj, string key)
    {
        var sel = FindProp(obj, key)?["select"];
        return sel switch
        {
            JsonObject o => o["key"]?.GetValue<string>(),
            JsonValue v => v.GetValue<string>(),
            _ => null,
        };
    }

    // ── property write helpers (API array shape) ──────────────────────────────

    static JsonObject ObjectsProp(string key, IReadOnlyList<string> ids)
    {
        var arr = new JsonArray();
        foreach (string id in ids) arr.Add(JsonValue.Create(id));
        return new JsonObject { ["key"] = key, ["objects"] = arr };
    }

    static JsonObject DateProp(string key, string isoDate) => new()
    {
        ["key"] = key,
        ["date"] = isoDate,
    };

    static JsonObject MarkdownPayload(string markdown) => new()
    {
        ["markdown"] = markdown,
    };

    // ── misc ─────────────────────────────────────────────────────────────────

    static string Today() =>
        DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string DisplayName(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        string? snippet = obj["snippet"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(snippet))
        {
            int nl = snippet.IndexOf('\n');
            return nl >= 0 ? snippet[..nl] : snippet;
        }
        return "(sans titre)";
    }

    static string ReportTitle(JsonObject report)
    {
        string name = DisplayName(report);
        return name == "(sans titre)" ? "le rapport" : $"« {name} »";
    }

    static double Elapsed(long t0) =>
        Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
}
