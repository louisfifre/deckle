using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A space plan is one JSON file that describes a whole Anytype space, in the
// order it must be built: properties and their options, types (panel, default
// template, views), templates (body, in-body views), collections, queries,
// pages, widgets and, optionally, objects. It carries no Anytype id (ADR-0003):
// everything points at something else by a symbolic reference.
//
//   type:<key>               a type            object:<name>   an object, by name
//   option:<prop>/<name>     a select option   $self           the object a view lives in
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
    IReadOnlyList<JsonObject> Objects)
{
    public const int CurrentVersion = 1;

    // Top-level members the manifest grammar does not own.
    internal static readonly string[] PlanOnlyMembers =
        ["plan_version", "reader_notes", "templates", "collections", "queries", "pages", "widgets", "objects"];

    private static readonly string[] TypeOnlyMembers = ["panel", "default_template", "views"];

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
                extras[key] = PlanTypeExtras.Parse(key, type);
                foreach (string member in TypeOnlyMembers) type.Remove(member);
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
            Section(root, "objects"));
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
    JsonArray? Views)
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

        return new PlanTypeExtras(featured, hidden, template, type["views"] as JsonArray);
    }

    private static IEnumerable<string> Keys(JsonNode? node, string label)
    {
        if (node is null) yield break;
        if (node is not JsonArray array) throw new ArgumentException($"« {label} » doit être un tableau de clés.");
        foreach (JsonNode? item in array)
            yield return KeyRules.Validate(item?.GetValue<string>() ?? "", label);
    }
}
