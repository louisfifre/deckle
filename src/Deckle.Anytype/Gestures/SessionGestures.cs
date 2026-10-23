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
// session_start (create and digest) and session_log run on API v2;
// session_touch still goes through v1.
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

        // The start creates a report on this task, so the name must be exact.
        string taskId = await _resolver.ResolveAsync(
            task, new[] { DevSpace.Types.Task }, NameMatch.Exact, ct);
        TaskView taskView = await ReadTaskAsync(taskId, ct);

        // Every read the digest needs comes BEFORE the create. The create is the
        // last remote call, so a read that fails leaves no report behind: the
        // caller retries the whole gesture, and a report created first would be
        // stacked under a second one on the same task (and the failed call never
        // gave its id back). The digest therefore cannot mention the new report,
        // which also makes the old filter on it unnecessary.
        string digest = await BuildStartDigestAsync(taskId, taskView, ct);

        // Measured on a real create (2026-10-22): the « # Journal <date> »
        // heading stays a heading_1 block, so the report opens on its date and
        // the first session_log finds a body and appends under it. A report is a
        // note: it has no name, and a `name` sent anyway lands as a paragraph
        // ahead of the heading, so none is sent. Properties are a {key: value}
        // map; a relation takes a list of ids, the date a plain yyyy-MM-dd.
        string today = Today();
        var reportPayload = new JsonObject
        {
            ["type"] = DevSpace.Types.Rapport,
            ["markdown"] = $"# Journal {today}",
            ["properties"] = new JsonObject
            {
                [DevSpace.Props.DateDuJournal] = today,
                [DevSpace.Props.TachesLiees] = new JsonArray(taskId),
            },
        };

        // No template is named, so the type's default applies if it has a live
        // one; on the day of the measure it had none and the body was exactly
        // the heading. The writer sends an Idempotency-Key, so a resend after a
        // 5xx cannot open a second report. The id comes back at the root of the
        // answer (measured).
        AnytypeV2WriteReceipt receipt = await _writer.CreateObjectAsync(reportPayload, ct: ct);
        string reportId = receipt.Body?["id"] is JsonValue idValue && idValue.TryGetValue(out string? created)
            && !string.IsNullOrEmpty(created)
                ? created
                : throw new InvalidOperationException("Le rapport créé n'a pas d'id.");

        DeckleAnytypeSource.Log.SessionStarted();
        DeckleAnytypeSource.Log.SessionReportCreated(reportId);

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
        // The read only tells whether the body has any block and whether an
        // entry is already there, and gives the journal date for the empty
        // case; the outline is its cheapest shape.
        AnytypeV2ObjectDocument report = await _reader.ReadAsync(
            reportId,
            new AnytypeV2ObjectReadOptions { Outline = true, Parts = AnytypeV2ObjectParts.Properties },
            ct);

        // A report created on v2 opens on its « # Journal … » heading block
        // (measured), so its outline is never empty and nothing is added. The
        // guard is for the reports created on v1, where Anytype took that line
        // as the note's title and the body read back empty: an entry written
        // alone would become the note's first line, hence its display name.
        // Not measured: whether the server omits the outline of an empty body,
        // so a missing one counts as empty.
        bool hasBody = report.Outline is { Count: > 0 };

        // Two paragraphs: when the entry happened (the part that matters in a
        // journal read days later), then what was said.
        string stamp = _time.GetLocalNow().ToString(MarkdownBody.JournalStampFormat, CultureInfo.InvariantCulture);
        string entry = $"{stamp}\n\n{line.Trim()}";

        // Anytype puts no space between paragraphs, so one entry's text ran
        // straight into the next entry's stamp. A divider sits between two
        // entries, never under the heading: `---` lands as a divider block
        // (measured), and the journal readers drop it as layout.
        if (HasEntry(report.Outline)) entry = $"---\n\n{entry}";

        string markdown = hasBody
            ? entry
            : $"# Journal {JournalDate(report)}\n\n{entry}";

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
            report, new[] { DevSpace.Types.Rapport }, NameMatch.Exact, ct);
        string taskId = await _resolver.ResolveAsync(
            task, new[] { DevSpace.Types.Task }, NameMatch.Exact, ct);
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

    async Task<string> BuildStartDigestAsync(
        string taskId, TaskView task, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // v2 serves a select as its option name; the digest has always shown the
        // task type by its key, so the name is mapped back (an unknown name is
        // shown as served).
        string? typeName = task.Props.Select(DevSpace.Props.TypeDeTache);
        string? type = typeName is null ? null : DevSpace.TypeDeTache.Resolve(typeName) ?? typeName;
        string? priority = task.Props.Select(DevSpace.Props.Priorite);
        string project = await LinkedNames.JoinAsync(
            _reader, task.Props.ObjectIds(DevSpace.Props.RelationProjet), ct) ?? "—";

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
        foreach (AnytypeV2SearchRow row in (await ReportsForTaskAsync(taskId, ct))
            .Take(RecentReports))
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
    // date, newest first (the structured form measured on 2026-10-22), so one
    // page of the size shown is all the digest needs: a task can carry a long
    // history of reports, and paging through it all to keep three was waste.
    async Task<IReadOnlyList<AnytypeV2SearchRow>> ReportsForTaskAsync(string taskId, CancellationToken ct)
    {
        if (taskId.Length == 0) return [];

        AnytypeV2SearchPage page = await _searcher.SearchPageAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Rapport,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.TachesLiees, [taskId])],
                Sorts = [new AnytypeV2SearchSort(DevSpace.Props.DateDuJournal, Descending: true)],
                Fields = [DevSpace.Props.DateDuJournal],
            }, offset: 0, limit: RecentReports, ct);
        return page.Rows;
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

    // ── property write helpers (API array shape) ──────────────────────────────

    static JsonObject ObjectsProp(string key, IReadOnlyList<string> ids)
    {
        var arr = new JsonArray();
        foreach (string id in ids) arr.Add(JsonValue.Create(id));
        return new JsonObject { ["key"] = key, ["objects"] = arr };
    }

    // ── misc ─────────────────────────────────────────────────────────────────

    // Any block but a heading is an earlier entry (or the bullets of a report
    // written before the stamped form). A block whose type is not served
    // counts as one: a divider too many costs less than two entries run together.
    static bool HasEntry(JsonArray? outline) =>
        outline is not null
        && outline.Any(block =>
            block?["type"] is not JsonValue type
            || !type.TryGetValue(out string? name)
            || name?.StartsWith("heading", StringComparison.Ordinal) != true);

    // v2 serves the date as a string with a time suffix,
    // « 2026-10-22T00:00:00Z » (measured on a real report).
    string JournalDate(AnytypeV2ObjectDocument report) =>
        report.Properties?[DevSpace.Props.DateDuJournal] is JsonValue value
            && value.TryGetValue(out string? iso)
            && iso is { Length: >= 10 }
                ? iso[..10]
                : Today();

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
