using System.Reflection;
using System.Text.Json.Nodes;

namespace Deckle.Home;

internal static class HomeSchemaTargetData
{
    public static readonly JsonObject Manifest = Load("Deckle.Home.Schema.schema-manifest.json");
    private static readonly JsonObject Annotations = Load("Deckle.Home.Schema.schema-annotations.json");

    public static readonly IReadOnlyDictionary<string, string> RequiredProperties = BuildProperties();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType = BuildTypeProperties();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectPropertyTargets = BuildTargets();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedVocabularies = BuildClosed();
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SeededVocabularies = BuildSeeded();
    public static readonly IReadOnlyList<string> ZoneProperties = BuildZoneProperties();

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

    private static List<(string Key, List<string> Properties)> TypeNodes() =>
        Manifest["types"]!.AsArray().OfType<JsonObject>()
            .Where(type => type["key"]!.GetValue<string>() != "zone")
            .Select(type => (
                HomeSchema.WireTypeKey(type["key"]!.GetValue<string>()),
                type["properties"]!.AsArray().Select(value => HomeSchema.WirePropertyKey(value!.GetValue<string>())).ToList()))
            .ToList();

    private static Dictionary<string, string> BuildProperties()
    {
        var attached = TypeNodes().SelectMany(type => type.Properties).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string canonical, JsonObject property) in PropertyNodes())
        {
            string wire = HomeSchema.WirePropertyKey(canonical);
            if (!attached.Contains(wire) || canonical == HomeSchema.Properties.RatedOutputPower) continue;
            result[wire] = property["format"]!.GetValue<string>();
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildTypeProperties() =>
        TypeNodes().ToDictionary(
            type => type.Key,
            type => (IReadOnlyList<string>)type.Properties
                .Where(property => property != HomeSchema.Properties.RatedOutputPower)
                .Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    private static Dictionary<string, IReadOnlyList<string>> BuildTargets()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (Annotations["properties"] is not JsonObject properties) return result;
        foreach ((string canonical, JsonNode? node) in properties)
        {
            if (node is not JsonObject value || value["target_types"] is not JsonArray targets) continue;
            result[HomeSchema.WirePropertyKey(canonical)] = targets
                .Select(target => HomeSchema.WireTypeKey(target!.GetValue<string>())).ToArray();
        }
        // These guards predate the sidecar annotations and remain part of the
        // Home domain contract: a task attachment targets a Worksite, and a
        // circuit's driver is a Point.
        result[HomeSchema.Properties.Worksite] = [HomeSchema.Types.Worksite];
        result[HomeSchema.Properties.PoweredBy] = [HomeSchema.Types.Point];
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildClosed()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (Annotations["properties"] is not JsonObject properties) return result;
        var manifestProperties = PropertyNodes();
        foreach ((string canonical, JsonNode? node) in properties)
        {
            if (node?["vocabulary"]?.GetValue<string>() != "closed") continue;
            if (!manifestProperties.TryGetValue(canonical, out JsonObject? property)
                || property["tags"] is not JsonArray tags) continue;
            result[HomeSchema.WirePropertyKey(canonical)] = tags.OfType<JsonObject>()
                .Select(tag => tag["key"]!.GetValue<string>()).ToArray();
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildSeeded()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach ((string canonical, JsonObject property) in PropertyNodes())
        {
            if (property["tags"] is not JsonArray tags) continue;
            result[HomeSchema.WirePropertyKey(canonical)] = tags.OfType<JsonObject>()
                .Select(tag => tag["key"]!.GetValue<string>()).ToArray();
        }
        return result;
    }

    private static IReadOnlyList<string> BuildZoneProperties() =>
        Manifest["types"]!.AsArray().OfType<JsonObject>()
            .Where(type => type["key"]!.GetValue<string>() == "zone")
            .SelectMany(type => type["properties"]!.AsArray())
            .Select(value => HomeSchema.WirePropertyKey(value!.GetValue<string>()))
            .Distinct(StringComparer.Ordinal).ToArray();
}
