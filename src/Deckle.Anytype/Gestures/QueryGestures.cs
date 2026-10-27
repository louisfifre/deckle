using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Search, link, update, archive, section edit and idea capture spanning every
// type. Like the other gesture families, each returns a terse French
// plain-string digest. Whole-object reads live on ReadGestures, one per need.
public sealed class QueryGestures(AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2)
{
    readonly LiveTagResolver _liveTags = new(api);
    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);

    // One op per deleted root plus the insert must fit a single atomic batch.
    const int MaxBatchOps = 512;

    // A search preview carries this many meaningful lines: enough to tell two
    // candidates apart, not enough to stand in for a typed read.
    public const int PreviewLines = 3;

    public async Task<string> SearchAsync(
        string text,
        IReadOnlyList<string>? typeKeys = null,
        bool context = false,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        // Stays on the v1 search ON PURPOSE: this tool's output needs the snippet
        // (the « Aperçu » block with context, and the title of a nameless report
        // through QueryProp.Name). A v2 search row is {id,name,type,properties?}
        // with no snippet (measured 2026-10-22), so porting it would silently drop
        // that output. Revisit only if the preview is rebuilt from a v2 markdown
        // read per hit, which costs one request per result.
        JsonObject root = await api.SearchAsync(text, typeKeys, limit: 20, ct: ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var sb = new StringBuilder();
        foreach (JsonNode? node in hits)
        {
            if (node is not JsonObject o) continue;
            string typeKey = QueryProp.TypeKey(o) ?? "?";
            // No id: the typed read that follows a search answers with it first,
            // and a name it cannot settle lists the candidates by id. Here it
            // would cost some sixty characters on every hit.
            sb.Append(typeKey).Append(" · ").Append(QueryProp.Name(o));

            if (context)
            {
                if (typeKey is DevSpace.Types.Project or DevSpace.Types.Task)
                {
                    AppendSearchProperty(sb, o, DevSpace.Props.Description, "Description");
                    AppendSearchProperty(sb, o, DevSpace.Props.DefinitionDeFini, "Définition de fini");
                }

                string snippet = QueryProp.Snippet(o);
                if (snippet.Length > 0)
                    sb.Append('\n').Append("Aperçu :\n").Append(snippet);
            }

            sb.Append('\n');
        }

        string digest = sb.ToString().TrimEnd();
        if (digest.Length == 0) digest = "Aucun résultat.";

        DeckleAnytypeSource.Log.GestureCompleted("search", Elapsed(started));
        return digest;
    }

    static void AppendSearchProperty(StringBuilder sb, JsonObject obj, string key, string label)
    {
        string? value = QueryProp.Render(obj, key);
        if (!string.IsNullOrWhiteSpace(value))
            sb.Append('\n').Append(label).Append(" : ").Append(value.Trim());
    }

    // État remains planning state; completion is the built-in `done` signal for
    // both chantier and task.
    public async Task<string> CompleteAsync(
        string selector, bool value = true, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(
            selector, [DevSpace.Types.Project, DevSpace.Types.Task], NameMatch.Exact, ct);

        using var _ = await api.AcquireWriteScopeAsync("complete", id, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string type = QueryProp.TypeKey(obj) ?? "";
        if (type is not (DevSpace.Types.Project or DevSpace.Types.Task))
            throw new InvalidOperationException(
                "Seuls un chantier (project) ou une tâche portent le signal de fin canonique.");

        var payload = new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Done, ["checkbox"] = value },
            },
        };

        JsonObject updated = await api.UpdateObjectAsync(id, payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("complete", Elapsed(started));

        string name = QueryProp.Name(updated);
        bool project = type == DevSpace.Types.Project;
        return (project, value) switch
        {
            (true, true) => $"Chantier terminé : {name}",
            (true, false) => $"Chantier rouvert : {name}",
            (false, true) => $"Tâche terminée : {name}",
            _ => $"Tâche rouverte : {name}",
        };
    }

    // The API PATCH replaces a property's value wholesale, so this is a
    // read-modify-write: union the current refs with the resolved target ids.
    public async Task<string> LinkAsync(string source, IReadOnlyList<string> targets, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string sourceId = await resolver.ResolveAsync(source, typeKeys: null, NameMatch.Exact, ct);

        using var _ = await api.AcquireWriteScopeAsync("link", sourceId, ct);
        JsonObject sourceObj = await api.GetObjectAsync(sourceId, ct);
        string sourceType = QueryProp.TypeKey(sourceObj) ?? "";

        // The destination property depends on the (source, target) pair, so each
        // target is resolved without a type constraint, its type read, then routed.
        // A mixed batch (e.g. a task linked to a project AND a rapport) groups by
        // destination property and writes each property once.
        var byProp = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var collectionAdds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string t in targets)
        {
            string targetId = await resolver.ResolveAsync(t, typeKeys: null, NameMatch.Exact, ct);
            JsonObject targetObj = await api.GetObjectAsync(targetId, ct);
            string targetType = QueryProp.TypeKey(targetObj) ?? "";

            LinkRoute? route = LinkRouteFor(sourceType, targetType);
            if (route is null)
                throw new InvalidOperationException(SupportedPairsError(sourceType, targetType));

            if (route.PropertyKey is { } propKey)
                (byProp.TryGetValue(propKey, out var list) ? list : byProp[propKey] = []).Add(targetId);
            else
                (collectionAdds.TryGetValue(targetId, out var list) ? list : collectionAdds[targetId] = []).Add(sourceId);
        }

        var entries = new JsonArray();
        int added = 0;
        foreach ((string propKey, List<string> ids) in byProp)
        {
            var union = new List<string>(QueryProp.ObjectRefs(sourceObj, propKey));
            foreach (string id in ids)
            {
                if (!union.Contains(id)) { union.Add(id); added++; }
            }

            var refs = new JsonArray();
            foreach (string id in union) refs.Add(id);
            entries.Add(new JsonObject { ["key"] = propKey, ["objects"] = refs });
        }

        if (entries.Count > 0)
            await api.UpdateObjectAsync(sourceId, new JsonObject { ["properties"] = entries }, ct);

        int collectionRequests = 0;
        foreach ((string collectionId, List<string> ids) in collectionAdds)
        {
            await api.AddToCollectionAsync(collectionId, ids.Distinct(StringComparer.Ordinal).ToArray(), ct);
            collectionRequests++;
        }

        DeckleAnytypeSource.Log.GestureCompleted("link", Elapsed(started));
        string collectionSuffix = collectionRequests > 0
            ? $", {collectionRequests} rattachement(s) collection demandé(s)"
            : "";
        return $"Lié : {QueryProp.Name(sourceObj)} → {targets.Count} cible(s), {added} ajout(s){collectionSuffix}.";
    }

    public async Task<string> UpdateAsync(string selector, string? name, JsonObject? properties, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        // Shape refusals come before any network call.
        bool hasName = name is not null;
        if (hasName && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom ne peut pas être vide.", nameof(name));

        bool hasProps = properties is { Count: > 0 };
        if (!hasName && !hasProps)
            throw new ArgumentException(
                "Rien à mettre à jour : fournissez un nom, des propriétés, ou les deux.", nameof(properties));

        // A replayable update must carry provider identity. In particular, a
        // successful rename makes the original display name unusable on replay.
        string id = AnytypeObjectId.Require(selector, "object");

        using var _ = await api.AcquireWriteScopeAsync("update", id, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string objType = QueryProp.TypeKey(obj) ?? "";

        // rapport and idee are body-titled (empty `name`, title = first line of the
        // body): they have no own name to rewrite. Detection goes by type key
        // because the GET carries no `layout` field.
        if (hasName && (objType == DevSpace.Types.Rapport || objType == DevSpace.Types.Idee))
            throw new InvalidOperationException(
                $"Le type {objType} tire son titre de la première ligne de son corps — " +
                "il n'a pas de nom propre à renommer. Édite le corps (replace_section) plutôt que de passer un nom.");

        var entries = new JsonArray();
        var applied = new List<string>();
        if (hasProps)
            foreach ((string nameOrKey, JsonNode? value) in properties!)
            {
                if (!DevSpace.TryResolveProperty(objType, nameOrKey, out string key, out string format))
                    throw new InvalidOperationException(
                        $"Propriété inconnue « {nameOrKey} » pour le type {objType}. " +
                        $"Connues : {string.Join(", ", DevSpace.PropertiesFor(objType).Select(p => p.Label))}.");

                entries.Add(await BuildEntryAsync(key, format, value, ct));
                applied.Add(DevSpace.PropertyLabel(key) ?? key);
            }

        var payload = new JsonObject();
        if (hasName) payload["name"] = name;
        if (entries.Count > 0) payload["properties"] = entries;
        await api.UpdateObjectAsync(id, payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("update", Elapsed(started));

        // QueryProp.Name(obj) is the pre-PATCH title; the new one is shown
        // explicitly.
        if (hasName)
        {
            string suffix = applied.Count > 0 ? ", " + string.Join(", ", applied) : "";
            return $"Mis à jour : {QueryProp.Name(obj)} (renommé en « {name} »{suffix}).";
        }
        return $"Mis à jour : {QueryProp.Name(obj)} ({string.Join(", ", applied)}).";
    }

    // A lifecycle verb kept distinct from update for the small model's sake,
    // though update can write the same checkbox. Rapport carries no « Archivé »:
    // a report is never archived, it stays searchable.
    public async Task<string> ArchiveAsync(string selector, bool value = true, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, NameMatch.Exact, ct);

        using var _ = await api.AcquireWriteScopeAsync("archive", id, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string objType = QueryProp.TypeKey(obj) ?? "";

        if (!DevSpace.PropertiesFor(objType).Any(p => p.Key == DevSpace.Props.Archive))
            throw new InvalidOperationException(
                $"Le type {objType} ne porte pas de case « Archivé » — rien à archiver.");

        var payload = new JsonObject
        {
            ["properties"] = new JsonArray
            {
                new JsonObject { ["key"] = DevSpace.Props.Archive, ["checkbox"] = value },
            },
        };
        JsonObject updated = await api.UpdateObjectAsync(id, payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("archive", Elapsed(started));

        string name = QueryProp.Name(updated);
        if (name.Length == 0) name = QueryProp.Name(obj);
        return value ? $"Archivé : {name}" : $"Désarchivé : {name}";
    }

    // Replaces the blocks under one heading with block ops, then reads the
    // document back as markdown to confirm the intent and that no other heading
    // was dropped. An absent or ambiguous heading throws before any write, the
    // model-facing error channel.
    //
    // The section is found in the block document (BlockSections: boundary rule,
    // dividers kept) and rewritten as ONE atomic batch: a recursive delete per
    // top-level block of the section, then the new content inserted as markdown
    // right after the heading. Nothing else in the object is touched, so a
    // block another writer added elsewhere survives; a write to the section
    // itself is what If-Match guards (below).
    public async Task<string> ReplaceSectionAsync(
        string selector, string heading, string content, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, NameMatch.Exact, ct);

        using var _ = await api.AcquireWriteScopeAsync("replace_section", id, ct);
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(id, ct: ct);
        string name = NameOf(doc, selector);

        BlockSections.Located found = BlockSections.Locate(doc.Blocks, heading);
        if (found.Outcome == BlockSections.Outcome.NotFound)
            throw new InvalidOperationException(
                $"Section « {heading} » introuvable dans {name}. Titres présents : {HeadingList(found.Headings)}.");
        if (found.Outcome == BlockSections.Outcome.Ambiguous)
            throw new InvalidOperationException(
                $"Section « {heading} » ambiguë dans {name} : {found.MatchCount} titres portent ce texte. " +
                "Renomme les doublons avant de l'éditer.");

        var ops = new List<AnytypeV2Op>(found.RootIds.Count + 1);
        foreach (string rootId in found.RootIds)
            ops.Add(AnytypeV2BlockOps.DeleteBlock(AnytypeV2BlockRef.ById(rootId), recursive: true));
        if (!string.IsNullOrWhiteSpace(content))
            ops.Add(AnytypeV2BlockOps.InsertMarkdown(AnytypeV2Placement.After(found.HeadingId), content));

        if (ops.Count > MaxBatchOps)
            throw new InvalidOperationException(
                $"Section « {heading} » trop longue pour {name} : {found.RootIds.Count} blocs à remplacer " +
                $"en une seule écriture atomique (maximum {MaxBatchOps - 1}). Rien n'a été écrit.");

        // An empty section replaced by nothing has nothing to send; it still
        // goes through the verification below.
        if (ops.Count > 0)
        {
            // The ops are computed from what this read saw (which blocks are the
            // section, where the heading is). If the object changed since, the
            // plan may delete a block that is no longer part of the section, so
            // the write is conditioned on the read's ETag and a 409 refuses it
            // whole: the batch is atomic, nothing was written. A read that gave
            // no ETag sends no condition.
            AnytypeV2WriteOptions? options = doc.ETag is not null
                ? new AnytypeV2WriteOptions { IfMatch = doc.ETag }
                : null;
            try
            {
                await _writer.ApplyObjectOpsAsync(id, ops, options, ct);
            }
            catch (AnytypeV2Exception ex) when (ex.IsStaleEtag)
            {
                throw new InvalidOperationException(
                    $"{name} a changé depuis la lecture : la section « {heading} » n'a pas été remplacée " +
                    "(l'écriture est atomique, rien n'a été écrit). Relance replace_section pour repartir de la version actuelle.");
            }
        }

        DeckleAnytypeSource.Log.GestureCompleted("replace_section", Elapsed(started));

        // The batch committed and cannot be rolled back, so a verification that
        // does not confirm it is reported, not thrown: the caller learns the
        // read-back diverged and can re-read and retry.
        string? why = await VerifySectionAsync(id, heading, content, found.KeptHeadings, ct);
        return why is null
            ? $"Section « {heading} » remplacée dans {name} (vérifié)."
            : $"Section « {heading} » écrite dans {name}, mais vérification en échec : {why}.";
    }

    // Reads the object back as markdown (?format=md) and compares it with the
    // intent through the same normalization as before the port: Anytype's escapes
    // undone, trailing spaces and blank lines ignored. Null when confirmed,
    // otherwise the reason.
    //
    // The md shape is measured on a template and the catalogue document (a JSON
    // object whose "markdown" field holds the text, headings as "#", dividers as
    // " --- "); NOT measured after a block write, nor on headings nested in a
    // column. A body without that field reads as empty and fails the check
    // instead of passing it.
    //
    // Dividers are removed from both sides before comparing: the divider kept
    // before the next heading (BlockSections) sits right after the new content in
    // the md render, and is layout, not part of what was meant to be written.
    async Task<string?> VerifySectionAsync(
        string id, string heading, string content, IReadOnlyList<string> keptHeadings, CancellationToken ct)
    {
        string reread;
        try
        {
            AnytypeV2ObjectDocument md = await _reader.ReadAsync(
                id, new AnytypeV2ObjectReadOptions { Markdown = true }, ct);
            reread = md.Body["markdown"] is JsonValue value && value.TryGetValue(out string? text)
                ? text ?? ""
                : "";
        }
        // HttpClient's own timeout is a TaskCanceledException nobody asked for:
        // the batch is committed all the same, so it is a failed check too.
        catch (Exception ex) when (ex is HttpRequestException
            || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return "la relecture a échoué";
        }

        string seen = WithoutDividers(reread);
        bool intent = MarkdownBody.SectionContentMatches(seen, heading, WithoutDividers(content));

        // Every heading outside the replaced section must still be there, and so
        // must the ones the new content brings. A sub-heading that lived inside
        // the replaced section is removed on purpose and is not in the reference.
        var after = new HashSet<string>(MarkdownBody.HeadingTexts(seen), StringComparer.OrdinalIgnoreCase);
        bool guard = keptHeadings.Concat(MarkdownBody.HeadingTexts(content)).All(after.Contains);

        // A lost heading is named first: the content under it then merges into
        // the replaced section, so the intent check fails too, but the heading
        // is the cause and the damage outside what was asked.
        if (intent && guard) return null;
        return !guard ? "une autre section a disparu" : "le contenu relu ne correspond pas à l'intention";
    }

    static string WithoutDividers(string markdown)
        => string.Join('\n', markdown.Split('\n').Where(line => line.Trim() != "---"));

    // v2 serves the title as the "name" property of the read.
    static string NameOf(AnytypeV2ObjectDocument doc, string fallback)
        => doc.Properties?["name"] is JsonValue value
            && value.TryGetValue(out string? name)
            && !string.IsNullOrEmpty(name)
                ? name
                : fallback;

    public async Task<string> CreateIdeaAsync(string content, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        content = content.Trim();
        bool isShort = content.Length <= 80;
        string name = isShort ? content : FirstWords(content, 80);

        var payload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Idee,
            ["name"] = name,
        };
        if (!isShort) payload["body"] = content;

        JsonObject created = await api.CreateObjectAsync(payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("create_idea", Elapsed(started));
        return $"Idée notée : {name}";
    }

    // ── Internals ─────────────────────────────────────────────────────────

    // A null PropertyKey routes to collection membership on the target.
    sealed record LinkRoute(string? PropertyKey);

    static LinkRoute? LinkRouteFor(string sourceType, string targetType)
    {
        if (sourceType == DevSpace.Types.Task && targetType == DevSpace.Types.Project) return new(DevSpace.Props.RelationProjet);
        if (sourceType == DevSpace.Types.Rapport && targetType == DevSpace.Types.Task) return new(DevSpace.Props.TachesLiees);
        if (sourceType == DevSpace.Types.Project && targetType == DevSpace.Types.Project) return new(DevSpace.Props.DependDe);
        if (sourceType == DevSpace.Types.Project && targetType == DevSpace.Types.Epic) return new(null);
        return null;
    }

    static string SupportedPairsError(string sourceType, string targetType) =>
        $"Liaison non supportée : {sourceType} → {targetType}. " +
        "Paires supportées : tâche→projet, rapport→tâche(s), projet→projet, projet→epic.";

    // The body's headings, quoted, for the « introuvable » error — it tells the
    // model which titles actually exist so it can retry with one of them.
    static string HeadingList(IReadOnlyList<string> headings)
        => headings.Count > 0 ? string.Join(", ", headings.Select(h => $"« {h} »")) : "(aucun)";

    async Task<JsonObject> BuildEntryAsync(string key, string format, JsonNode? value, CancellationToken ct) => format switch
    {
        "number"       => new JsonObject { ["key"] = key, ["number"] = AsNumber(value) },
        "checkbox"     => new JsonObject { ["key"] = key, ["checkbox"] = AsBool(value) },
        "select"       => new JsonObject { ["key"] = key, ["select"] = await ResolveTagAsync(key, AsString(value), ct) },
        "multi_select" => new JsonObject { ["key"] = key, ["multi_select"] = await MultiTagsAsync(key, value, ct) },
        "objects"      => new JsonObject { ["key"] = key, ["objects"] = AsStringArray(value) },
        "date"         => new JsonObject { ["key"] = key, ["date"] = AsString(value) },
        _              => new JsonObject { ["key"] = key, ["text"] = AsString(value) },
    };

    async Task<JsonArray> MultiTagsAsync(string key, JsonNode? value, CancellationToken ct)
    {
        var arr = new JsonArray();
        if (value is JsonArray src)
            foreach (JsonNode? n in src) arr.Add(await ResolveTagAsync(key, AsString(n), ct));
        else if (value is not null)
            arr.Add(await ResolveTagAsync(key, AsString(value), ct));
        return arr;
    }

    // The library cannot mint a tag option: a frozen vocabulary matches in
    // DevSpace, a free (space-managed) one against the live options, and an
    // unknown value throws before any PATCH.
    Task<string> ResolveTagAsync(string key, string value, CancellationToken ct) =>
        DevSpace.HasFrozenVocabulary(key)
            ? Task.FromResult(DevSpace.ResolveTag(key, value))
            : _liveTags.ResolveAsync(key, value, ct);

    static JsonArray AsStringArray(JsonNode? value)
    {
        var arr = new JsonArray();
        if (value is JsonArray src)
            foreach (JsonNode? n in src) { if (n is not null) arr.Add(AsString(n)); }
        else if (value is not null)
            arr.Add(AsString(value));
        return arr;
    }

    // Coercions tolerate the JSON kind the model actually sent: a select value or
    // date may arrive as a string, a number sometimes as its textual form, etc.
    static string AsString(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.TryGetValue(out string? s) => s ?? "",
        _ => value.ToJsonString().Trim('"'),
    };

    static double AsNumber(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue(out double d) => d,
        JsonValue v when v.TryGetValue(out string? s) && double.TryParse(s, out double p) => p,
        _ => 0,
    };

    static bool AsBool(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out string? s) && bool.TryParse(s, out bool p) => p,
        _ => false,
    };

    static string FirstWords(string content, int maxChars)
    {
        string head = content.Length <= maxChars ? content : content[..maxChars];
        int lastSpace = head.LastIndexOf(' ');
        if (lastSpace > 0) head = head[..lastSpace];
        return head.TrimEnd() + "…";
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}

// Reads typed values out of a GET'd object's `properties` array
// ([{key, format, <format>:value}]). `file`-scoped so each gesture file owns its
// own copy with no shared-type collision (see ProjectGestures for the twin).
file static class QueryProp
{
    public static string Name(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        return FirstLine(obj["snippet"]?.GetValue<string>());
    }

    // The preview keeps lines that say something: blank lines, `---` dividers
    // and the template's empty checklist slot are layout, not content.
    public static string Snippet(JsonObject obj)
    {
        string? snippet = obj["snippet"]?.GetValue<string>();
        if (string.IsNullOrEmpty(snippet)) return "";

        var meaningful = MarkdownBody.Clean(snippet).Split('\n')
            .Where(l => !(MarkdownBody.TryChecklistLine(l, out _, out string label) && label.Length == 0))
            .ToList();
        if (meaningful.Count <= QueryGestures.PreviewLines) return string.Join('\n', meaningful);
        return string.Join('\n', meaningful.Take(QueryGestures.PreviewLines)) + " …";
    }

    public static string? TypeKey(JsonObject obj) => obj["type"]?["key"]?.GetValue<string>();

    public static IReadOnlyList<string> ObjectRefs(JsonObject obj, string key)
    {
        if (Prop(obj, key)?["objects"] is not JsonArray arr) return [];
        var ids = new List<string>(arr.Count);
        foreach (JsonNode? n in arr)
            if (n?.GetValue<string>() is { } id) ids.Add(id);
        return ids;
    }

    public static string? Render(JsonObject obj, string key)
    {
        JsonObject? p = Prop(obj, key);
        if (p is null) return null;

        if (p["text"] is JsonValue t) { string s = t.GetValue<string>(); return s.Length > 0 ? s : null; }
        if (p["number"] is JsonValue n) return n.GetValue<double>().ToString();
        if (p["checkbox"] is JsonValue c) return c.GetValue<bool>() ? "oui" : "non";
        if (p["date"] is JsonValue d) { string s = d.GetValue<string>(); return s.Length > 0 ? s : null; }
        if (p["select"] is { } sel) return SelectName(sel);
        if (p["multi_select"] is JsonArray ms) return ms.Count > 0 ? string.Join(", ", MultiNames(ms)) : null;
        if (p["objects"] is JsonArray o) return o.Count > 0 ? $"{o.Count} lien(s)" : null;
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

    static string FirstLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOf('\n');
        return nl < 0 ? s : s[..nl];
    }
}
