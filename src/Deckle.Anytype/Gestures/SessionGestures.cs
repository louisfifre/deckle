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
// session_log is the first gesture on API v2 (an append at the end of the body);
// the others still go through v1.
public sealed class SessionGestures(
    AnytypeApiClient api,
    NameResolver resolver,
    AnytypeV2Client v2,
    TimeProvider? time = null)
{
    readonly AnytypeApiClient _api = api;
    readonly NameResolver _resolver = resolver;
    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);
    readonly AnytypeV2Searcher _searcher = new(v2);
    readonly TimeProvider _time = time ?? TimeProvider.System;

    // ── session_start ────────────────────────────────────────────────────────

    public async Task<string> StartAsync(string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string taskId = await _resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);
        TaskView taskView = await ReadTaskAsync(taskId, ct);

        // Body first line is the title (note layout has no name).
        string today = Today();
        string body = $"# Journal {today}";

        var reportPayload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Rapport,
            ["body"] = body,
            ["properties"] = new JsonArray(
                DateProp(DevSpace.Props.DateDuJournal, today),
                ObjectsProp(DevSpace.Props.TachesLiees, new[] { taskId })),
        };

        // Stays on the v1 create ON PURPOSE: the real v2 create body of a report
        // has never been measured, and every later session_log leans on its
        // result. To port this to AnytypeV2Writer.CreateObjectAsync (type
        // `rapport`, `date_du_journal`, `tache(s)_liee(s)` = [taskId]), check
        // live, on a throwaway task:
        //   1. Title block: does a first « # Journal <date> » heading in the v2
        //      body become the note's title (as in v1, where the body then reads
        //      back empty), stay a visible heading block, or get duplicated next
        //      to a name the server derives? Test with name omitted and with "".
        //   2. Empty-body guard: LogAsync writes the « # Journal <date> » heading
        //      itself when the outline has no block. After the v2 create, read the
        //      outline back: if the heading survived as a block the guard skips it
        //      (fine); if it vanished the guard rewrites it (fine); if it is
        //      doubled or the note is named by the first log line, the guard and
        //      the create body must change together.
        //   3. That the v2 create response carries the new object's id, and that
        //      date and link properties are accepted in the create body.
        JsonObject report = await _api.CreateObjectAsync(reportPayload, ct);
        string reportId = report["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Le rapport créé n'a pas d'id.");

        DeckleAnytypeSource.Log.SessionStarted();
        DeckleAnytypeSource.Log.SessionReportCreated(reportId);

        string digest = await BuildStartDigestAsync(taskId, taskView, ct);
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

        // The entry is appended as new blocks at the end, never as a rewrite of
        // the body: a whole-body PATCH sent every untouched line back through
        // the markdown parser (backslash escapes multiplied on each round) and
        // overwrote whatever another writer had added since the read. An append
        // depends on nothing the read saw, so it carries no If-Match.
        //
        // The read only tells whether the body has any block, and gives the
        // journal date for the empty case; the outline is its cheapest shape.
        AnytypeV2ObjectDocument report = await _reader.ReadAsync(
            reportId,
            new AnytypeV2ObjectReadOptions { Outline = true, Parts = AnytypeV2ObjectParts.Properties },
            ct);

        // Established (v1): right after session_start the report body reads
        // back empty, because Anytype took the creation body's first line
        // « # Journal … » as the note's title; an entry written alone would
        // become the note's first line, hence its display name. NOT measured:
        // whether v2 serves that title as a block. If it does, the outline is
        // not empty and nothing is added; if it does not, the outline is empty
        // and the heading is written first, as in v1. « Has no block » is
        // right either way. Also unmeasured: whether the server omits the
        // outline of an empty body, so a missing one counts as empty.
        bool hasBody = report.Outline is { Count: > 0 };

        // Two paragraphs: when the entry happened (the part that matters in a
        // journal read days later), then what was said.
        string stamp = _time.GetLocalNow().ToString(MarkdownBody.JournalStampFormat, CultureInfo.InvariantCulture);
        string entry = $"{stamp}\n\n{line.Trim()}";
        string markdown = hasBody
            ? entry
            : $"# Journal {await JournalDateAsync(report, reportId, ct)}\n\n{entry}";

        await _writer.ApplyObjectOpsAsync(
            reportId,
            [AnytypeV2BlockOps.InsertMarkdown(AnytypeV2Placement.End, markdown)],
            ct: ct);

        DeckleAnytypeSource.Log.GestureCompleted("session_log", Elapsed(t0));
        // The caller just wrote the line: echoing it back cost 570 000 chars
        // over 45 days of transcripts and told it nothing.
        return "Noté.";
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

    // The PATCH replaces the objects array wholesale, so the report's current
    // ids are read first.
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

    async Task<string> BuildStartDigestAsync(string taskId, TaskView task, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // v2 serves a select as its option name; the digest has always shown the
        // task type by its key, so the name is mapped back (an unknown name is
        // shown as served).
        string? typeName = task.Props.Select(DevSpace.Props.TypeDeTache);
        string? type = typeName is null ? null : DevSpace.TypeDeTache.Resolve(typeName) ?? typeName;
        string? priority = task.Props.Select(DevSpace.Props.Priorite);
        string project = (await ResolveNamesAsync(task.Props.ObjectIds(DevSpace.Props.RelationProjet), ct))
            is { Count: > 0 } projects ? string.Join(", ", projects) : "—";

        var header = new List<string>
        {
            (task.Props.Checkbox(DevSpace.Props.Done) ? "[x] " : "[ ] ") + task.Name,
        };
        if (type is not null) header.Add($"type {type}");
        if (priority is not null) header.Add($"prio {priority}");
        header.Add($"projet {project}");
        sb.Append(string.Join(" · ", header));

        foreach (MarkdownBody.ChecklistItem item in MarkdownBody.ChecklistItems(task.Markdown))
            sb.Append('\n').Append(item.Done ? "- [x] " : "- [ ] ").Append(item.Label);

        // Search rows carry no markdown, so each shown report is read for its
        // body. Only the tail of each is shown: the digest places the session,
        // it does not replay it (report_journal does, on demand, by the id
        // given here).
        foreach (AnytypeV2SearchRow row in (await ReportsForTaskAsync(taskId, ct)).Take(RecentReports))
        {
            string date = row.Properties.Day(DevSpace.Props.DateDuJournal) ?? Today();
            sb.Append($"\n— {date} · {row.Id}");

            AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                row.Id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
            IReadOnlyList<string> entries = MarkdownBody.JournalEntries(md.Body["markdown"]?.GetValue<string>() ?? "");
            int skipped = Math.Max(0, entries.Count - ReportTailEntries);
            if (skipped > 0) sb.Append($"\n… {skipped} entrée(s) avant");
            foreach (string entry in entries.Skip(skipped)) sb.Append('\n').Append(entry);
        }

        return sb.ToString();
    }

    // The task as the digest needs it: title and typed properties from one v2
    // read, the checklist from a second (a markdown read carries neither).
    sealed record TaskView(string Name, AnytypeV2PropertyMap Props, string Markdown);

    async Task<TaskView> ReadTaskAsync(string taskId, CancellationToken ct)
    {
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
            taskId, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        var props = AnytypeV2PropertyMap.Of(doc);
        AnytypeV2ObjectDocument md = await _reader.ReadAsync(
            taskId, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
        string markdown = MarkdownBody.DropTitle(md.Body["markdown"]?.GetValue<string>() ?? "", props.Name);
        return new TaskView(props.Name.Length > 0 ? props.Name : "(sans titre)", props, markdown);
    }

    // Measured: three full reports copied into the digest put session_start at
    // 12 958 chars at p90. Three tails of five entries (one line each when this
    // was measured) keep the thread of the last sessions within a few hundred.
    public const int RecentReports = 3;
    public const int ReportTailEntries = 5;

    // The server filters on the report's link to the task and sorts by journal
    // date, newest first (the structured form measured on 2026-10-22); every
    // page is read, but only the first RecentReports are shown.
    async Task<IReadOnlyList<AnytypeV2SearchRow>> ReportsForTaskAsync(string taskId, CancellationToken ct)
    {
        if (taskId.Length == 0) return [];

        return await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Rapport,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.TachesLiees, [taskId])],
                Sorts = [new AnytypeV2SearchSort(DevSpace.Props.DateDuJournal, Descending: true)],
                Fields = [DevSpace.Props.DateDuJournal],
            }, ct);
    }

    async Task<List<string>> ResolveNamesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var names = new List<string>(ids.Count);
        foreach (string id in ids)
        {
            AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
                id, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
            string name = AnytypeV2PropertyMap.Of(doc).Name;
            names.Add(name.Length > 0 ? name : "(sans titre)");
        }
        return names;
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

    static string? ReadDate(JsonObject obj, string key) =>
        FindProp(obj, key)?["date"]?.GetValue<string>();

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

    // ── misc ─────────────────────────────────────────────────────────────────

    // The API may return « Date du journal » with a time suffix.
    string JournalDate(JsonObject report)
    {
        string? iso = ReadDate(report, DevSpace.Props.DateDuJournal);
        return iso is { Length: >= 10 } ? iso[..10] : Today();
    }

    // v2 serves properties as a map keyed by property key. How a date comes
    // back there is not measured, so a string is read when it is one, and
    // otherwise the date is read the way v1 does, from the v1 object.
    async Task<string> JournalDateAsync(
        AnytypeV2ObjectDocument report, string reportId, CancellationToken ct)
    {
        if (report.Properties?[DevSpace.Props.DateDuJournal] is JsonValue value
            && value.TryGetValue(out string? iso)
            && iso is { Length: >= 10 })
            return iso[..10];

        return JournalDate(await _api.GetObjectAsync(reportId, ct));
    }

    string Today() =>
        _time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    static double Elapsed(long t0) =>
        Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
}
