using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A space plan is one JSON file that describes a whole Anytype space, in the
// order it must be built: properties and their options, types (panel, default
// template, views), templates (body, in-body views), collections, queries,
// pages, widgets and, optionally, objects. It carries no Anytype id (ADR-0003):
// everything points at something else by a symbolic reference.
//
//   type:<key>               a type            object:<name>   an object, by name
//   $self                    the object a view lives in (a filter value)
//
// The first three sections (properties, types, sections) are the schema
// manifest's own grammar and still go through SchemaPlanner. A plan adds, on
// each type, `panel`, `default_template` and `views`, and the top-level members
// below. SpacePlan splits the two: ManifestNode is what SchemaManifest.Parse
// accepts, the rest is read here and checked against it.
internal sealed record SpacePlan(
    JsonObject ManifestNode,
    SchemaManifest Manifest,
    IReadOnlyDictionary<string, PlanTypeExtras> TypeExtras,
    IReadOnlyList<JsonObject> Templates,
    IReadOnlyList<JsonObject> Collections,
    IReadOnlyList<JsonObject> Queries,
    IReadOnlyList<JsonObject> Pages,
    IReadOnlyList<JsonObject> Widgets,
    IReadOnlyList<JsonObject> Objects,
    IReadOnlySet<string> Declared)
{
    public const int CurrentVersion = 1;

    // Whether the file states a section at all. A prune removes from a section
    // only what a stated section leaves out: a plan without `templates` says
    // nothing about the space's templates, it does not ask to remove them.
    public bool States(string section) => Declared.Contains(section);

    // Top-level members the manifest grammar does not own.
    internal static readonly string[] PlanOnlyMembers =
        ["plan_version", "reader_notes", "templates", "collections", "queries", "pages", "widgets", "objects"];

    private static readonly string[] TypeOnlyMembers = ["panel", "default_template", "views", "display"];

    public static SpacePlan Parse(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        int version = root["plan_version"]?.GetValue<int>() ?? CurrentVersion;
        if (version != CurrentVersion)
            throw new ArgumentException($"plan_version {version} inconnue : cette version lit la version {CurrentVersion}.");

        JsonObject manifestNode = (JsonObject)root.DeepClone();
        foreach (string member in PlanOnlyMembers) manifestNode.Remove(member);

        var extras = new Dictionary<string, PlanTypeExtras>(StringComparer.Ordinal);
        if (manifestNode["types"] is JsonArray types)
            foreach (JsonObject type in types.OfType<JsonObject>())
            {
                string key = type["key"]?.GetValue<string>() ?? "";
                PlanTypeExtras typeExtras = PlanTypeExtras.Parse(key, type);
                extras[key] = typeExtras;
                foreach (string member in TypeOnlyMembers) type.Remove(member);
                // A property in the hidden section is out of the type's regular
                // list: the schema stage, which compares that list, must not see it.
                if (typeExtras.Hidden.Count > 0 && type["properties"] is JsonArray regular)
                    for (int i = regular.Count - 1; i >= 0; i--)
                        if (typeExtras.Hidden.Contains(regular[i]?.GetValue<string>() ?? "")) regular.RemoveAt(i);
                // Anytype lists a type's header properties before its regular ones
                // (measured on a live type: featured, regular, hidden). The schema
                // stage compares that list, so a header property declared after a
                // regular one would never match and be reordered on every run.
                if (typeExtras.Featured.Count > 0 && type["properties"] is JsonArray listed)
                {
                    var keys = listed.Select(n => n?.GetValue<string>() ?? "").ToList();
                    var ordered = typeExtras.Featured.Where(keys.Contains)
                        .Concat(keys.Where(k => !typeExtras.Featured.Contains(k))).ToList();
                    if (!ordered.SequenceEqual(keys))
                    {
                        listed.Clear();
                        foreach (string k in ordered) listed.Add(k);
                    }
                }
            }

        JsonObject schemaOnly = manifestNode;
        SchemaManifest manifest = SchemaManifest.Parse(schemaOnly);

        return new SpacePlan(
            schemaOnly,
            manifest,
            extras,
            Section(root, "templates"),
            Section(root, "collections"),
            Section(root, "queries"),
            Section(root, "pages"),
            Section(root, "widgets"),
            Section(root, "objects"),
            root.Where(kv => kv.Value is not null).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal));
    }

    private static List<JsonObject> Section(JsonObject root, string name)
    {
        if (root[name] is null) return [];
        if (root[name] is not JsonArray array)
            throw new ArgumentException($"Le champ « {name} » doit être un tableau.");
        var result = new List<JsonObject>();
        foreach (JsonNode? node in array)
            result.Add(node as JsonObject ?? throw new ArgumentException($"Chaque entrée de « {name} » doit être un objet."));
        return result;
    }
}

