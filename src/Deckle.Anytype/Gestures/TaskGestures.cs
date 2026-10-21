using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Task gestures: create an action-layout task under a project and set/append
// checklist items in its body. Cross-type lifecycle operations (complete and
// archive) live on QueryGestures.
//
// task_subtask is on API v2: a checklist item is a checkbox block, edited by
// block ops (an update_block or an insert_blocks), never by sending the body
// back. A whole-body PATCH sent every untouched line through the markdown
// parser again, so backslash escapes multiplied on each round, and it
// overwrote whatever another writer had added since the read. Labels still
// come back escaped (Anytype escapes _, *, backtick and | in the text it
// serves), which is why the match compares both sides unescaped. task_create
// still goes through v1.
public sealed class TaskGestures(AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2)
{
    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);

    /// <param name="type">type_de_tache tag KEY or display name (both accepted).</param>
    /// <param name="priority">0-5; null leaves it unset.</param>
    /// <param name="checklist">Subtask labels, one checklist item each.</param>
    /// <param name="notes">Text for the template's notes section.</param>
    public async Task<string> CreateAsync(
        string project, string name, string type,
        int? priority = null, IReadOnlyList<string>? checklist = null, string? notes = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        string projectId = await resolver.ResolveAsync(
            project, new[] { DevSpace.Types.Project }, ct);

        string typeKey = DevSpace.TypeDeTache.Resolve(type)
            ?? throw new ArgumentException(
                $"Type de tâche inconnu « {type} ».", nameof(type));

        // properties is an ARRAY of per-format objects ({key, <format>: value}),
        // not a {key: value} map; the client does no normalization.
        var props = new JsonArray
        {
            ObjectsProp(DevSpace.Props.RelationProjet, projectId),
            SelectProp(DevSpace.Props.TypeDeTache, typeKey),
        };

        if (priority is int level)
            props.Add(SelectProp(DevSpace.Props.Priorite, DevSpace.Priority.KeyFor(level)));

        var payload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Task,
            ["name"] = name,
            ["template_id"] = DevSpace.Templates.Task,
            ["properties"] = props,
        };

        JsonObject created = await api.CreateObjectAsync(payload, ct);
        string id = created["id"]?.GetValue<string>() ?? "";

        // A `body` on the POST lands under the template's last section instead
        // of filling its sections, so the body is written in a second step, in
        // the template's shape, once the template has shaped the object. A
        // task born empty keeps the template body untouched.
        string body = TemplateBody(checklist, notes);
        if (body.Length > 0 && id.Length > 0)
            await api.UpdateObjectAsync(id, new JsonObject { ["markdown"] = body }, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_create", sw.Elapsed.TotalMilliseconds);

        var facts = new List<string> { $"Tâche créée : {NameOf(created, name)}", $"id : {id}", $"Type : {DisplayType(typeKey)}" };
        if (priority is int p) facts.Add($"Priorité : {p}");
        if (checklist is { Count: > 0 }) facts.Add($"Sous-tâches : {checklist.Count}");
        return string.Join('\n', facts);
    }

    // The task template's two sections, filled. Headings are the template's,
    // so task_checklist and task_notes read back what was written here.
    static string TemplateBody(IReadOnlyList<string>? checklist, string? notes)
    {
        var items = (checklist ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        string text = (notes ?? "").Trim();
        if (items.Count == 0 && text.Length == 0) return "";

        var sb = new StringBuilder();
        sb.Append("## ").Append(ReadGestures.ChecklistHeading);
        foreach (string item in items) sb.Append("\n- [ ] ").Append(item);
        sb.Append("\n\n## ").Append(ReadGestures.NotesHeading);
        if (text.Length > 0) sb.Append('\n').Append(text);
        return sb.ToString();
    }

    public async Task<string> SubtaskAsync(
        string task, string label, bool done, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        label = RequireChecklistLabel(label);

        string taskId = await resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);

        using var _ = await api.AcquireWriteScopeAsync("task_subtask", taskId, ct);

        // The whole document is read, not the outline and not one subtree:
        //   • the outline cuts every text to 80 runes and does not say whether
        //     a checkbox is ticked, so a label longer than that could never be
        //     matched on it;
        //   • a ?block= subtree needs the checklist's root, and the checklist
        //     has none: the heading and its items are siblings in the flat
        //     document, so the subtree of the heading is the heading alone. It
        //     would also take a first outline read to find the heading's id,
        //     and the checkboxes the template leaves outside it (in a column,
        //     say) would be missed.
        // The name of the task rides in the properties of the same read. The
        // ETag of this read is the one the write may be conditioned on.
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(taskId, ct: ct);
        string objName = NameOf(doc, task);

        ChecklistPlan plan = PlanChecklist(doc.Blocks, label, done);

        // One batch, so the edit is atomic. If-Match decides who loses a race:
        //   • a NEW item (add, or fill of the empty slot) is placed from what
        //     the read saw — "after the last item", "this slot is still empty".
        //     If another writer changed the checklist since, the anchor is
        //     stale and the fill would overwrite the text they put in the slot,
        //     so the write carries the ETag and fails with a 409 instead.
        //   • a MATCH sets one field of one block named by its id, to an
        //     absolute state: it clobbers nothing another writer did elsewhere
        //     in the body, and repeating it changes nothing. A condition would
        //     only make it fail when a parallel session ticks ANOTHER subtask
        //     of the same task, which is the common case worth surviving. What
        //     it accepts: if the label was edited between the read and the
        //     write, the edited item is the one ticked.
        // A read that gave no ETag sends no condition at all.
        AnytypeV2WriteOptions? options = plan.DependsOnRead && doc.ETag is not null
            ? new AnytypeV2WriteOptions { IfMatch = doc.ETag }
            : null;
        await _writer.ApplyObjectOpsAsync(taskId, [plan.Op], options, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_subtask", sw.Elapsed.TotalMilliseconds);

        return plan.Outcome switch
        {
            ChecklistOutcome.Added    => $"Sous-tâche ajoutée à {objName} : {label}",
            ChecklistOutcome.Checked  => $"Sous-tâche cochée dans {objName} : {label}",
            _                         => $"Sous-tâche décochée dans {objName} : {label}",
        };
    }

    // ── Checklist mechanics ──────────────────────────────────────────────────

    enum ChecklistOutcome { Added, Checked, Unchecked }

    // The one op to send, whether it was computed from what the read saw (a new
    // item) or only names a block by id (a match), and what to answer.
    readonly record struct ChecklistPlan(AnytypeV2Op Op, bool DependsOnRead, ChecklistOutcome Outcome);

    // Finds the unique checkbox block whose label contains `label`
    // (case-insensitive) and sets its state. Multiple matches are refused; no
    // match adds an item in the requested state. The operation is a set, never
    // a toggle, so replay cannot change the requested state.
    //
    // A new item joins the checklist, not the end of the body: it fills the
    // template's empty slot when one is left, else follows the last item, else
    // follows the checklist heading, and only a body with none of those gets it
    // appended. Appending at the end put subtasks under the notes section.
    //
    // The document is flat, in reading order, with an indent per block, so
    // walking the array visits every checkbox wherever it sits: a column or a
    // row nests them (indent 2 is measured on the task template). "After the
    // last item" is after that block's subtree and at its own level, so a
    // nested checklist stays in its column.
    //
    // Measured: a checkbox is a block of type "checkbox" whose text is the
    // label as markup source, with checked:true only when ticked; an empty
    // slot has no text; update_block {id, set:{checked}} ticks it in one call.
    // NOT measured: setting checked to false (a tick is, on a dry run), and an
    // insert_blocks given a typed checkbox block (the dry run measured the
    // markdown form "- [ ] todo"); both are the documented shapes of the
    // schema.
    static ChecklistPlan PlanChecklist(JsonArray? blocks, string label, bool done)
    {
        var matches = new List<string>();
        string? lastItem = null, emptySlot = null, heading = null;
        foreach (JsonNode? node in blocks ?? [])
        {
            if (node is not JsonObject block || TextOf(block, "id") is not { Length: > 0 } id)
                continue;

            string type = TextOf(block, "type") ?? "";
            string text = (TextOf(block, "text") ?? "").Trim();

            if (heading is null && IsChecklistHeading(type, text)) heading = id;
            if (type != "checkbox") continue;

            if (text.Length == 0) { emptySlot ??= id; continue; }
            lastItem = id;
            // Anytype escapes markdown in the text it serves (`get\_task` for
            // get_task); the caller types the label naturally, so both sides
            // compare unescaped. Comparing raw never matched a label with an
            // underscore and added a ticked duplicate instead.
            if (MarkdownBody.Unescape(text).Contains(
                    MarkdownBody.Unescape(label), StringComparison.OrdinalIgnoreCase))
                matches.Add(id);
        }

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Sous-tâche « {label} » ambiguë : {matches.Count} lignes correspondent. "
                + "Utilise un libellé plus précis.");

        if (matches.Count == 1)
            return new(
                AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById(matches[0]), done),
                DependsOnRead: false,
                done ? ChecklistOutcome.Checked : ChecklistOutcome.Unchecked);

        if (emptySlot is not null)
            return new(
                AnytypeV2BlockOps.UpdateBlock(
                    AnytypeV2BlockRef.ById(emptySlot),
                    new JsonObject { ["text"] = label, ["checked"] = done }),
                DependsOnRead: true,
                ChecklistOutcome.Added);

        AnytypeV2Placement where =
            lastItem is not null ? AnytypeV2Placement.After(lastItem)
            : heading is not null ? AnytypeV2Placement.After(heading)
            : AnytypeV2Placement.End;
        return new(
            AnytypeV2BlockOps.InsertBlocks(where, [AnytypeV2Block.Create("checkbox", label, done)]),
            DependsOnRead: true,
            ChecklistOutcome.Added);
    }

    // Any heading level counts, as the markdown "#" did. Toggle headings are
    // not looked at: whether Anytype serves them as headings is not measured.
    static bool IsChecklistHeading(string blockType, string text)
        => (blockType.StartsWith("heading_", StringComparison.Ordinal) || blockType == "header_4")
            && MarkdownBody.Unescape(text)
                .Equals(ReadGestures.ChecklistHeading, StringComparison.OrdinalIgnoreCase);

    static string? TextOf(JsonObject block, string name)
        => block[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    static string RequireChecklistLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Le libellé de sous-tâche ne peut pas être vide.", nameof(label));
        return label.Trim();
    }

    // ── Wire-shape property builders ─────────────────────────────────────────

    static JsonObject ObjectsProp(string key, string id)
        => new() { ["key"] = key, ["objects"] = new JsonArray(id) };

    static JsonObject SelectProp(string key, string tagKey)
        => new() { ["key"] = key, ["select"] = tagKey };

    // ── Digest helpers ───────────────────────────────────────────────────────

    static string NameOf(JsonObject obj, string fallback)
    {
        string? name = obj["name"]?.GetValue<string>();
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    // v2 serves the title as the "name" property of the read.
    static string NameOf(AnytypeV2ObjectDocument doc, string fallback)
        => doc.Properties?["name"] is JsonValue value
            && value.TryGetValue(out string? name)
            && !string.IsNullOrEmpty(name)
                ? name
                : fallback;

    static string DisplayType(string typeKey)
        => DevSpace.TypeDeTache.NameFor(typeKey) ?? typeKey;
}
