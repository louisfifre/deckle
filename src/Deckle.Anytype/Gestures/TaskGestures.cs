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
    public async Task<string> CreateAsync(
        string project, string name, string type,
        int? priority = null, string? body = null, CancellationToken ct = default)
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
    // refused; no match appends an item in the requested state. The operation
    // is a set, never a toggle, so replay cannot change the requested state.
    static (string Markdown, ChecklistOutcome Outcome) ApplyChecklist(
        string markdown, string label, bool done)
    {
        string[] lines = markdown.Split('\n');

        var matches = new List<(int Line, int Box)>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryParseChecklistLine(lines[i], out int boxIndex, out string itemLabel))
                continue;

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

        string box = done ? "x" : " ";
        string newItem = $"- [{box}] {label}";
        string appended = markdown.Length == 0
            ? newItem
            : markdown.TrimEnd('\n') + "\n" + newItem;
        return (appended, ChecklistOutcome.Added);
    }

    static string RequireChecklistLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Le libellé de sous-tâche ne peut pas être vide.", nameof(label));
        return label.Trim();
    }

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