internal sealed record PlanTypeExtras(
    IReadOnlyList<string> Featured,
    IReadOnlyList<string> Hidden,
    string? DefaultTemplate,
    JsonArray? Views,
    IReadOnlyList<string> AllProperties,
    PlanTypeDisplay? Display = null)
{
    public static PlanTypeExtras Parse(string typeKey, JsonObject type)
    {
        var featured = new List<string>();
        var hidden = new List<string>();
        if (type["panel"] is JsonObject panel)
        {
            JsonShape.RequireOnly(panel, ["featured", "hidden"], $"panel du type {typeKey}");
            featured.AddRange(Keys(panel["featured"], $"type {typeKey}.panel.featured"));
            hidden.AddRange(Keys(panel["hidden"], $"type {typeKey}.panel.hidden"));
        }
        else if (type["panel"] is not null)
            throw new ArgumentException($"Le champ « panel » du type « {typeKey} » doit être un objet.");

        string? template = type["default_template"]?.GetValue<string>();
        if (type["views"] is not null and not JsonArray)
            throw new ArgumentException($"Le champ « views » du type « {typeKey} » doit être un tableau.");

        // Every property the type holds, the hidden ones included: the manifest the
        // schema stage reads has the hidden ones taken out.
        var all = Keys(type["properties"], $"type {typeKey}.properties").ToList();
        return new PlanTypeExtras(featured, hidden, template, type["views"] as JsonArray, all, PlanTypeDisplay.Parse(typeKey, type["display"]));
    }

    private static IEnumerable<string> Keys(JsonNode? node, string label)
    {
        if (node is null) yield break;
        if (node is not JsonArray array) throw new ArgumentException($"« {label} » doit être un tableau de clés.");
        foreach (JsonNode? item in array)
            yield return KeyRules.Validate(item?.GetValue<string>() ?? "", label);
    }
}

// How a type draws its own objects, set in the type's settings in the app:
// `header` is « Properties view » (list: one property per line under the title;
// inline: side by side), `width` the page width (0 = automatic, else 0 to 1).
// Both live on the type as an object, not in its type settings: they are read
// from GET objects/{type id} and written by set_properties on it, since
// PATCH types/{key} refuses set_properties (measured 2026-11-14). Either may be
// left out, and then the plan says nothing about it.
internal sealed record PlanTypeDisplay(string? Header, double? Width)
{
    public const string HeaderProperty = "header_relations_layout";
    public const string WidthProperty = "layout_width";

    public static PlanTypeDisplay? Parse(string typeKey, JsonNode? node)
    {
        if (node is null) return null;
        if (node is not JsonObject obj)
            throw new ArgumentException($"Le champ « display » du type « {typeKey} » doit être un objet.");
        JsonShape.RequireOnly(obj, ["header", "width"], $"display du type {typeKey}");
        string? header = obj["header"]?.GetValue<string>();
        if (header is not (null or "list" or "inline"))
            throw new ArgumentException($"type {typeKey} : display.header vaut « list » ou « inline », pas « {header} ».");
        double? width = obj["width"]?.GetValue<double>();
        if (width is < 0 or > 1)
            throw new ArgumentException($"type {typeKey} : display.width va de 0 (automatique) à 1, pas {width}.");
        return new PlanTypeDisplay(header, width);
    }

    // The type object's properties as the API gives them; a value it lacks reads
    // as the app's default (inline, automatic).
    public static PlanTypeDisplay FromWire(JsonObject? properties)
    {
        double header = Number(properties?[HeaderProperty]);
        return new PlanTypeDisplay(header == 1 ? "list" : "inline", Number(properties?[WidthProperty]));
    }

    public JsonObject ToPlan()
    {
        var node = new JsonObject();
        if (Header is not null) node["header"] = Header;
        if (Width is not null) node["width"] = Width;
        return node;
    }

    // The set_properties values for what this display states and `live` lacks.
    public JsonObject Differences(PlanTypeDisplay live)
    {
        var set = new JsonObject();
        if (Header is not null && Header != live.Header) set[HeaderProperty] = Header == "list" ? 1 : 0;
        if (Width is not null && Math.Abs(Width.Value - (live.Width ?? 0)) > 1e-9) set[WidthProperty] = Width.Value;
        return set;
    }

    private static double Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out double d) ? d : 0;
}
