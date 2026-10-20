using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Task gestures: create an action-layout task under a project and toggle/append
// inline checklist items in its markdown body. Cross-type lifecycle operations
// (complete and archive) live on QueryGestures.
//
// Subtask round-trip: the body markdown returned by the API is Anytype's raw
// export form. The checklist markers (`- [ ]` / `- [x]`) are not escaped, so a
// full-replacement PATCH preserves the rest verbatim. Labels are not safe:
// Anytype re-escapes _, *, backtick and | on each write, so a label holding
// one drifts after repeated writes, the literal contains-match misses it, and
// the verb then appends a duplicate.

public sealed class TaskGestures(AnytypeApiClient api, NameResolver resolver)
{
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
        JsonObject obj = await api.GetObjectAsync(taskId, ct);
        string objName = NameOf(obj, task);
        string markdown = obj["markdown"]?.GetValue<string>() ?? "";

        var (next, outcome) = ApplyChecklist(markdown, label, done);

        var payload = new JsonObject
        {
            ["markdown"] = next,
        };
        await api.UpdateObjectAsync(taskId, payload, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_subtask", sw.Elapsed.TotalMilliseconds);

        return outcome switch
        {
            ChecklistOutcome.Added    => $"Sous-tâche ajoutée à {objName} : {label}",
            ChecklistOutcome.Checked  => $"Sous-tâche cochée dans {objName} : {label}",
            _                         => $"Sous-tâche décochée dans {objName} : {label}",
        };
    }

    // ── Checklist mechanics ──────────────────────────────────────────────────

    enum ChecklistOutcome { Added, Checked, Unchecked }

    // Finds the unique GFM checklist line whose label contains `label`
    // (case-insensitive) and rewrites its checkbox. Multiple matches are
    // refused; no match adds an item in the requested state. The operation
    // is a set, never a toggle, so replay cannot change the requested state.
    //
    // A new item joins the checklist, not the end of the body: it fills the
    // template's empty slot when one is left, else follows the last item, else
    // follows the checklist heading, and only a body with none of those gets it
    // appended. Appending at the end put subtasks under the notes section.
    static (string Markdown, ChecklistOutcome Outcome) ApplyChecklist(
        string markdown, string label, bool done)
    {
        var lines = markdown.Split('\n').ToList();

        var matches = new List<(int Line, int Box)>();
        int lastItem = -1, emptySlot = -1, heading = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (heading < 0 && IsChecklistHeading(lines[i])) heading = i;
            if (!MarkdownBody.TryChecklistLine(lines[i], out int boxIndex, out string itemLabel))
                continue;

            if (itemLabel.Length == 0) { if (emptySlot < 0) emptySlot = i; continue; }
            lastItem = i;
            if (itemLabel.Contains(label, StringComparison.OrdinalIgnoreCase))
                matches.Add((i, boxIndex));
        }

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Sous-tâche « {label} » ambiguë : {matches.Count} lignes correspondent. "
                + "Utilise un libellé plus précis.");

        if (matches.Count == 1)
        {
            (int line, int boxIndex) = matches[0];
            var sb = new StringBuilder(lines[line]);
            sb[boxIndex] = done ? 'x' : ' ';
            lines[line] = sb.ToString();
            return (string.Join('\n', lines),
                    done ? ChecklistOutcome.Checked : ChecklistOutcome.Unchecked);
        }

        string newItem = $"- [{(done ? 'x' : ' ')}] {label}";
        if (emptySlot >= 0) lines[emptySlot] = newItem;
        else if (lastItem >= 0) lines.Insert(lastItem + 1, newItem);
        else if (heading >= 0) lines.Insert(heading + 1, newItem);
        else if (markdown.Length == 0) return (newItem, ChecklistOutcome.Added);
        else lines.Add(newItem);
        return (string.Join('\n', lines), ChecklistOutcome.Added);
    }

    static bool IsChecklistHeading(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith("#", StringComparison.Ordinal)
            && MarkdownBody.Unescape(t.TrimStart('#').Trim())
                .Equals(ReadGestures.ChecklistHeading, StringComparison.OrdinalIgnoreCase);
    }

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

    static string DisplayType(string typeKey)
        => DevSpace.TypeDeTache.NameFor(typeKey) ?? typeKey;
}
