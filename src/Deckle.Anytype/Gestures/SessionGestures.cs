using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype.Api;
using Deckle.Anytype.Schema;

namespace Deckle.Anytype.Gestures;

// Session gestures: open a daily report anchored to a task, append journal lines
// to it, and link further tasks to the open report. Anchoring is done from the
// task side — the rapport carries journal date + project, and each touched task
// gets the report id appended to its « Rapport(s) lié(s) » property.
//
// The "current report" is per-process state: session_log / session_touch with no
// explicit report act on the report opened by session_start in this process.
//
// All schema keys go through Deckle.Anytype.Schema.DevSpace — the single source
// of truth for the wire keys, including the trap keys (the misspelled
// « rpport(s)_lie(s) », the opaque Priorité id).
public sealed class SessionGestures(AnytypeApiClient api, NameResolver resolver)
{
    readonly AnytypeApiClient _api = api;
    readonly NameResolver _resolver = resolver;

    // Per-process: the report opened by the last session_start. Holds across the
    // life of the host process so session_log / session_touch need no id.
    string? _currentReportId;

    // ── session_start ────────────────────────────────────────────────────────

    public async Task<string> StartAsync(string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string taskId = await _resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);
        JsonObject taskObj = await _api.GetObjectAsync(taskId, ct);

        IReadOnlyList<string> projectIds = ReadObjectIds(taskObj, DevSpace.Props.RelationProjet);

        // Body first line is the title (note layout has no name). Keep it terse —
        // the journal date is its own line so the digest reads it back cheaply.
        string today = Today();
        string body = $"# Journal {today}";

        var reportPayload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Rapport,
            ["body"] = body,
            ["properties"] = new JsonArray(
                DateProp(DevSpace.Props.DateDuJournal, today),
                ObjectsProp(DevSpace.Props.RelationProjet, projectIds)),
        };

        JsonObject report = await _api.CreateObjectAsync(reportPayload, ct);
        string reportId = report["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Le rapport créé n'a pas d'id.");

        await AppendReportToTaskAsync(taskObj, reportId, ct);
        _currentReportId = reportId;

        DeckleAnytypeSource.Log.SessionReportCreated(reportId);
        DeckleAnytypeSource.Log.SessionStarted();

        string digest = await BuildStartDigestAsync(taskObj, ct);
        DeckleAnytypeSource.Log.GestureCompleted("session_start", Elapsed(t0));
        return digest;
    }

    // ── session_log ──────────────────────────────────────────────────────────

    public async Task<string> LogAsync(
        string line, string? reportSelector = null, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string reportId;
        if (reportSelector is { Length: > 0 })
            reportId = await _resolver.ResolveAsync(reportSelector, new[] { DevSpace.Types.Rapport }, ct);
        else if (_currentReportId is { Length: > 0 })
            reportId = _currentReportId;
        else
            return "Aucun rapport ouvert dans cette session. Appelle d'abord session_start.";

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

    public async Task<string> TouchTaskAsync(string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        if (_currentReportId is not { Length: > 0 })
            return "Aucun rapport ouvert dans cette session. Appelle d'abord session_start.";

        string taskId = await _resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);
        JsonObject taskObj = await _api.GetObjectAsync(taskId, ct);

        await AppendReportToTaskAsync(taskObj, _currentReportId, ct);

        DeckleAnytypeSource.Log.GestureCompleted("session_touch", Elapsed(t0));
        return $"Tâche « {DisplayName(taskObj)} » liée au rapport courant.";
    }

    // ── anchoring ────────────────────────────────────────────────────────────

    // Appends reportId to the task's « Rapport(s) lié(s) ». The objects array is
    // replaced wholesale by the PATCH, so read the current ids first; idempotent
    // when the report is already linked.
    async Task AppendReportToTaskAsync(JsonObject taskObj, string reportId, CancellationToken ct)
    {
        string taskId = taskObj["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("La tâche n'a pas d'id.");

        var ids = ReadObjectIds(taskObj, DevSpace.Props.RapportsLies).ToList();
        if (ids.Contains(reportId)) return;
        ids.Add(reportId);

        var payload = new JsonObject
        {
            ["properties"] = new JsonArray(ObjectsProp(DevSpace.Props.RapportsLies, ids)),
        };
        await _api.UpdateObjectAsync(taskId, payload, ct);
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

        var reportIds = ReadObjectIds(taskObj, DevSpace.Props.RapportsLies);
        var last3 = reportIds.Skip(Math.Max(0, reportIds.Count - 3)).ToList();
        foreach (string rid in last3)
        {
            JsonObject report = await _api.GetObjectAsync(rid, ct);
            string date = ReadDate(report, DevSpace.Props.DateDuJournal) ?? "?";
            string rbody = (report["markdown"]?.GetValue<string>() ?? "").Trim();
            sb.Append($"\n— {date}");
            if (rbody.Length > 0) sb.Append('\n').Append(rbody);
        }

        return sb.ToString();
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
