using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype.Api;
using Deckle.Anytype.Schema;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Gestures;

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

    // Compact hits: one line per result — type, name, id, snippet.
    public async Task<string> SearchAsync(string text, IReadOnlyList<string>? typeKeys = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        JsonObject root = await api.SearchAsync(text, typeKeys, limit: 20, ct);
        JsonArray hits = root["data"]?.AsArray() ?? [];

        var sb = new StringBuilder();
        foreach (JsonNode? node in hits)
        {
            if (node is not JsonObject o) continue;
            sb.Append(QueryProp.TypeKey(o) ?? "?").Append(" · ").Append(QueryProp.Name(o));
            sb.Append(" · ").Append(QueryProp.Id(o));
            string snippet = QueryProp.Snippet(o);
            if (snippet.Length > 0) sb.Append(" · ").Append(snippet);
            sb.Append('\n');
        }

        string digest = sb.ToString().TrimEnd();
        if (digest.Length == 0) digest = "Aucun résultat.";

        DeckleAnytypeSource.Log.GestureCompleted("search", Elapsed(started));
        return digest;
    }

    // Appends targets into the natural link property of the (source type, target
    // type) pair. The API PATCH replaces a property's value wholesale, so this is
    // a read-modify-write: union the current refs with the resolved target ids.
    //
    // Supported pairs (property written on the SOURCE):
    //   task    → project   : relation_projet
    //   rapport → task      : tache(s)_liee(s)
    //   project → project   : depend_de
    public async Task<string> LinkAsync(string source, IReadOnlyList<string> targets, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string sourceId = await resolver.ResolveAsync(source, typeKeys: null, ct);
        JsonObject sourceObj = await api.GetObjectAsync(sourceId, ct);
        string sourceType = QueryProp.TypeKey(sourceObj) ?? "";

        // The destination property depends on the (source, target) pair, so each
        // target is resolved without a type constraint, its type read, then routed.
        // A mixed batch (e.g. a task linked to a project AND a rapport) groups by
        // destination property and writes each property once.
        var byProp = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string t in targets)
        {
            string targetId = await resolver.ResolveAsync(t, typeKeys: null, ct);
            JsonObject targetObj = await api.GetObjectAsync(targetId, ct);
            string targetType = QueryProp.TypeKey(targetObj) ?? "";

            string? propKey = LinkPropertyFor(sourceType, targetType);
            if (propKey is null)
                throw new InvalidOperationException(SupportedPairsError(sourceType, targetType));

            (byProp.TryGetValue(propKey, out var list) ? list : byProp[propKey] = []).Add(targetId);
        }

        // Per property: union the new ids with the existing refs (PATCH replaces a
        // property's value wholesale, so the full set must be re-sent).
        var entries = new JsonArray();
        int added = 0, total = 0;
        foreach ((string propKey, List<string> ids) in byProp)
        {
            var union = new List<string>(QueryProp.ObjectRefs(sourceObj, propKey));
            foreach (string id in ids)
            {
                total++;
                if (!union.Contains(id)) { union.Add(id); added++; }
            }

            var refs = new JsonArray();
            foreach (string id in union) refs.Add(id);
            entries.Add(new JsonObject { ["key"] = propKey, ["objects"] = refs });
        }

        await api.UpdateObjectAsync(sourceId, new JsonObject { ["properties"] = entries }, ct);

        DeckleAnytypeSource.Log.GestureCompleted("link", Elapsed(started));
        return $"Lié : {QueryProp.Name(sourceObj)} → {total} cible(s), {added} ajout(s).";
    }

    // Property names or keys → values. Resolves the selector to any type, maps
    // each display-name-or-key to a schema property key, builds the format-typed
    // entry (select/multi_select values resolved name-or-key), and PATCHes.
    public async Task<string> UpdateAsync(string selector, JsonObject properties, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string objType = QueryProp.TypeKey(obj) ?? "";

        var entries = new JsonArray();
        var applied = new List<string>();
        foreach ((string nameOrKey, JsonNode? value) in properties)
        {
            if (!DevSpace.TryResolveProperty(objType, nameOrKey, out string key, out string format))
                throw new InvalidOperationException(
                    $"Propriété inconnue « {nameOrKey} » pour le type {objType}. " +
                    $"Connues : {string.Join(", ", DevSpace.PropertiesFor(objType).Select(p => p.Label))}.");

            entries.Add(await BuildEntryAsync(key, format, value, ct));
            applied.Add(DevSpace.PropertyLabel(key) ?? key);
        }

        var payload = new JsonObject { ["properties"] = entries };
        await api.UpdateObjectAsync(id, payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("update", Elapsed(started));
        return $"Mis à jour : {QueryProp.Name(obj)} ({string.Join(", ", applied)}).";
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
    static string? LinkPropertyFor(string sourceType, string targetType)
    {
        if (sourceType == DevSpace.Types.Task && targetType == DevSpace.Types.Project) return DevSpace.Props.RelationProjet;
        if (sourceType == DevSpace.Types.Rapport && targetType == DevSpace.Types.Task) return DevSpace.Props.TachesLiees;
        if (sourceType == DevSpace.Types.Project && targetType == DevSpace.Types.Project) return DevSpace.Props.DependDe;
        return null;
    }

    static string SupportedPairsError(string sourceType, string targetType) =>
        $"Liaison non supportée : {sourceType} → {targetType}. " +
        "Paires supportées : tâche→projet, rapport→tâche(s), projet→projet.";

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

    public static string Snippet(JsonObject obj) => FirstLine(obj["snippet"]?.GetValue<string>());

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
}
