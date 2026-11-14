using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Builds a one-element space plan from a few arguments, so a single create
// command goes through the same engine as a whole space: the same validation, the
// same preview id, the same writes in the same order.
//
// Why a fragment fills `properties` from the space's own list: a plan names a
// property by key and declares it once. Taking the declaration from the space
// (name, format, options) means the fragment never restates a property
// differently from how the space holds it, so the schema stage finds nothing to
// change on it and the type only links what already exists.
public static class SpacePlanFragments
{
    private static readonly string[] TypeFields =
        ["key", "name", "plural_name", "layout", "description", "icon", "properties", "panel", "views"];

    // spaceProperties: the `properties` array of a space read through
    // SpacePlanReader (SpacePlanGestures.ReadAsync). typeArgs: key and name, and
    // optionally plural_name, layout, description, icon, properties (keys), panel
    // {featured, hidden} and views.
    public static JsonObject ForType(JsonArray spaceProperties, JsonObject typeArgs)
    {
        foreach (KeyValuePair<string, JsonNode?> field in typeArgs)
            if (!TypeFields.Contains(field.Key, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Unknown field '{field.Key}' for a type. Fields: {string.Join(", ", TypeFields)}.");
        Required(typeArgs, "key");
        Required(typeArgs, "name");

        var byKey = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (JsonObject property in spaceProperties.OfType<JsonObject>())
            if (property["key"]?.GetValue<string>() is { Length: > 0 } k) byKey[k] = property;

        // The type's own properties, in the order given; the panel's keys count as
        // the type's own (a header or hidden property is first a property of the type).
        var own = new List<string>();
        void Add(JsonNode? node, string where)
        {
            foreach (JsonNode? item in node as JsonArray ?? [])
            {
                string name = item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s)
                    ? s
                    : throw new ArgumentException($"{where} must hold property keys as strings.");
                if (!own.Contains(name, StringComparer.Ordinal)) own.Add(name);
            }
        }
        Add(typeArgs["properties"], "properties");
        JsonObject? panel = typeArgs["panel"] as JsonObject;
        if (typeArgs["panel"] is not null && panel is null) throw new ArgumentException("panel must be an object {featured, hidden}.");
        Add(panel?["featured"], "panel.featured");
        Add(panel?["hidden"], "panel.hidden");

        // A property the space holds is declared as the space holds it. One it does
        // not hold is left to the plan validator: it knows the properties every space
        // ships, and refuses the rest with the reason.
        var declared = new JsonArray();
        foreach (string property in own)
            if (byKey.TryGetValue(property, out JsonObject? live))
                declared.Add(live.DeepClone());
            else if (!KnownProperties.IsKnown(property) || IsRenamed(property))
                throw new ArgumentException(
                    $"Property '{property}' is not in the space. Create it first (schema_apply or space_plan_apply), or pick one of: "
                    + string.Join(", ", byKey.Keys.Order(StringComparer.Ordinal)) + ".");

        var type = new JsonObject();
        foreach (string field in TypeFields)
        {
            if (field is "properties" or "panel" || typeArgs[field] is null) continue;
            type[field] = typeArgs[field]!.DeepClone();
        }
        type["properties"] = new JsonArray(own.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        if (panel is not null)
            type["panel"] = new JsonObject
            {
                ["featured"] = panel["featured"]?.DeepClone() ?? new JsonArray(),
                ["hidden"] = panel["hidden"]?.DeepClone() ?? new JsonArray(),
            };

        return new JsonObject
        {
            ["plan_version"] = SpacePlan.CurrentVersion,
            ["properties"] = declared,
            ["types"] = new JsonArray { type },
        };
    }

    // status, done and due_date ship under English names and a space renames them,
    // so a plan must declare them: absent from the space's list they are not usable.
    private static bool IsRenamed(string key) => key is "status" or "done" or "due_date";

    private static string Required(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)
            ? s
            : throw new ArgumentException($"Missing required type field '{name}'.");
}
