using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Generic read/link/update/idea gestures spanning every type. Like the other
// gesture families, each returns a terse French plain-string digest.
public sealed class QueryGestures(AnytypeApiClient api, NameResolver resolver)
{
    // Resolves free-vocabulary (space-managed) select/multi_select values against
    // the live space. Frozen vocabularies are resolved in-memory by DevSpace; this
    // covers the rest without ever letting an unknown option reach the wire.
    readonly LiveTagResolver _liveTags = new(api);

    // Full read: header facts (known schema properties only) + the markdown body.
    public async Task<string> GetAsync(string selector, string? typeKey = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        IReadOnlyList<string>? types = typeKey is null ? null : [typeKey];
        string id = await resolver.ResolveAsync(selector, types, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);

        string objType = QueryProp.TypeKey(obj) ?? "";
        var sb = new StringBuilder();
        sb.Append(QueryProp.Name(obj));
        if (objType.Length > 0) sb.Append(" (").Append(objType).Append(')');

        // Surface every mapped property the object actually carries a value for,
        // in the type's digest order.
        foreach (PropertyDef def in DevSpace.PropertiesFor(objType))
        {
            string? value = QueryProp.Render(obj, def.Key);
            if (value is not null) sb.Append('\n').Append(def.Label).Append(" : ").Append(value);
        }

        string md = QueryProp.Markdown(obj);
        if (md.Length > 0) sb.Append("\n\n").Append(md);

        DeckleAnytypeSource.Log.GestureCompleted("get", Elapsed(started));
        return sb.ToString().TrimEnd();
    }

