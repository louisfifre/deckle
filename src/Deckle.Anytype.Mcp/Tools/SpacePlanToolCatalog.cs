using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// The space plan surface: build a whole Anytype space from one plan file, check
// one against a plan, and read one back into a plan. It sits on the schema-admin
// surface beside schema_preview / schema_apply, which stay as they are.
public static class SpacePlanToolCatalog
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<ToolDescriptor> Build(SpacePlanGestures plans)
    {
        return new ToolDescriptor[]
        {
            new(
                "space_plan_preview",
                "Preview a space plan against a configured Anytype space, read only: what the schema stage and the structure stage would write, in creation order (properties and options, types, panels, collections, queries, templates, default templates, views, pages), plus the objects and the sidebar when their stages are asked for. A plan that is invalid (a view on a property nobody declared, an option that does not exist, an empty view block) is refused here with the reasons, before any write. With nothing to do it says the space matches the plan, which is the check; run it again ten minutes after a build, since a sort or filter written without an id reads right at once and collapses when the object loads again. Returns a deterministic preview id for space_plan_apply.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias, for example home or pm."),
                        ("plan", PlanProp()),
                    ],
                    optional: [StagesProp()]),
                async (args, ct) =>
                {
                    SpacePlanPreview preview = await plans.PreviewAsync(
                        Str(args, "space"), Obj(args, "plan"), Stages(args), ct);
                    return new ToolOutput(preview.Rendered, PreviewContent(preview));
                },
                ToolExecutionContract.ReadOnly),

            new(
                "space_plan_apply",
                "Apply a previous space_plan_preview. Repeat the exact plan, the same stages and the preview_id; the live state must still match what was reviewed. Builds in creation order, and is idempotent: a second run on a built space does nothing. Pages that exist are never rewritten. Requires confirm:true. The sidebar stage (stages including widgets) is written only with the space owner's key; an editor's key is refused.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias used by the preview."),
                        Prop("preview_id", "string", "Preview id returned by space_plan_preview."),
                        ("plan", PlanProp()),
                        Prop("confirm", "boolean", "Must be true; omit or false refuses the write."),
                    ],
                    optional: [StagesProp()]),
                async (args, ct) =>
                    await plans.ApplyAsync(
                        Str(args, "space"), Str(args, "preview_id"), Obj(args, "plan"), Stages(args), Bool(args, "confirm"), ct),
                ToolExecutionContract.AdditiveVerifiableWithStableTarget),

            new(
                "space_plan_read",
                "Read a configured Anytype space into a plan, read only: its properties and options with colours, its types with panel, default template and views, its templates with bodies and in-body views, its collections and queries with their views, its sidebar, and the pages named in `pages`. The plan is the form space_plan_preview takes, so a space can be read once, corrected by hand and built again elsewhere. Objects are never read. Things the space holds twice (an option, a template) are read once and listed in `reader_notes`.",
                Schema(
                    required: [Prop("space", "string", "Configured space alias, for example home or pm.")],
                    optional:
                    [
                        ("pages", new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Names of the pages to read with their body, for example the home page.",
                            ["items"] = new JsonObject { ["type"] = "string" },
                        }),
                    ]),
                async (args, ct) =>
                {
                    string[] pages = args is not null && args["pages"] is JsonArray array
                        ? array.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToArray()
                        : [];
                    JsonObject plan = await plans.ReadAsync(Str(args, "space"), pages, ct);
                    return plan.ToJsonString(Indented);
                },
                ToolExecutionContract.ReadOnly),
        };
    }

    private static SpacePlanStages Stages(JsonObject? args)
    {
        if (args is null || args["stages"] is not JsonArray array || array.Count == 0) return SpacePlanStages.Default;
        SpacePlanStages stages = SpacePlanStages.None;
        foreach (JsonNode? node in array)
        {
            string word = node?.GetValue<string>() ?? "";
            if (!Enum.TryParse(word, ignoreCase: true, out SpacePlanStages stage) || stage is SpacePlanStages.None or SpacePlanStages.Default)
                throw new ArgumentException($"Unknown stage '{word}'. Stages: schema, structure, objects, widgets.");
            stages |= stage;
        }
        return stages;
    }

    private static (string, JsonObject) StagesProp() =>
        ("stages", new JsonObject
        {
            ["type"] = "array",
            ["description"] = "Which parts of the plan to cover: schema (properties, options, types), structure (panels, collections, queries, templates, views, pages), objects, widgets (the sidebar, owner only). Default: schema and structure.",
            ["items"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray { "schema", "structure", "objects", "widgets" },
            },
        });

    private static JsonObject PlanProp() => new()
    {
        ["type"] = "object",
        ["description"] =
            "A space plan: the schema manifest's `properties`, `types` (each may add `panel` {featured, hidden}, `default_template` and `views`) and `sections`, plus `templates`, `collections`, `queries`, `pages`, `widgets` and `objects`. Keys are snake_case ASCII; nothing holds an Anytype id; references are `type:<key>`, `object:<name>`, and `$self` for the object a view lives in (a filter value). A select option is its name.",
    };

    private static JsonObject PreviewContent(SpacePlanPreview preview)
    {
        var actions = new JsonArray();
        foreach (SchemaPreviewAction action in preview.Actions)
            actions.Add(new JsonObject { ["kind"] = action.Kind, ["key"] = action.Key, ["detail"] = action.Name });
        return new JsonObject
        {
            ["preview_id"] = preview.PreviewId,
            ["space"] = preview.SpaceAlias,
            ["actions"] = actions,
            ["problems"] = new JsonArray(preview.Problems.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["conflicts"] = new JsonArray(preview.Conflicts.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["notes"] = new JsonArray(preview.Notes.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        };
    }

    private static JsonObject Schema(
        IReadOnlyList<(string Name, JsonObject Schema)> required,
        IReadOnlyList<(string Name, JsonObject Schema)>? optional = null)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();
        foreach (var (name, schema) in required)
        {
            properties[name] = schema;
            requiredNames.Add(name);
        }
        if (optional is not null)
            foreach (var (name, schema) in optional) properties[name] = schema;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = requiredNames,
            ["additionalProperties"] = false,
        };
    }

    private static (string, JsonObject) Prop(string name, string type, string description) =>
        (name, new JsonObject { ["type"] = type, ["description"] = description });

    private static string Str(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            throw new ArgumentException($"Argument '{name}' must be a string.", name);
        return s;
    }

    private static bool Bool(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonValue value || !value.TryGetValue<bool>(out var b))
            throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
        return b;
    }

    private static JsonObject Obj(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonObject obj)
            throw new ArgumentException($"Argument '{name}' must be an object.", name);
        return (JsonObject)obj.DeepClone();
    }
}
