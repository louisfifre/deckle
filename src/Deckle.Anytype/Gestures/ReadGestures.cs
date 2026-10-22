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
//
// Reads go through the v2 reader. Its shapes were measured live on 2026-10-22:
// `?include=properties` gives the title, the type and the typed properties,
// `?format=md` gives only {etag, id, type, markdown} — no title, no property —
// so a read that needs both (every typed read does: the title and the done box
// head each answer) makes two requests, properties first so a wrong type is
// refused before the body is paid for. Only name resolution (NameResolver)
// stays on v1.
public sealed class ReadGestures(NameResolver resolver, AnytypeV2Client v2)
{
    readonly AnytypeV2ObjectReader _reader = new(v2);

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
        Loaded obj = await LoadAsync(task, DevSpace.Types.Task, withMarkdown: false, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: obj.Props.Checkbox(DevSpace.Props.Done));
        await AppendPropertiesAsync(sb, obj, DevSpace.Types.Task, ct);

        DeckleAnytypeSource.Log.GestureCompleted("task_properties", Elapsed(started));
        return sb.ToString();
    }

    public async Task<string> TaskChecklistAsync(string task, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        Loaded obj = await LoadAsync(task, DevSpace.Types.Task, withMarkdown: true, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: obj.Props.Checkbox(DevSpace.Props.Done));

        IReadOnlyList<MarkdownBody.ChecklistItem> items = MarkdownBody.ChecklistItems(obj.Markdown);
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
        Loaded obj = await LoadAsync(task, DevSpace.Types.Task, withMarkdown: true, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: obj.Props.Checkbox(DevSpace.Props.Done));

        string body = obj.Markdown;
        string? source = null;
        foreach (string heading in NotesHeadings)
            if (MarkdownBody.TryReadSection(body, heading, out string content)) { source = content; break; }
        // Checklist lines are dropped wherever they sit: task_checklist owns
        // them, and a task written outside the template mixes both.
        string notes = string.Join('\n', MarkdownBody.WithoutChecklistLines(source ?? body).Split('\n')
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
        Loaded obj = await LoadAsync(report, DevSpace.Types.Rapport, withMarkdown: true, ct);

        var sb = new StringBuilder();
        sb.Append("id : ").Append(obj.Id);
        AppendFact(sb, "Date du journal", obj.Props.Day(DevSpace.Props.DateDuJournal));
        AppendFact(sb, "Tâches liées", await NamesAsync(obj.Props.ObjectIds(DevSpace.Props.TachesLiees), ct));

        IReadOnlyList<string> entries = MarkdownBody.JournalEntries(obj.Markdown);
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
        Loaded obj = await LoadAsync(idea, DevSpace.Types.Idee, withMarkdown: true, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: null);
        string text = MarkdownBody.Clean(obj.Markdown);
        if (text.Length > 0) sb.Append('\n').Append(text);
        if (obj.Props.Checkbox(DevSpace.Props.Archive)) sb.Append("\nArchivé : oui");

        DeckleAnytypeSource.Log.GestureCompleted("idea_text", Elapsed(started));
        return sb.ToString();
    }

    // ── document ─────────────────────────────────────────────────────────────

    // The header and the table of sections with their sizes: enough to pick a
    // section before paying for its text.
    public async Task<string> DocumentOutlineAsync(string document, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        Loaded obj = await LoadAsync(document, DevSpace.Types.Document, withMarkdown: true, ct);

        var sb = new StringBuilder();
        AppendIdentity(sb, obj, done: null);
        await AppendPropertiesAsync(sb, obj, DevSpace.Types.Document, ct);

        string body = obj.Markdown;
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
        Loaded obj = await LoadAsync(document, DevSpace.Types.Document, withMarkdown: true, ct);
        string body = obj.Markdown;

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
                $"Section « {heading} » introuvable dans {obj.Name}. Titres présents : "
                + (present.Count > 0 ? string.Join(", ", present.Select(h => $"« {h} »")) : "(aucun)") + ".");
        }

        string clean = MarkdownBody.Clean(content);
        sb.Append('\n').Append(clean.Length > 0 ? clean : "Section vide.");

        DeckleAnytypeSource.Log.GestureCompleted("document_section", Elapsed(started));
        return sb.ToString();
    }

    // ── shared rendering ─────────────────────────────────────────────────────

    // An object as the typed reads need it: identity and properties from one v2
    // read, the body from a second when the gesture projects it.
    sealed record Loaded(string Id, string Name, AnytypeV2PropertyMap Props, JsonObject Root, string Markdown);

    // Properties first: they carry the type, so a wrong-typed selector is
    // refused before the body is read. The markdown comes back without the
    // `# <title>` line the v2 render opens with (a task's name is not a block
    // of its body), so the projections see only what the author wrote.
    async Task<Loaded> LoadAsync(string selector, string typeKey, bool withMarkdown, CancellationToken ct)
    {
        string id = await resolver.ResolveAsync(selector, [typeKey], NameMatch.Tolerant, ct);
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
            id, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        var props = AnytypeV2PropertyMap.Of(doc);
        string name = props.Name.Length > 0 ? props.Name : "(sans titre)";

        string actual = doc.Body["type"]?.GetValue<string>() ?? "";
        if (actual.Length > 0 && actual != typeKey)
            throw new InvalidOperationException(
                $"{name} est un objet {actual}, pas {typeKey} : lis-le avec la lecture de son type.");

        string markdown = "";
        if (withMarkdown)
        {
            AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
            markdown = MarkdownBody.DropTitle(md.Body["markdown"]?.GetValue<string>() ?? "", props.Name);
        }
        // Ideas and reports are often created without a name: their first body
        // line stands in for it rather than a bare "(sans titre)".
        if (props.Name.Length == 0 && typeKey is DevSpace.Types.Idee or DevSpace.Types.Rapport)
        {
            string first = MarkdownBody.FirstLine(markdown);
            if (first.Length > 0) name = first;
        }
        return new Loaded(doc.Id ?? id, name, props, doc.Body, markdown);
    }

    // `id : …` then the title, prefixed with the done box when the type has one.
    static void AppendIdentity(StringBuilder sb, Loaded obj, bool? done) =>
        AppendIdentity(sb, obj.Id, obj.Name, done);

    static void AppendIdentity(StringBuilder sb, string id, string name, bool? done)
    {
        sb.Append("id : ").Append(id);
        sb.Append('\n');
        if (done is bool d) sb.Append(d ? "[x] " : "[ ] ");
        sb.Append(name);
    }

    // Every mapped property with a value, in the type's digest order. Done is
    // already in the title box; a checkbox shows only when ticked.
    async Task AppendPropertiesAsync(StringBuilder sb, Loaded obj, string typeKey, CancellationToken ct)
    {
        AnytypeV2PropertyMap props = obj.Props;
        foreach (PropertyDef def in DevSpace.PropertiesFor(typeKey))
        {
            if (def.Key == DevSpace.Props.Done) continue;
            // v2 serves a select as its option name and a multi-select as the
            // names, ready to show (measured 2026-10-22 on the task's état and
            // livrables); the number and files shapes were not measured.
            string? value = def.Format switch
            {
                "objects" => await NamesAsync(props.ObjectIds(def.Key), ct),
                "date" => props.Day(def.Key),
                "checkbox" => props.Checkbox(def.Key) ? "oui" : null,
                "number" => props.Number(def.Key),
                "select" => props.Select(def.Key),
                "multi_select" => JoinOrNull(props.Names(def.Key)),
                "files" => props.Count(def.Key) is > 0 and int n ? $"{n} fichier(s)" : null,
                _ => props.Text(def.Key),
            };
            AppendFact(sb, def.Label, value);
        }

        // Anytype's own bin, distinct from the space's « Archivé » checkbox: a
        // binned object still answers a GET by id, while every v2 search leaves
        // it out unless asked.
        if (props.InBin) AppendFact(sb, "Corbeille", "oui");
    }

    static string? JoinOrNull(IReadOnlyList<string> names) => names.Count > 0 ? string.Join(", ", names) : null;
    // Links are rendered by name: an id tells a reader nothing, and v2 serves
    // only ids.
    Task<string?> NamesAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        LinkedNames.JoinAsync(_reader, ids, ct);

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
