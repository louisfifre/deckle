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
                    await schema.InspectAsync(Str(args, "space"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "schema_preview",
                "Preview an additive schema manifest against a configured Anytype space. It reports reuse, creations and conflicts, and returns a deterministic preview id for schema_apply. No write happens here and no server-side session is retained.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias, for example dev or home."),
                        ManifestProp(),
                    ]),
                async (args, ct) =>
                {
                    SchemaPreviewResult result = await schema.PreviewResultAsync(
                        Str(args, "space"), Obj(args, "manifest"), ct);
                    return new ToolOutput(result.Digest, PreviewContent(result));
                },
                ToolExecutionContract.ReadOnly)
            {
                OutputSchema = PreviewOutputSchema(),
            },

            new(
                "schema_apply",
                "Apply a previous schema_preview. Repeat the exact manifest and its deterministic preview_id; the live plan must still match what was reviewed. Additive only: create missing types/properties/tags, set missing type icons and descriptions, attach properties to types, and provision section collections with their member types. Requires confirm:true.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias used by the preview."),
                        Prop("preview_id", "string", "Preview id returned by schema_preview."),
                        ManifestProp(),
                        Prop("confirm", "boolean", "Must be true; omit or false refuses the write."),
                    ]),
                async (args, ct) =>
                    await schema.ApplyAsync(
                        Str(args, "space"),
                        Str(args, "preview_id"),
                        Obj(args, "manifest"),
                        Bool(args, "confirm"),
                        ct),
                ToolExecutionContract.AdditiveVerifiableWithStableTarget),
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
                "Additive manifest with optional arrays `types`, `properties` and `sections`. Keys must be snake_case ASCII.",
            ["properties"] = new JsonObject
            {
                ["types"] = ArrayOf(TypeSpecSchema()),
                ["properties"] = ArrayOf(PropertySpecSchema()),
                ["sections"] = ArrayOf(SectionSpecSchema()),
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
            ["plural_name"] = new JsonObject { ["type"] = "string" },
            ["layout"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray { "basic", "profile", "action", "note", "collection" },
            },
            ["description"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "One-line description of the type, written on its object face. Additive: "
                    + "set when the live description is empty; a differing live text is "
                    + "reported as a skipped conflict, never overwritten.",
            },
            ["icon"] = IconSpecSchema(),
            ["properties"] = ArrayOf(new JsonObject { ["type"] = "string" }),
        },
        ["required"] = new JsonArray { "key", "name" },
        ["additionalProperties"] = false,
    };

    // A section is a pinned sidebar folder: one collection object (built-in
    // type key "collection") whose members are the section's TYPE objects.
    static JsonObject SectionSpecSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Exact collection name; an existing built-in collection with this name is reused.",
            },
            ["icon"] = SectionIconSpecSchema(),
            ["types"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Type keys whose TYPE objects become collection members; each must exist in the manifest or in the live space.",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["uniqueItems"] = true,
            },
        },
        ["required"] = new JsonArray { "name", "types" },
        ["additionalProperties"] = false,
    };

    static JsonObject SectionIconSpecSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray { "emoji" },
            },
            ["emoji"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray { "format", "emoji" },
        ["additionalProperties"] = false,
    };

    static JsonObject IconSpecSchema() => new()
    {
        ["oneOf"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "icon" } },
                    ["name"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name from the Anytype built-in icon set for API 2025-05-20.",
                    },
                    ["color"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray
                        {
                            "grey", "yellow", "orange", "red", "pink",
                            "purple", "blue", "ice", "teal", "lime",
                        },
                    },
                },
                ["required"] = new JsonArray { "format", "name" },
                ["additionalProperties"] = false,
            },
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "emoji" } },
                    ["emoji"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray { "format", "emoji" },
                ["additionalProperties"] = false,
            },
        },
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

    internal static JsonObject PreviewOutputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["preview_id"] = new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = "^[0-9a-f]{64}$",
            },
            ["space"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            ["actions"] = ArrayOf(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray
                        {
                            "create_property", "create_tag", "create_type",
                            "set_icon", "set_description", "attach_property",
                            "create_section", "add_to_section",
                        },
                    },
                    ["key"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                    ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                },
                ["required"] = new JsonArray { "kind", "key", "name" },
                ["additionalProperties"] = false,
            }),
            ["conflicts"] = ArrayOf(new JsonObject { ["type"] = "string", ["minLength"] = 1 }),
            ["skipped_conflicts"] = ArrayOf(new JsonObject { ["type"] = "string", ["minLength"] = 1 }),
        },
        ["required"] = new JsonArray
        {
            "preview_id", "space", "actions", "conflicts", "skipped_conflicts",
        },
        ["additionalProperties"] = false,
    };

    internal static JsonObject PreviewContent(SchemaPreviewResult result)
    {
        var actions = new JsonArray();
        foreach (SchemaPreviewAction action in result.Actions)
        {
            actions.Add(new JsonObject
            {
                ["kind"] = action.Kind,
                ["key"] = action.Key,
                ["name"] = action.Name,
            });
        }

        return new JsonObject
        {
            ["preview_id"] = result.PreviewId,
            ["space"] = result.SpaceAlias,
            ["actions"] = actions,
            ["conflicts"] = new JsonArray(result.Conflicts
                .Select(value => JsonValue.Create(value))
                .ToArray()),
            ["skipped_conflicts"] = new JsonArray(result.SkippedConflicts
                .Select(value => JsonValue.Create(value))
                .ToArray()),
        };
    }

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
