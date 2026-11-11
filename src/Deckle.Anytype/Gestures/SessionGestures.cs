using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Session gestures: open a daily report anchored to a task, append journal lines
// to it, and link further tasks to the open report. Anchoring is done from the
// report side — the report carries the linked task(s) in its « Tâches »
// property; its project is derived through those tasks, and its date is its
// native creation date.
// open_report and session_log run on API v2; session_touch still goes through v1.
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

    // ── open_report ──────────────────────────────────────────────────────────

    // Opens the report and says nothing of the task's history: the caller has
    // just read the task, and report_journal replays a report on demand. The
    // answer is the handle log needs plus one line naming the task and its
    // project, so a report opened on the wrong task shows at once.
    //
    // Only an id is taken, as log does for a report: the caller has it from
    // task_properties or search, and a name, even matched exactly, can be
    // carried by another task or renamed, so it is no stable anchor for a write.
    public async Task<string> OpenReportAsync(string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string taskId = AnytypeObjectId.Require(task, "task");

        // Every read comes before the create: a failure after it would leave a
        // report the caller never got the id of.
        AnytypeV2ObjectDocument taskDoc = await _reader.ReadAsync(
            taskId, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        var taskProps = AnytypeV2PropertyMap.Of(taskDoc);
        string taskName = taskProps.Name.Length > 0 ? taskProps.Name : "(sans titre)";

        // The id is not resolved by type any more, so the type is checked here:
        // a report linked to anything but a task has no place in the space.
        string actual = taskDoc.Body["type"]?.GetValue<string>() ?? "";
        if (actual.Length > 0 && actual != DevSpace.Types.Task)
            throw new InvalidOperationException(
                $"{taskName} est un objet {actual}, pas une tâche : un rapport s'ouvre sur une tâche.");

        // The project name only helps the person check the task; a report
        // belongs to its task, not to the project. So a project read that fails
        // does not stop the report: the line says the name was not read, and
        // the failed request is already logged by the client.
        string project;
        try
        {
            project = await LinkedNames.JoinAsync(
                _reader, taskProps.ObjectIds(DevSpace.Props.Projects), ct) ?? "—";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            project = "(projet non lu)";
        }

        // Measured on a real create (2026-10-22): the « # Journal <date> »
        // heading stays a heading_1 block, so the report opens on its date and
        // the first session_log finds a body and appends under it. A report is a
        // note: it has no name, and a `name` sent anyway lands as a paragraph
        // ahead of the heading, so none is sent. Properties are a {key: value}
        // map; a relation takes a list of ids. The model v6 has no journal-date
        // property: the report's date is its native creation date, and the
        // heading above carries it for the reader.
        string today = Today();
        var reportPayload = new JsonObject
        {
            ["type"] = DevSpace.Types.Report,
            ["markdown"] = $"# Journal {today}",
            ["properties"] = new JsonObject
            {
                [DevSpace.Props.Tasks] = new JsonArray(taskId),
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

        DeckleAnytypeSource.Log.GestureCompleted("open_report", Elapsed(t0));
        return $"report_id : {reportId}\n{taskName} · {project}";
    }

    // ── session_log ──────────────────────────────────────────────────────────

    public async Task<string> LogAsync(
        string line, string reportSelector, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        // open_report returns this provider handle. Accepting a report title
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
        // entry is already there; the outline is its cheapest shape.
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

        // Three paragraphs: when the entry happened (the part that matters in a
        // journal read days later), what was said, then a spacer paragraph.
        // Markdown drops trailing blank lines, so the spacer is a paragraph
        // holding one no-break space (not measured on a live Anytype yet: if
        // the server trims it, the gap before the next divider is lost).
        string stamp = _time.GetLocalNow().ToString(MarkdownBody.JournalStampFormat, CultureInfo.InvariantCulture);
        // A journal line names keys too: its underscores are escaped like any
        // markdown the server parses (MarkdownBody.PrepareForWrite).
        string entry = $"{stamp}\n\n{MarkdownBody.PrepareForWrite(line.Trim())}\n\n{EntrySpacer}";

        // Anytype puts no space between paragraphs, so one entry's text ran
        // straight into the next entry's stamp. A divider sits between two
        // entries, never under the heading: `---` lands as a divider block
        // (measured), and the journal readers drop it as layout.
        if (HasEntry(report.Outline)) entry = $"---\n\n{entry}";

        string markdown = hasBody
            ? entry
            : $"# Journal {Today()}\n\n{entry}";

        await _writer.ApplyObjectOpsAsync(
            reportId,
            [AnytypeV2BlockOps.InsertMarkdown(AnytypeV2Placement.End, markdown)],
            ct: ct);

        DeckleAnytypeSource.Log.GestureCompleted("session_log", Elapsed(t0));
        // The caller just wrote the line: echoing it back cost 570 000 chars
        // over 45 days of transcripts and told it nothing.
        return "Noté.";
    }

    // ── close_report ─────────────────────────────────────────────────────────

    // Closes a session report: computes its real time by the rule of
    // ReportTime, writes the closing entry carrying that figure, ticks the
    // tasks the person says are done, and sets the real time of every task the
    // report carries, summed again from all their closed reports.
    //
    // Everything that can refuse is checked before the first write: the report
    // id, a correction below zero, a task to tick that the report does not
    // carry. The writes then go in the order that leaves the least to repair:
    // the closing entry first (it holds the figure the sums read), then the
    // ticks, then the sums, which a second close redoes from scratch.
    //
    // A report carrying several tasks gives each an equal share of its time:
    // the model left the split open (« la clôture dit la part de chacune »),
    // and an equal share is the rule a person can check by hand. Proposed, to
    // revisit with use.
    public async Task<string> CloseReportAsync(
        string reportSelector, string summary, IReadOnlyList<string>? done = null,
        double? correctedHours = null, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string reportId = AnytypeObjectId.Require(reportSelector, "report");
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException(
                "L'entrée de clôture ne peut pas être vide : dis ce qui est fait, ce qui reste et par quoi reprendre.",
                nameof(summary));
        if (correctedHours is double c && (c < 0 || double.IsNaN(c) || double.IsInfinity(c)))
            throw new ArgumentException($"Temps corrigé « {correctedHours} » refusé : un nombre d'heures positif.", nameof(correctedHours));

        AnytypeV2ObjectDocument report = await _reader.ReadAsync(
            reportId, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        string actual = report.Body["type"]?.GetValue<string>() ?? "";
        if (actual.Length > 0 && actual != DevSpace.Types.Report)
            throw new InvalidOperationException($"{reportId} est un objet {actual}, pas un rapport : seul un rapport se clôt.");
        IReadOnlyList<string> taskIds = AnytypeV2PropertyMap.Of(report).ObjectIds(DevSpace.Props.Tasks);

        var toTick = new List<string>();
        foreach (string selector in done ?? [])
        {
            string id = await _resolver.ResolveAsync(selector, [DevSpace.Types.Task], NameMatch.Exact, ct);
            if (!taskIds.Contains(id))
                throw new InvalidOperationException(
                    $"« {selector} » n'est pas une tâche de ce rapport : lie-la d'abord (link), puis clos. Rien n'a été écrit.");
            if (!toTick.Contains(id)) toTick.Add(id);
        }

        AnytypeV2ObjectDocument md = await _reader.ReadAsync(
            reportId, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
        var entries = ReportTime.Entries(md.Body["markdown"]?.GetValue<string>() ?? "").ToList();
        // The closing entry is stamped now, so the gap up to it counts unless
        // the last entry was waiting on the person. To the minute, as the
        // stamp log is about to write.
        DateTime now = _time.GetLocalNow().DateTime;
        entries.Add(new ReportTime.Entry(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute)), summary));
        double hours = correctedHours ?? ReportTime.TotalHours(entries);

        await LogAsync(ReportTime.ClosingLine(hours, summary, correctedHours is not null), reportId, ct);

        foreach (string id in toTick)
            await _api.UpdateObjectAsync(id, new JsonObject
            {
                ["properties"] = new JsonArray(new JsonObject { ["key"] = DevSpace.Props.Done, ["checkbox"] = true }),
            }, ct);

        var lines = new List<string>
        {
            $"Rapport clos : temps réel {ReportTime.Format(hours)} h"
                + (correctedHours is not null ? " (corrigé)" : $" sur {entries.Count} entrée(s)") + ".",
        };
        foreach (string taskId in taskIds)
        {
            double taskHours = await TaskRealHoursAsync(taskId, reportId, hours, taskIds.Count, ct);
            await _api.UpdateObjectAsync(taskId, new JsonObject
            {
                ["properties"] = new JsonArray(new JsonObject { ["key"] = DevSpace.Props.TimeActual, ["number"] = taskHours }),
            }, ct);
            string name = await TaskNameAsync(taskId, ct);
            lines.Add($"{(toTick.Contains(taskId) ? "[x]" : "[ ]")} {name} · temps réel {ReportTime.Format(taskHours)} h");
        }

        DeckleAnytypeSource.Log.GestureCompleted("close_report", Elapsed(t0));
        return string.Join('\n', lines);
    }

    // The real time of a task: for each of its reports closed by the rule, the
    // figure of its last closing entry divided by the tasks it carries. The
    // report being closed counts with the figure just computed, not with a
    // read that might not see the entry yet.
    async Task<double> TaskRealHoursAsync(
        string taskId, string closingId, double closingHours, int closingShare, CancellationToken ct)
    {
        IReadOnlyList<AnytypeV2SearchRow> reports = await _searcher.SearchAllAsync(
            new AnytypeV2SearchRequest
            {
                Type = DevSpace.Types.Report,
                Filters = [AnytypeV2SearchFilter.In(DevSpace.Props.Tasks, [taskId])],
                Fields = [DevSpace.Props.Tasks],
            }, ct);

        double total = 0;
        bool sawClosing = false;
        foreach (AnytypeV2SearchRow r in reports)
        {
            int share = Math.Max(1, r.Properties.ObjectIds(DevSpace.Props.Tasks).Count);
            double? recorded;
            if (r.Id == closingId)
            {
                recorded = closingHours;
                sawClosing = true;
            }
            else
            {
                AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                    r.Id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
                recorded = ReportTime.RecordedHours(ReportTime.Entries(md.Body["markdown"]?.GetValue<string>() ?? ""));
            }
            if (recorded is double h) total += h / share;
        }
        // A search that does not list the report yet still counts it once.
        if (!sawClosing) total += closingHours / closingShare;
        return Math.Round(total, 2);
    }

    async Task<string> TaskNameAsync(string taskId, CancellationToken ct)
    {
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
            taskId, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        string name = AnytypeV2PropertyMap.Of(doc).Name;
        return name.Length > 0 ? name : taskId;
    }

    // ── session_touch ────────────────────────────────────────────────────────

    public async Task<string> TouchTaskAsync(
        string report, string task, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string reportId = await _resolver.ResolveAsync(
            report, new[] { DevSpace.Types.Report }, NameMatch.Exact, ct);
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

        var ids = ReadObjectIds(report, DevSpace.Props.Tasks).ToList();
        if (ids.Contains(taskId)) return;
        ids.Add(taskId);

        var payload = new JsonObject
        {
            ["properties"] = new JsonArray(ObjectsProp(DevSpace.Props.Tasks, ids)),
        };
        await _api.UpdateObjectAsync(reportId, payload, ct);
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

    const string EntrySpacer = " ";

    // Any block but a heading is an earlier entry (or the bullets of a report
    // written before the stamped form). A block whose type is not served
    // counts as one: a divider too many costs less than two entries run together.
    static bool HasEntry(JsonArray? outline) =>
        outline is not null
        && outline.Any(block =>
            block?["type"] is not JsonValue type
            || !type.TryGetValue(out string? name)
            || name?.StartsWith("heading", StringComparison.Ordinal) != true);

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
