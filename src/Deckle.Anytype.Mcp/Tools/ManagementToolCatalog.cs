using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// The destructive tools the base catalog withholds, mounted only on the client
// surfaces that opt in (McpClients). Every other surface is served none of
// these.
//
// Same validation discipline as ToolCatalog: a handler throw becomes an
// isError:true result the model can read and self-correct on.
public static class ManagementToolCatalog
{
    // Appended to the host's instructions when this catalog is mounted, so a
    // consumer (and a small model) learns when the bin is the right gesture and
    // the two-step delete contract up front. The chat OK is a rule of conduct the
    // server cannot check; the toast and the refusals are what it enforces.
    public const string Instructions =
        "Management tools are mounted. archive and delete answer two different wishes. "
        + "archive ticks « Archivé »: the object had a life and should only stop showing; it is the normal gesture. "
        + "delete moves an object to Anytype's restorable bin: only for what should never have existed "
        + "(a duplicate, a botched creation). A binned object vanishes from every search, so a wrong bin goes unnoticed. "
        + "delete runs in two steps pinned by id: a first call previews the target without deleting it; "
        + "show it to the person and wait for their explicit OK in the conversation, for that object only; "
        + "then call again with the previewed id and confirm:true. Deckle then asks the person once more "
        + "through a Windows notification and bins only if they accept. "
        + "delete always refuses a rapport and any object another object links to; do not look for a way around it.";

    public static IReadOnlyList<ToolDescriptor> Build(ManagementGestures management)
    {
        return new ToolDescriptor[]
        {
            new(
                "delete",
                "Move an object that should never have existed (a duplicate, a botched creation) to Anytype's restorable bin; to only stop seeing an object, use archive instead. Two steps, pinned by id: call it once with the target (name or id) to PREVIEW what would be binned (its name, type and id) without deleting anything; get the person's explicit OK in the conversation; then call it again with that id as target and confirm:true. The confirming call waits up to 60 s for the person to accept a Windows notification. Refused for a rapport and for any object another object links to.",
                Schema(
                    required: [Prop("target", "string", "Object to move to trash, name or id.")],
                    optional: [Prop("confirm", "boolean", "Pass true ONLY on the second call, with target set to the id the preview returned, to commit. Omit for the preview.")]),
                async (args, ct) =>
                    await management.DeleteAsync(Str(args, "target"), BoolOpt(args, "confirm") ?? false, ct),
                ToolExecutionContract.DestructiveVerifiable),
        };
    }

    // ── Schema + argument helpers (local copies, as in the other catalogs) ────

    static JsonObject Schema(IReadOnlyList<(string Name, JsonObject Schema)>? required = null,
                             IReadOnlyList<(string Name, JsonObject Schema)>? optional = null)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();

        if (required is not null)
            foreach (var (name, schema) in required)
            {
                properties[name] = schema;
                requiredNames.Add(name);
            }

        if (optional is not null)
            foreach (var (name, schema) in optional)
                properties[name] = schema;

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (requiredNames.Count > 0) root["required"] = requiredNames;
        return root;
    }

    static (string, JsonObject) Prop(string name, string type, string description) =>
        (name, new JsonObject { ["type"] = type, ["description"] = description });

    static string Str(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            throw new ArgumentException($"Argument '{name}' must be a string.", name);
        return s;
    }

    static bool? BoolOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonValue value || !value.TryGetValue<bool>(out var b))
            throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
        return b;
    }
}
