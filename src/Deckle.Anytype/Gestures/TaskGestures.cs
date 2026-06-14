using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Task gestures: create an action-layout task under a project, mark it done,
// and toggle/append inline checklist items in its markdown body.
//
// The action-layout "done" state is set through the object's `done` checkbox
// property: the vendor reference (anytypeHelper) maps the action/todo layout
// title checkbox to a plain boolean checkbox property, with no dedicated
// endpoint. Measured live 2026-06-12, a completed task carries
// `done [checkbox] = True`; the key is DevSpace.Props.Done.
//
// Subtask round-trip: the body markdown returned by the API is Anytype's raw
// export form. GFM checklist lines (`- [ ]` / `- [x]`) are NOT among the
// characters Anytype escapes on export (only _ * ` |), so contains-matching on
// the label is reliable and a full-replacement PATCH preserves the rest verbatim.

public sealed class TaskGestures(AnytypeApiClient api, NameResolver resolver)
{
    /// <summary>
    /// Creates a task under <paramref name="project"/>.
    /// </summary>
    /// <param name="type">type_de_tache tag KEY or display name (both accepted).</param>
    /// <param name="priority">0-5; null leaves it unset.</param>
    /// <param name="body">Optional markdown body.</param>
    public async Task<string> CreateAsync(
        string project, string name, string type,
        int? priority = null, string? body = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        string projectId = await resolver.ResolveAsync(
            project, new[] { DevSpace.Types.Project }, ct);

        // type accepts a tag key or a display name; resolve both to the wire key.
        string typeKey = DevSpace.TypeDeTache.Resolve(type)
            ?? throw new ArgumentException(
                $"Type de tâche inconnu « {type} ».", nameof(type));

        // The client is thin transport with no property normalization, so the
        // exact API wire shape is built here: properties is an ARRAY of
        // per-format objects ({key, objects|select|checkbox|...}), not a
        // {key: value} map (see anytypeHelper.normalizeProperties).
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
            // The API ignores the type's default template unless we name it; pass
            // it so the task is born with the template's blocks and views.
            ["template_id"] = DevSpace.Templates.Task,
            ["properties"] = props,
        };

        // POST body content lives under "body", not "markdown" (PATCH side uses
        // "markdown") — a wire asymmetry inherited from the Anytype API.
        if (!string.IsNullOrEmpty(body))
            payload["body"] = body;

        JsonObject created = await api.CreateObjectAsync(payload, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_create", sw.Elapsed.TotalMilliseconds);

        string objName = NameOf(created, name);
        string prioritySuffix = priority is int p ? $", priorité {p}" : "";
        return $"Tâche créée : {objName} ({DisplayType(typeKey)}{prioritySuffix})";
    }

    /// <summary>
    /// Marks the task done (sets the action-layout done checkbox to true).
    /// </summary>
    public async Task<string> DoneAsync(string task, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        string taskId = await resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);

        using var _ = await api.AcquireWriteScopeAsync("task_done", taskId, ct);
        var payload = new JsonObject
        {
            ["properties"] = new JsonArray { CheckboxProp(DevSpace.Props.Done, true) },
        };

        JsonObject updated = await api.UpdateObjectAsync(taskId, payload, ct);

        sw.Stop();
        DeckleAnytypeSource.Log.GestureCompleted("task_done", sw.Elapsed.TotalMilliseconds);

        return $"Tâche terminée : {NameOf(updated, task)}";
    }

    /// <summary>
    /// Toggles a checklist item in the task body. A case-insensitive
    /// contains-match on <paramref name="label"/> finds the line; no match
    /// appends a new unchecked item. <paramref name="done"/> forces the target
    /// state (default = mark complete).
    /// </summary>
    public async Task<string> SubtaskAsync(
        string task, string label, bool? done = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        string taskId = await resolver.ResolveAsync(task, new[] { DevSpace.Types.Task }, ct);

        using var _ = await api.AcquireWriteScopeAsync("task_subtask", taskId, ct);
        JsonObject obj = await api.GetObjectAsync(taskId, ct);
        string objName = NameOf(obj, task);
        string markdown = obj["markdown"]?.GetValue<string>() ?? "";

        var (next, outcome) = ApplyChecklist(markdown, label, done);

        // Full-replacement PATCH: the Anytype API has no append for body, so the
        // whole markdown is rewritten (cheap — task bodies are short checklists).
        // PATCH carries body under the top-level "markdown" field, not inside
        // "properties" (vendor updateObject sends it at the request root).
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

    // Finds the first GFM checklist line whose label contains `label`
    // (case-insensitive) and rewrites its checkbox. No match → append a new
    // item. The two paths default differently when `done` is null: a matched
    // item is being completed (→ checked), a new item is being planned
    // (→ unchecked); an explicit `done` forces either path.
    static (string Markdown, ChecklistOutcome Outcome) ApplyChecklist(
        string markdown, string label, bool? done)
    {
        string[] lines = markdown.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryParseChecklistLine(lines[i], out int boxIndex, out string itemLabel))
                continue;

            if (itemLabel.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                bool target = done ?? true;

                // boxIndex points at the char between the brackets.
                var sb = new StringBuilder(lines[i]);
                sb[boxIndex] = target ? 'x' : ' ';
                lines[i] = sb.ToString();
                return (string.Join('\n', lines),
                        target ? ChecklistOutcome.Checked : ChecklistOutcome.Unchecked);
            }
        }

        string box = done == true ? "x" : " ";
        string newItem = $"- [{box}] {label}";
        string appended = markdown.Length == 0
            ? newItem
            : markdown.TrimEnd('\n') + "\n" + newItem;
        return (appended, ChecklistOutcome.Added);
    }

    // Recognizes "- [ ] text" / "- [x] text" (also '*'/'+' bullets, any leading
    // whitespace, upper/lowercase X). On success, boxIndex is the position of the
    // single char inside the brackets and label is the trimmed item text.
    static bool TryParseChecklistLine(string line, out int boxIndex, out string label)
    {
        boxIndex = -1;
        label = "";

        int p = 0;
        while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;

        if (p >= line.Length || (line[p] != '-' && line[p] != '*' && line[p] != '+'))
            return false;
        p++;

        if (p >= line.Length || line[p] != ' ') return false;
        p++;

        if (p + 2 >= line.Length || line[p] != '[' || line[p + 2] != ']')
            return false;

        char box = line[p + 1];
        if (box != ' ' && box != 'x' && box != 'X') return false;

        boxIndex = p + 1;
        label = line[(p + 3)..].Trim();
        return true;
    }

    // ── Wire-shape property builders ─────────────────────────────────────────
    // The API expects each property as {key, <format>: value}. The client does
    // no normalization, so these are built explicitly per format.

    static JsonObject ObjectsProp(string key, string id)
        => new() { ["key"] = key, ["objects"] = new JsonArray(id) };

    static JsonObject SelectProp(string key, string tagKey)
        => new() { ["key"] = key, ["select"] = tagKey };

    static JsonObject CheckboxProp(string key, bool value)
        => new() { ["key"] = key, ["checkbox"] = value };

    // ── Digest helpers ───────────────────────────────────────────────────────

    static string NameOf(JsonObject obj, string fallback)
    {
        string? name = obj["name"]?.GetValue<string>();
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    // Display name of a type_de_tache key for the confirmation line; falls back
    // to the raw key if the option is unknown (should not happen post-resolve).
    static string DisplayType(string typeKey)
        => DevSpace.TypeDeTache.NameFor(typeKey) ?? typeKey;
}
