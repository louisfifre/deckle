using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

public static class SchemaAdminToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(SchemaAdminGestures schema)
    {
        return new ToolDescriptor[]
        {
            new(
                "schema_inspect_space",
                "Inspect a configured Anytype space alias and list its current types and properties. The space argument is an alias such as dev or home, never a raw space_id.",
                Schema(
                    required: [Prop("space", "string", "Configured space alias, for example dev or home.")]),
                async (args, ct) =>
                    await schema.InspectAsync(Str(args, "space"), ct)),

            new(
                "schema_preview",
                "Preview an additive schema manifest against a configured Anytype space. It reports reuse, creations and conflicts, and returns a preview id for schema_apply. No write happens here.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias, for example dev or home."),
                        ManifestProp(),
                    ]),
                async (args, ct) =>
                    await schema.PreviewAsync(Str(args, "space"), Obj(args, "manifest"), ct)),

            new(
                "schema_apply",
                "Apply a previous schema_preview. Additive only: create missing types/properties/tags and attach properties to types. Requires confirm:true and the preview_id returned by schema_preview.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias used by the preview."),
                        Prop("preview_id", "string", "Preview id returned by schema_preview."),
                        Prop("confirm", "boolean", "Must be true; omit or false refuses the write."),
                    ]),
                async (args, ct) =>
                    await schema.ApplyAsync(
                        Str(args, "space"),
                        Str(args, "preview_id"),
                        Bool(args, "confirm"),
                        ct)),
        };
    }

    static JsonObject Schema(IReadOnlyList<(string Name, JsonObject Schema)>? required = null)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();

        if (required is not null)
            foreach (var (name, schema) in required)
            {
                properties[name] = schema;
                requiredNames.Add(name);
            }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = requiredNames,
            ["additionalProperties"] = false,
        };
    }

    static (string, JsonObject) Prop(string name, string type, string description) =>
        (name, new JsonObject { ["type"] = type, ["description"] = description });

    static (string, JsonObject) ManifestProp() =>
        ("manifest", new JsonObject
        {
            ["type"] = "object",
            ["description"] =
                "Additive manifest with optional arrays `types` and `properties`. Keys must be snake_case ASCII.",
            ["properties"] = new JsonObject
            {
                ["types"] = ArrayOf(TypeSpecSchema()),
                ["properties"] = ArrayOf(PropertySpecSchema()),
            },
            ["additionalProperties"] = false,
        });

    static JsonObject TypeSpecSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["key"] = new JsonObject { ["type"] = "string" },
            ["name"] = new JsonObject { ["type"] = "string" },
            ["layout"] = new JsonObject { ["type"] = "string" },
            ["properties"] = ArrayOf(new JsonObject { ["type"] = "string" }),
        },
        ["required"] = new JsonArray { "key", "name" },
        ["additionalProperties"] = false,
    };

    static JsonObject PropertySpecSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["key"] = new JsonObject { ["type"] = "string" },
            ["name"] = new JsonObject { ["type"] = "string" },
            ["format"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray
                {
                    "text", "number", "select", "multi_select", "date", "files",
                    "checkbox", "url", "email", "phone", "objects",
                },
            },
            ["tags"] = ArrayOf(new JsonObject
            {
                ["oneOf"] = new JsonArray
                {
                    new JsonObject { ["type"] = "string" },
                    TagSpecSchema(),
                },
            }),
        },
        ["required"] = new JsonArray { "key", "name", "format" },
        ["additionalProperties"] = false,
    };

    static JsonObject TagSpecSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["key"] = new JsonObject { ["type"] = "string" },
            ["name"] = new JsonObject { ["type"] = "string" },
            ["color"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray { "name" },
        ["additionalProperties"] = false,
    };

    static JsonObject ArrayOf(JsonObject itemSchema) => new()
    {
        ["type"] = "array",
        ["items"] = itemSchema,
    };

    static string Str(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            throw new ArgumentException($"Argument '{name}' must be a string.", name);
        return s;
    }

    static bool Bool(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonValue value || !value.TryGetValue<bool>(out var b))
            throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
        return b;
    }

    static JsonObject Obj(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonObject obj)
            throw new ArgumentException($"Argument '{name}' must be an object.", name);
        return (JsonObject)obj.DeepClone();
    }
}
