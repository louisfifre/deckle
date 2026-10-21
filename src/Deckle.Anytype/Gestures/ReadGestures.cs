using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Typed reads: one gesture per thing an agent needs from an object, named by
// what it returns, in place of a whole-object read. Measured over 45 days of
// transcripts the whole-object read was the context cost of the surface (median
// 1 908 chars, p90 5 664, a document at 23 000) while an agent used a few lines
// of it. Each read here projects the object down to one need and lays it out as
// facts, one per line, the object id first so the next call can address it by id.
//
// Properties shared by several reads (title, links by name) are rendered the
// same way everywhere; dates are trimmed to their day, object links are
// rendered by name, a checkbox is shown only when ticked, since a reader takes
// silence as « non ».
public sealed class ReadGestures(AnytypeApiClient api, NameResolver resolver)
{
    // The task template's two sections. The notes heading is the template's
    // today; « À savoir » is the name proposed for it. Both are read so the
    // template can be renamed in Anytype (the REST API cannot write templates)
    // without a code release in between.
    public const string ChecklistHeading = "Sous-tâches";
    public const string NotesHeading = "Details";
    public static readonly string[] NotesHeadings = ["À savoir", NotesHeading];

    // ── task ─────────────────────────────────────────────────────────────────

    public async Task<string> TaskPropertiesAsync(string task, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(task, DevSpace.Types.Task, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: ReadProp.Checkbox(obj, DevSpace.Props.Done));
        await AppendPropertiesAsync(sb, obj, DevSpace.Types.Task, ct);

        DeckleAnytypeSource.Log.GestureCompleted("task_properties", Elapsed(started));
        return sb.ToString();
    }

    public async Task<string> TaskChecklistAsync(string task, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(task, DevSpace.Types.Task, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: ReadProp.Checkbox(obj, DevSpace.Props.Done));

        IReadOnlyList<MarkdownBody.ChecklistItem> items = MarkdownBody.ChecklistItems(ReadProp.Markdown(obj));
        foreach (MarkdownBody.ChecklistItem item in items)
            sb.Append('\n').Append(item.Done ? "- [x] " : "- [ ] ").Append(item.Label);
        sb.Append(items.Count == 0
            ? "\nAucune sous-tâche."
            : $"\nCochées : {items.Count(i => i.Done)} / {items.Count}");

        DeckleAnytypeSource.Log.GestureCompleted("task_checklist", Elapsed(started));
        return sb.ToString();
    }

    // The notes section of the task body, the checklist left out. A body
    // without the section falls back to its non-checklist lines so a task
    // written outside the template still answers.
    public async Task<string> TaskNotesAsync(string task, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(task, DevSpace.Types.Task, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: ReadProp.Checkbox(obj, DevSpace.Props.Done));

        string body = ReadProp.Markdown(obj);
        string? source = null;
        foreach (string heading in NotesHeadings)
            if (MarkdownBody.TryReadSection(body, heading, out string content)) { source = content; break; }
        // Checklist lines are dropped wherever they sit: task_checklist owns
        // them, and a task written outside the template mixes both.
        string notes = string.Join('\n', (source ?? body).Split('\n')
            .Where(l => !MarkdownBody.TryChecklistLine(l, out _, out _))
            .Where(l => !IsHeading(l, ChecklistHeading)));

        string clean = MarkdownBody.Clean(notes);
        sb.Append('\n').Append(clean.Length > 0 ? clean : "Aucune note.");

        DeckleAnytypeSource.Log.GestureCompleted("task_notes", Elapsed(started));
        return sb.ToString();
    }

    // ── rapport ──────────────────────────────────────────────────────────────

    // A report is read to look back, so its tail is what matters: the last
    // `last` journal entries, the count of older ones stated.
    public async Task<string> ReportJournalAsync(string report, int last = 20, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        if (last < 1) throw new ArgumentException("last doit valoir au moins 1.", nameof(last));
        JsonObject obj = await ReadAsync(report, DevSpace.Types.Rapport, ct);

        var sb = new StringBuilder();
        sb.Append("id : ").Append(ReadProp.Id(obj));
        AppendFact(sb, "Date du journal", ReadProp.Day(obj, DevSpace.Props.DateDuJournal));
        AppendFact(sb, "Tâches liées", await NamesAsync(ReadProp.ObjectRefs(obj, DevSpace.Props.TachesLiees), ct));

        IReadOnlyList<string> entries = MarkdownBody.JournalEntries(ReadProp.Markdown(obj));
        int skipped = Math.Max(0, entries.Count - last);
        if (skipped > 0) sb.Append($"\n… {skipped} entrée(s) avant");
        foreach (string entry in entries.Skip(skipped)) sb.Append('\n').Append(entry);
        if (entries.Count == 0) sb.Append("\nJournal vide.");

        DeckleAnytypeSource.Log.GestureCompleted("report_journal", Elapsed(started));
        return sb.ToString();
    }

