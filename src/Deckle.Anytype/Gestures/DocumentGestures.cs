using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Document gestures: create stable reference documents. Reads,
// property updates, and section edits stay on the generic query surface; this
// class owns only the document-specific creation intent.
public sealed class DocumentGestures(AnytypeApiClient api)
{
    public async Task<string> CreateAsync(
        string name,
        string type,
        string? body = null,
        string? version = null,
        bool isGlobal = false,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom du document ne peut pas être vide.", nameof(name));

        string typeKey = DevSpace.DocumentType.Resolve(type)
            ?? throw new ArgumentException($"Type de document inconnu « {type} ».", nameof(type));

        string? versionValue = string.IsNullOrWhiteSpace(version) ? null : version.Trim();

        var properties = new JsonArray
        {
            SelectProp(DevSpace.Props.DocumentType, typeKey),
        };

        if (versionValue is not null)
            properties.Add(TextProp(DevSpace.Props.Version, versionValue));

        if (isGlobal)
            properties.Add(CheckboxProp(DevSpace.Props.DocumentGlobal, true));

        var payload = new JsonObject
        {
            ["type_key"] = DevSpace.Types.Document,
            ["name"] = name.Trim(),
            ["properties"] = properties,
        };

        if (!string.IsNullOrWhiteSpace(body))
            payload["body"] = body;

        JsonObject created = await api.CreateObjectAsync(payload, ct);

        DeckleAnytypeSource.Log.GestureCompleted("document_create", Elapsed(started));

        string objName = NameOf(created, name.Trim());
        string suffix = versionValue is null ? "" : $", version {versionValue}";
        if (isGlobal) suffix += ", global";
        return $"Document créé : {objName} ({DisplayType(typeKey)}{suffix})";
    }

    static JsonObject SelectProp(string key, string tagKey)
        => new() { ["key"] = key, ["select"] = tagKey };

    static JsonObject TextProp(string key, string value)
        => new() { ["key"] = key, ["text"] = value };

    static JsonObject CheckboxProp(string key, bool value)
        => new() { ["key"] = key, ["checkbox"] = value };

    static string DisplayType(string typeKey)
        => DevSpace.DocumentType.NameFor(typeKey) ?? typeKey;

    static string NameOf(JsonObject obj, string fallback)
    {
        string? name = obj["name"]?.GetValue<string>();
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}