    // Compact by default: type, name and id. Context mode adds the concise
    // framing properties for project/task hits, then up to five lines of
    // Anytype's bounded body snippet. Name still falls back to the snippet's
    // first line for nameless note-layout objects in either mode.
    public async Task<string> SearchAsync(
        string text,
        IReadOnlyList<string>? typeKeys = null,
        bool context = false,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        JsonObject root = await api.SearchAsync(text, typeKeys, limit: 20, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var sb = new StringBuilder();
        foreach (JsonNode? node in hits)
        {
            if (node is not JsonObject o) continue;
            string typeKey = QueryProp.TypeKey(o) ?? "?";
            sb.Append(typeKey).Append(" · ").Append(QueryProp.Name(o));
            sb.Append(" · ").Append(QueryProp.Id(o));

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

    // Sets the canonical action-layout completion checkbox on a finite chantier
    // or an executable task. État remains planning state; completion is the
    // built-in `done` signal for both object types.
    public async Task<string> CompleteAsync(
        string selector, bool value = true, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(
            selector, [DevSpace.Types.Project, DevSpace.Types.Task], ct);

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

    // Appends targets into the natural link property of the (source type, target
    // type) pair. The API PATCH replaces a property's value wholesale, so this is
    // a read-modify-write: union the current refs with the resolved target ids.
    //
    // Supported pairs (property written on the SOURCE unless noted):
    //   task    → project   : relation_projet
    //   rapport → task      : tache(s)_liee(s)
    //   project → project   : depend_de
    //   project → epic      : collection membership, written on the epic list
    public async Task<string> LinkAsync(string source, IReadOnlyList<string> targets, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string sourceId = await resolver.ResolveAsync(source, typeKeys: null, ct);

        // Hold the space's write lock across the whole read-modify-write: the union
        // is computed from a GET that must not be raced by another session's PATCH
        // before our own lands.
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
            string targetId = await resolver.ResolveAsync(t, typeKeys: null, ct);
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

        // Per property: union the new ids with the existing refs (PATCH replaces a
        // property's value wholesale, so the full set must be re-sent).
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

    // Renames an object and/or sets its properties in ONE PATCH. The name (when
    // given) rides at the payload root, mirroring create; the properties map each
    // display-name-or-key to a schema property key and build the format-typed entry
    // (select/multi_select values resolved name-or-key). At least one of the two
    // must be present, and a blank name is refused — both are shape errors thrown
    // before any network call, ahead of the GET.
    public async Task<string> UpdateAsync(string selector, string? name, JsonObject? properties, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        // Shape refusals first — no GET needed to know the request is empty or the
        // name is blank, so refuse before resolving the selector or hitting the wire.
        bool hasName = name is not null;
        if (hasName && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom ne peut pas être vide.", nameof(name));

        bool hasProps = properties is { Count: > 0 };
        if (!hasName && !hasProps)
            throw new ArgumentException(
                "Rien à mettre à jour : fournissez un nom, des propriétés, ou les deux.", nameof(properties));

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);

        using var _ = await api.AcquireWriteScopeAsync("update", id, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string objType = QueryProp.TypeKey(obj) ?? "";

        // rapport and idee are body-titled (empty `name`, title = first line of the
        // body): they have no own name to rewrite. Refuse a rename on them and point
        // at replace_section, the body-editing gesture. Type-key detection — the GET
        // carries no `layout` field, and these are exactly the two types the module
        // already treats as body-titled.
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

        // One composed PATCH: name at the root (as create writes it), the resolved
        // property entries when any exist — never two round-trips.
        var payload = new JsonObject();
        if (hasName) payload["name"] = name;
        if (entries.Count > 0) payload["properties"] = entries;
        await api.UpdateObjectAsync(id, payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("update", Elapsed(started));

        // QueryProp.Name(obj) is the pre-PATCH title (the GET ran before the rename);
        // that frames « object X was updated », and the new title is shown explicitly.
        if (hasName)
        {
            string suffix = applied.Count > 0 ? ", " + string.Join(", ", applied) : "";
            return $"Mis à jour : {QueryProp.Name(obj)} (renommé en « {name} »{suffix}).";
        }
        return $"Mis à jour : {QueryProp.Name(obj)} ({string.Join(", ", applied)}).";
    }

    // Sets the transversal « Archivé » checkbox to take an object out of the views
    // (value true) or bring it back (false). A lifecycle verb kept distinct from
    // update for the small model's sake, though update can write the same checkbox.
    // The checkbox lives on almost every type but NOT on rapport (a report is never
    // archived — it stays searchable); a type that does not carry it is refused with
    // the schema-true message rather than sending a no-op PATCH.
    public async Task<string> ArchiveAsync(string selector, bool value = true, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);

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

    // Replaces the body under a markdown heading and verifies the write landed.
    // The body PATCH is a full replacement (Anytype has no block-level REST edit),
    // so this reads the current body, splices the one targeted section
    // (MarkdownBody keeps every other section verbatim), PATCHes the whole
    // document, then reads it back to confirm the intent: the section now carries
    // the new content (compared normalized, since Anytype re-escapes and reflows on
    // export) and no other heading was dropped. Strict — an absent or ambiguous
    // heading throws before any write, the model-facing error channel.
    public async Task<string> ReplaceSectionAsync(
        string selector, string heading, string content, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);

        using var _ = await api.AcquireWriteScopeAsync("replace_section", id, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string body = QueryProp.Markdown(obj);
        string name = QueryProp.Name(obj);

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, heading, content);
        if (edit.Status == MarkdownBody.EditStatus.NotFound)
            throw new InvalidOperationException(
                $"Section « {heading} » introuvable dans {name}. Titres présents : {HeadingList(body)}.");
        if (edit.Status == MarkdownBody.EditStatus.Ambiguous)
            throw new InvalidOperationException(
                $"Section « {heading} » ambiguë dans {name} : {edit.MatchCount} titres portent ce texte. " +
                "Renomme les doublons avant de l'éditer.");

        JsonObject patched = await api.UpdateObjectAsync(
            id, new JsonObject { ["markdown"] = edit.Body }, ct);

        // Read-after-write. The PATCH response already carries the re-rendered body
        // when Anytype echoes it (free); otherwise read once more. Either way the
        // read-back is normalized, never the bytes we sent.
        string reread = QueryProp.Markdown(patched);
        if (reread.Length == 0)
            reread = QueryProp.Markdown(await api.GetObjectAsync(id, ct));

        DeckleAnytypeSource.Log.GestureCompleted("replace_section", Elapsed(started));

        // Guard against the splice (or Anytype's re-serialization) dropping a
        // section: every heading the spliced document MEANT to keep must still be
        // there. The reference is the intended body, not the original — a sub-heading
        // inside the replaced section is removed on purpose and must not read as loss.
        bool intent = MarkdownBody.SectionContentMatches(reread, heading, content);
        var after = new HashSet<string>(MarkdownBody.HeadingTexts(reread), StringComparer.OrdinalIgnoreCase);
        bool guard = MarkdownBody.HeadingTexts(edit.Body).All(after.Contains);

        if (intent && guard)
            return $"Section « {heading} » remplacée dans {name} (vérifié).";

        // The PATCH committed — a full-replacement write cannot be rolled back — so
        // the divergence is reported, not thrown: the caller learns the read-back
        // did not confirm the intent and can re-read and retry.
        string why = !intent ? "le contenu relu ne correspond pas à l'intention" : "une autre section a disparu";
        return $"Section « {heading} » écrite dans {name}, mais vérification en échec : {why}.";
    }

    // Captures a quick idea. Short content (≤80 chars) becomes the name; longer
    // content keeps its first words as the name and the full text as the body.
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

    // The natural link property for a (source, target) type pair, or null if the
    // pair is unsupported. Matrix is exhaustive — see the brief.
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
    static string HeadingList(string body)
    {
        IReadOnlyList<string> headings = MarkdownBody.HeadingTexts(body);
        return headings.Count > 0 ? string.Join(", ", headings.Select(h => $"« {h} »")) : "(aucun)";
    }

    // select/multi_select are async because a free (space-managed) vocabulary is
    // resolved against the live space; every other format is built in-memory.
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

    // A select/multi_select value resolves to an EXISTING option's wire key, never
    // a fresh one: a frozen vocabulary matches in DevSpace, a free (space-managed)
    // one against the live options. Either way an unknown value throws (listing the
    // valid options) before any PATCH — the library cannot mint a tag option.
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
    public static string Id(JsonObject obj) => obj["id"]?.GetValue<string>() ?? "";

    public static string Name(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        return FirstLine(obj["snippet"]?.GetValue<string>());
    }

    public static string Markdown(JsonObject obj) => obj["markdown"]?.GetValue<string>() ?? "";

    public static string Snippet(JsonObject obj) =>
        FirstLines(obj["snippet"]?.GetValue<string>(), 5);

    // The object's type key. The API nests it as {type:{key,...}}.
    public static string? TypeKey(JsonObject obj) => obj["type"]?["key"]?.GetValue<string>();

    public static IReadOnlyList<string> ObjectRefs(JsonObject obj, string key)
    {
        if (Prop(obj, key)?["objects"] is not JsonArray arr) return [];
        var ids = new List<string>(arr.Count);
        foreach (JsonNode? n in arr)
            if (n?.GetValue<string>() is { } id) ids.Add(id);
        return ids;
    }

    // Human-readable rendering of any mapped property value, format-agnostic, for
    // the GetAsync digest. Returns null when the object carries no value.
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

    static string FirstLines(string? value, int count)
    {
        if (string.IsNullOrEmpty(value)) return "";

        int start = 0;
        for (int line = 0; line < count; line++)
        {
            int newline = value.IndexOf('\n', start);
            if (newline < 0) return value.TrimEnd();
            if (line == count - 1)
            {
                string preview = value[..newline].TrimEnd();
                return string.IsNullOrWhiteSpace(value[(newline + 1)..])
                    ? preview
                    : preview + " …";
            }
            start = newline + 1;
        }

        return value.TrimEnd();
    }
}