    // ── idée ─────────────────────────────────────────────────────────────────

    public async Task<string> IdeaTextAsync(string idea, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(idea, DevSpace.Types.Idee, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: null);
        string text = MarkdownBody.Clean(ReadProp.Markdown(obj));
        if (text.Length > 0) sb.Append('\n').Append(text);
        if (ReadProp.Checkbox(obj, DevSpace.Props.Archive)) sb.Append("\nArchivé : oui");

        DeckleAnytypeSource.Log.GestureCompleted("idea_text", Elapsed(started));
        return sb.ToString();
    }

    // ── document ─────────────────────────────────────────────────────────────

    // The header and the table of sections with their sizes: enough to pick a
    // section before paying for its text.
    public async Task<string> DocumentOutlineAsync(string document, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(document, DevSpace.Types.Document, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: null);
        await AppendPropertiesAsync(sb, obj, DevSpace.Types.Document, ct);

        string body = ReadProp.Markdown(obj);
        int preamble = MarkdownBody.Clean(MarkdownBody.Preamble(body)).Length;
        if (preamble > 0) sb.Append($"\nPréambule : {preamble} car.");
        IReadOnlyList<MarkdownBody.Section> outline = MarkdownBody.Outline(body);
        foreach (MarkdownBody.Section s in outline)
            sb.Append('\n').Append('#', s.Level).Append(' ').Append(s.Heading).Append($" ({s.ContentChars} car.)");
        if (outline.Count == 0 && preamble == 0) sb.Append("\nCorps vide.");

        DeckleAnytypeSource.Log.GestureCompleted("document_outline", Elapsed(started));
        return sb.ToString();
    }

    // One section's text; no heading reads the preamble (the whole body when it
    // has no heading). An absent heading lists the present ones, the retry hint.
    public async Task<string> DocumentSectionAsync(string document, string? heading = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        JsonObject obj = await ReadAsync(document, DevSpace.Types.Document, ct);
        string body = ReadProp.Markdown(obj);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: null);

        string content;
        if (string.IsNullOrWhiteSpace(heading))
        {
            content = MarkdownBody.Preamble(body);
        }
        else if (MarkdownBody.TryReadSection(body, heading, out string section))
        {
            // The heading line is echoed as the document spells it, at its
            // real level, so the reader can pass it back to replace_section.
            string wanted = MarkdownBody.Unescape(heading).Trim();
            MarkdownBody.Section found = MarkdownBody.Outline(body)
                .First(s => s.Heading.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            sb.Append('\n').Append('#', found.Level).Append(' ').Append(found.Heading);
            content = section;
        }
        else
        {
            IReadOnlyList<string> present = MarkdownBody.HeadingTexts(body);
            throw new InvalidOperationException(
                $"Section « {heading} » introuvable dans {ReadProp.Name(obj)}. Titres présents : "
                + (present.Count > 0 ? string.Join(", ", present.Select(h => $"« {h} »")) : "(aucun)") + ".");
        }

        string clean = MarkdownBody.Clean(content);
        sb.Append('\n').Append(clean.Length > 0 ? clean : "Section vide.");

        DeckleAnytypeSource.Log.GestureCompleted("document_section", Elapsed(started));
        return sb.ToString();
    }

    // ── shared rendering ─────────────────────────────────────────────────────

    async Task<JsonObject> ReadAsync(string selector, string typeKey, CancellationToken ct)
    {
        string id = await resolver.ResolveAsync(selector, [typeKey], ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string actual = ReadProp.TypeKey(obj) ?? "";
        if (actual.Length > 0 && actual != typeKey)
            throw new InvalidOperationException(
                $"{ReadProp.Name(obj)} est un objet {actual}, pas {typeKey} : lis-le avec la lecture de son type.");
        return obj;
    }

    // `id : …` then the title, prefixed with the done box when the type has one.
    static void AppendIdentity(StringBuilder sb, JsonObject obj, bool? done)
    {
        sb.Append("id : ").Append(ReadProp.Id(obj));
        sb.Append('\n');
        if (done is bool d) sb.Append(d ? "[x] " : "[ ] ");
        sb.Append(ReadProp.Name(obj));
    }

    // Every mapped property with a value, in the type's digest order. Done is
    // already in the title box; a checkbox shows only when ticked.
    async Task AppendPropertiesAsync(StringBuilder sb, JsonObject obj, string typeKey, CancellationToken ct)
    {
        foreach (PropertyDef def in DevSpace.PropertiesFor(typeKey))
        {
            if (def.Key == DevSpace.Props.Done) continue;
            string? value = def.Format switch
            {
                "objects" => await NamesAsync(ReadProp.ObjectRefs(obj, def.Key), ct),
                "date" => ReadProp.Day(obj, def.Key),
                "checkbox" => ReadProp.Checkbox(obj, def.Key) ? "oui" : null,
                _ => ReadProp.Render(obj, def.Key),
            };
            AppendFact(sb, def.Label, value);
        }

        // Anytype's own bin, distinct from the space's « Archivé » checkbox: a
        // binned object still answers a GET by id.
        if (obj["archived"]?.GetValue<bool>() == true) AppendFact(sb, "Corbeille", "oui");
    }

    // Links are rendered by name: an id tells a reader nothing. One GET per link
    // is the price; links are few on every type.
    async Task<string?> NamesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return null;
        var names = new List<string>(ids.Count);
        foreach (string id in ids)
            names.Add(ReadProp.Name(await api.GetObjectAsync(id, ct)));
        return string.Join(", ", names);
    }

