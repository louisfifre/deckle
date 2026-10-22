using System.Diagnostics;
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
// serves), which is why the match compares both sides unescaped.
//
// task_create is on v2 too: the task is born from its template, and the
// checklist and the notes are then blocks added to the template's own
// sections.
public sealed class TaskGestures(AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2)
{
    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);

    // One insert_blocks run holds 1 to 256 blocks.
    const int MaxBlockRun = 256;

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

        var items = (checklist ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        string text = (notes ?? "").Trim();

        // One insert_blocks takes 256 blocks at most. Said before the create,
        // so a list too long does not leave a half-made task behind.
        if (items.Count > MaxBlockRun)
            throw new ArgumentException(
                $"Trop de sous-tâches à la création : {items.Count} (maximum {MaxBlockRun}). "
                + "Crée la tâche avec les premières, puis ajoute le reste avec task_subtask.",
                nameof(checklist));

        // Properties are a {key: value} map. A select or a relation takes a
        // LIST (schema of POST /objects), and a select option is addressed by
        // its NAME, not by the v1 tag key: a v1 key answers 400 "option does
        // not exist" (measured on PATCH, not on a create). `template` replaces
        // the v1 `template_id`; naming it is reported as source "request"
        // (measured on a dry run).
        var props = new JsonObject
        {
            [DevSpace.Props.RelationProjet] = new JsonArray(projectId),
            [DevSpace.Props.TypeDeTache] = new JsonArray(DisplayType(typeKey)),
        };

        if (priority is int level)
        {
            DevSpace.Priority.KeyFor(level); // refuses a level outside 0-5
            // The options of priorite are named "0" to "5" (measured).
            props[DevSpace.Props.Priorite] = new JsonArray(
                level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var payload = new JsonObject
        {
            ["type"] = DevSpace.Types.Task,
            ["name"] = name,
            ["template"] = DevSpace.Templates.Task,
            ["properties"] = props,
        };

        // The writer sends an Idempotency-Key, so a resend after a 5xx cannot
        // make a second task. The id is read from the receipt's "id" (the
        // schema's CreateResult): the survey only dry-ran creations, so the
        // body of a real create is NOT measured.
        AnytypeV2WriteReceipt receipt = await _writer.CreateObjectAsync(payload, ct: ct);
        string id = receipt.Body?["id"] is JsonValue idValue && idValue.TryGetValue(out string? created)
            ? created ?? ""
            : "";

        // The template put its body there (dividers, the inline dataview, the
        // property blocks, columns), so the two sections are filled by block
        // ops and never by sending the body back: a whole-body write wiped all
        // of it. A task born with neither checklist nor notes is left as the
        // template made it. NOT measured: the body of a task born from the
        // template (the survey read the template object itself) and a typed
        // checkbox given to insert_blocks.
        if (items.Count > 0 || text.Length > 0)
            await FillSectionsAsync(id, items, text, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_create", sw.Elapsed.TotalMilliseconds);

        var facts = new List<string> { $"Tâche créée : {name}", $"id : {id}", $"Type : {DisplayType(typeKey)}" };
        if (priority is int p) facts.Add($"Priorité : {p}");
        if (items.Count > 0) facts.Add($"Sous-tâches : {items.Count}");
        return string.Join('\n', facts);
    }

    // One read of the new task (block ids and ETag), one atomic batch. If it
    // fails the task already exists and the caller must not create it again:
    // the error says so, with the id.
    async Task FillSectionsAsync(
        string id, IReadOnlyList<string> items, string notes, CancellationToken ct)
    {
        try
        {
            if (id.Length == 0)
                throw new InvalidOperationException("l'identifiant de la tâche n'a pas été renvoyé");

            AnytypeV2ObjectDocument doc = await _reader.ReadAsync(id, ct: ct);
            IReadOnlyList<AnytypeV2Op> ops = PlanFill(doc.Blocks, items, notes);

            // The placements come from what the read saw (where the slot and
            // the headings are), so the write carries its ETag: a change in
            // between is a 409 and nothing is written.
            AnytypeV2WriteOptions? options = doc.ETag is not null
                ? new AnytypeV2WriteOptions { IfMatch = doc.ETag }
                : null;
            await _writer.ApplyObjectOpsAsync(id, ops, options, ct);
        }
        // Only the caller's own cancellation escapes. HttpClient's timeout is a
        // TaskCanceledException too: letting it through would lose the "already
        // created" message and invite a duplicate.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            string cause = ex is AnytypeV2Exception { IsStaleEtag: true }
                ? "elle a changé entre sa lecture et l'écriture"
                : ex.Message;
            throw new InvalidOperationException(
                $"Tâche créée (id : {id}) mais ses sections n'ont pas été remplies ({cause}). "
                + "Ne la recrée pas : complète-la avec task_subtask et replace_section.",
                ex);
        }
    }

    // The batch that fills the template's two sections, from the blocks of the
    // new task. Nothing is deleted.
    //
    // Checklist: the first item takes the template's empty checkbox when one is
    // left (an update_block), the rest follow it in ONE insert_blocks so their
    // order is kept. Without a slot they follow the last checkbox, else the
    // checklist heading, else the end (the rule of task_subtask).
    //
    // Notes: markdown inserted right after the notes heading. What the template
    // put in that section stays (it may hold inline queries). The heading is
    // matched as BlockSections does (trimmed, unescaped, case-insensitive: the
    // template's carries a trailing space). Without one, the heading and the
    // notes are appended. The empty slot is left as is when no checklist is
    // given.
    static List<AnytypeV2Op> PlanFill(JsonArray? blocks, IReadOnlyList<string> items, string notes)
    {
        var ops = new List<AnytypeV2Op>();

        if (items.Count > 0)
        {
            ChecklistLayout layout = ScanChecklist(blocks);
            int next = 0;
            AnytypeV2Placement where = layout.AfterChecklist;
            if (layout.EmptySlot is { } slot)
            {
                ops.Add(AnytypeV2BlockOps.UpdateBlock(
                    AnytypeV2BlockRef.ById(slot),
                    new JsonObject { ["text"] = items[0], ["checked"] = false }));
                next = 1;
                where = AnytypeV2Placement.After(slot);
            }

            if (next < items.Count)
                ops.Add(AnytypeV2BlockOps.InsertBlocks(
                    where,
                    items.Skip(next).Select(item => AnytypeV2Block.Create("checkbox", item, false))));
        }

        if (notes.Length > 0)
        {
            BlockSections.Located section = BlockSections.Locate(blocks, ReadGestures.NotesHeading);
            ops.Add(section.Outcome switch
            {
                BlockSections.Outcome.Found => AnytypeV2BlockOps.InsertMarkdown(
                    AnytypeV2Placement.After(section.HeadingId), notes),
                BlockSections.Outcome.NotFound => AnytypeV2BlockOps.InsertMarkdown(
                    AnytypeV2Placement.End, $"## {ReadGestures.NotesHeading}\n{notes}"),
                _ => throw new InvalidOperationException(
                    $"{section.MatchCount} titres « {ReadGestures.NotesHeading} » : la section des notes est ambiguë"),
            });
        }

        return ops;
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
        try
        {
            await _writer.ApplyObjectOpsAsync(taskId, [plan.Op], options, ct);
        }
        catch (AnytypeV2Exception ex) when (ex.IsStaleEtag)
        {
            // Only a new item carries the condition, so only an add lands here.
            throw new InvalidOperationException(
                $"{objName} a changé depuis la lecture : la sous-tâche « {label} » n'a pas été ajoutée " +
                "(rien n'a été écrit). Relance task_subtask pour repartir de la version actuelle.");
        }

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
        ChecklistLayout layout = ScanChecklist(blocks);

        // Anytype escapes markdown in the text it serves (`get\_task` for
        // get_task); the caller types the label naturally, so both sides
        // compare unescaped. Comparing raw never matched a label with an
        // underscore and added a ticked duplicate instead.
        List<string> matches = layout.Items
            .Where(item => MarkdownBody.Unescape(item.Text).Contains(
                MarkdownBody.Unescape(label), StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id)
            .ToList();

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Sous-tâche « {label} » ambiguë : {matches.Count} lignes correspondent. "
                + "Utilise un libellé plus précis.");

        if (matches.Count == 1)
            return new(
                AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById(matches[0]), done),
                DependsOnRead: false,
                done ? ChecklistOutcome.Checked : ChecklistOutcome.Unchecked);

        if (layout.EmptySlot is not null)
            return new(
                AnytypeV2BlockOps.UpdateBlock(
                    AnytypeV2BlockRef.ById(layout.EmptySlot),
                    new JsonObject { ["text"] = label, ["checked"] = done }),
                DependsOnRead: true,
                ChecklistOutcome.Added);

        return new(
            AnytypeV2BlockOps.InsertBlocks(
                layout.AfterChecklist, [AnytypeV2Block.Create("checkbox", label, done)]),
            DependsOnRead: true,
            ChecklistOutcome.Added);
    }

    // What the flat document says about its checklist: the filled checkboxes in
    // reading order, the first empty one (the template's slot) and the first
    // checklist heading. Shared by task_subtask and task_create.
    sealed record ChecklistLayout(
        IReadOnlyList<(string Id, string Text)> Items, string? EmptySlot, string? Heading)
    {
        // A new item joins the checklist, not the end of the body: after the
        // last item, else after the heading, and only a body with neither gets
        // it appended. Appending at the end put subtasks under the notes.
        public AnytypeV2Placement AfterChecklist =>
            Items.Count > 0 ? AnytypeV2Placement.After(Items[^1].Id)
            : Heading is not null ? AnytypeV2Placement.After(Heading)
            : AnytypeV2Placement.End;
    }

    static ChecklistLayout ScanChecklist(JsonArray? blocks)
    {
        var items = new List<(string Id, string Text)>();
        string? emptySlot = null, heading = null;
        foreach (JsonNode? node in blocks ?? [])
        {
            if (node is not JsonObject block || TextOf(block, "id") is not { Length: > 0 } id)
                continue;

            string type = TextOf(block, "type") ?? "";
            string text = (TextOf(block, "text") ?? "").Trim();

            if (heading is null && IsChecklistHeading(type, text)) heading = id;
            if (type != "checkbox") continue;

            if (text.Length == 0) emptySlot ??= id;
            else items.Add((id, text));
        }
        return new(items, emptySlot, heading);
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

    // ── Digest helpers ───────────────────────────────────────────────────────

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
