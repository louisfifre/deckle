using System.Reflection;
using System.Text.Json.Nodes;

namespace Deckle.Home;

// Reads the embedded manifest and annotations as they are: every type, every
// attached property and every annotated relation target, under the keys the
// space carries. Nothing is projected, excluded or added here.
internal static class HomeSchemaTargetData
{
    public static readonly JsonObject Manifest = Load("Deckle.Home.Schema.schema-manifest.json");
    private static readonly JsonObject Annotations = Load("Deckle.Home.Schema.schema-annotations.json");

    public static readonly IReadOnlyDictionary<string, string> RequiredProperties = BuildProperties();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType = BuildTypeProperties();
    public static readonly IReadOnlyDictionary<string, string> Layouts = BuildLayouts();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectPropertyTargets = BuildTargets();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedVocabularies = BuildClosed();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SeededVocabularies = BuildSeeded();

    public static JsonObject CloneManifest() => (JsonObject)Manifest.DeepClone();

    private static JsonObject Load(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) throw new InvalidOperationException($"Home schema resource missing: {name}.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static Dictionary<string, JsonObject> PropertyNodes() =>
        Manifest["properties"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(p => p["key"]!.GetValue<string>(), StringComparer.Ordinal);

    private static IEnumerable<JsonObject> TypeNodes() =>
        Manifest["types"]!.AsArray().OfType<JsonObject>();

    private static string Key(JsonObject node) => node["key"]!.GetValue<string>();

    private static IEnumerable<string> AttachedKeys(JsonObject type) =>
        type["properties"]!.AsArray().Select(value => value!.GetValue<string>());

    // A property counts as required once a type attaches it; a manifest
    // property no type uses would only be a provisioning wish.
    private static Dictionary<string, string> BuildProperties()
    {
        var attached = TypeNodes().SelectMany(AttachedKeys).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, JsonObject property) in PropertyNodes())
            if (attached.Contains(key))
                result[key] = property["format"]!.GetValue<string>();
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildTypeProperties() =>
        TypeNodes().ToDictionary(
            Key,
            type => (IReadOnlyList<string>)AttachedKeys(type).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    private static Dictionary<string, string> BuildLayouts() =>
        TypeNodes().ToDictionary(Key, type => type["layout"]!.GetValue<string>(), StringComparer.Ordinal);

    private static Dictionary<string, IReadOnlyList<string>> BuildTargets()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (Annotations["properties"] is not JsonObject properties) return result;
        foreach ((string key, JsonNode? node) in properties)
        {
            if (node is not JsonObject value || value["target_types"] is not JsonArray targets) continue;
            result[key] = targets.Select(target => target!.GetValue<string>()).ToArray();
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildClosed()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (Annotations["properties"] is not JsonObject properties) return result;
        var manifestProperties = PropertyNodes();
        foreach ((string key, JsonNode? node) in properties)
        {
            if (node?["vocabulary"]?.GetValue<string>() != "closed") continue;
            if (!manifestProperties.TryGetValue(key, out JsonObject? property)
                || property["tags"] is not JsonArray tags) continue;
            result[key] = tags.OfType<JsonObject>().Select(Key).ToArray();
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildSeeded()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach ((string key, JsonObject property) in PropertyNodes())
        {
            if (property["tags"] is not JsonArray tags) continue;
            result[key] = tags.OfType<JsonObject>().Select(Key).ToArray();
        }
        return result;
    }
}