    static void AppendFact(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append('\n').Append(label).Append(" : ").Append(value.Trim());
    }

    static bool IsHeading(string line, string heading)
    {
        string t = line.TrimStart();
        return t.StartsWith("#", StringComparison.Ordinal)
            && MarkdownBody.Unescape(t.TrimStart('#').Trim()).Equals(heading, StringComparison.OrdinalIgnoreCase);
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}

// Reads typed values out of a GET'd object's `properties` array
// ([{key, format, <format>:value}]). `file`-scoped so each gesture file owns its
// own copy with no shared-type collision (see QueryGestures for the twin).
file static class ReadProp
{
    public static string Id(JsonObject obj) => obj["id"]?.GetValue<string>() ?? "";

    public static string Name(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        string? snippet = obj["snippet"]?.GetValue<string>();
        if (string.IsNullOrEmpty(snippet)) return "(sans titre)";
        int nl = snippet.IndexOf('\n');
        return nl < 0 ? snippet : snippet[..nl];
    }

    public static string Markdown(JsonObject obj) => obj["markdown"]?.GetValue<string>() ?? "";

    public static string? TypeKey(JsonObject obj) => obj["type"]?["key"]?.GetValue<string>();

    public static bool Checkbox(JsonObject obj, string key) =>
        Prop(obj, key)?["checkbox"]?.GetValue<bool>() ?? false;

    // The API returns a date with a midnight time suffix; the day is the fact.
    public static string? Day(JsonObject obj, string key)
    {
        string? iso = Prop(obj, key)?["date"]?.GetValue<string>();
        return iso is { Length: >= 10 } ? iso[..10] : null;
    }

    public static IReadOnlyList<string> ObjectRefs(JsonObject obj, string key)
    {
        if (Prop(obj, key)?["objects"] is not JsonArray arr) return [];
        var ids = new List<string>(arr.Count);
        foreach (JsonNode? n in arr)
        {
            string? id = n is JsonObject o ? o["id"]?.GetValue<string>() : n?.GetValue<string>();
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    public static string? Render(JsonObject obj, string key)
    {
        JsonObject? p = Prop(obj, key);
        if (p is null) return null;

        if (p["text"] is JsonValue t) { string s = t.GetValue<string>(); return s.Length > 0 ? s : null; }
        if (p["number"] is JsonValue n) return n.GetValue<double>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (p["select"] is { } sel) return SelectName(sel);
        if (p["multi_select"] is JsonArray ms) return ms.Count > 0 ? string.Join(", ", MultiNames(ms)) : null;
        if (p["files"] is JsonArray f) return f.Count > 0 ? $"{f.Count} fichier(s)" : null;
        return null;
    }

    static string? SelectName(JsonNode sel) => sel switch
    {
        JsonValue v => DevSpace.TagName(v.GetValue<string>()) ?? v.GetValue<string>(),
        JsonObject o => o["name"]?.GetValue<string>() ?? o["key"]?.GetValue<string>(),
        _ => null,
    };

    static IEnumerable<string> MultiNames(JsonArray ms)
    {
        foreach (JsonNode? n in ms)
        {
            string? name = n switch
            {
                JsonValue v => DevSpace.TagName(v.GetValue<string>()) ?? v.GetValue<string>(),
                JsonObject o => o["name"]?.GetValue<string>() ?? o["key"]?.GetValue<string>(),
                _ => null,
            };
            if (name is not null) yield return name;
        }
    }

    static JsonObject? Prop(JsonObject obj, string key)
    {
        if (obj["properties"] is not JsonArray props) return null;
        foreach (JsonNode? n in props)
            if (n is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        return null;
    }
}
