using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// ─── Management tool catalog ────────────────────────────────────────────────
//
// The destructive tools the base catalog withholds, mounted only when the host
// is launched with the management flag (arg/env). A default, unsupervised
// consumer is served none of these. For now the single tool is delete; the
// batch variant is deferred.
//
// Same validation discipline as ToolCatalog: a handler throw becomes an
// isError:true result the model can read and self-correct on.
public static class ManagementToolCatalog
{
    // Appended to the host's instructions when this catalog is mounted, so a
    // consumer (and a small model) learns the two-step delete contract up front.
    public const string Instructions =
        "Management tools are mounted. delete moves an object to Anytype's "
        + "restorable trash (a reversible bin), in two steps pinned by id: a first "
        + "call previews the target without deleting it, a second call with the "
        + "previewed id and confirm:true commits. Always preview before you confirm.";

    public static IReadOnlyList<ToolDescriptor> Build(ManagementGestures management)
    {
        return new ToolDescriptor[]
        {
            new(
                "delete",
                "Move an object to Anytype's restorable trash — a reversible bin, not a hard delete. Two steps, pinned by id: call it once with the target (name or id) to PREVIEW what would be trashed (its name, type and id) without deleting anything; then call it again with that id as target and confirm:true to commit. Preview first so the confirm targets exactly the object shown.",
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
