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
                ToolExecutionContract.ReadOnly)
            {
                OutputSchema = PreviewOutputSchema(),
            },

            new(
                "space_plan_apply",
                "Apply a previous space_plan_preview. Repeat the exact plan, the same stages and the preview_id; the live state must still match what was reviewed. Builds in creation order, and is idempotent: a second run on a built space does nothing. A page that exists and differs has its body rewritten in place, in one edit, keeping its id (its block and view ids are new). With the prune stage it also removes what the preview listed as absent from the plan; a removal the server refuses with 403, 404 or 409 (an object another key or the app created) is reported and the others go on, and the next preview lists it again since the space still holds it; any other failure stops the run. Requires confirm:true. The sidebar stage (stages including widgets) is written only with the space owner's key; an editor's key is refused.",
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
                // A plan also rewrites views, templates and a type's property list to match
                // itself, and deletes a type's default view: not the additive write that
                // schema_apply's create-only manifest is.
                ToolExecutionContract.OverwritingVerifiableWithStableTarget),

            new(
                "space_plan_read",
                "Read a configured Anytype space into a plan, read only: its properties and options with colours, its types with panel, display (header list or inline, page width), default template and views, its templates with bodies and in-body views, its collections and queries with their views, its sidebar, and the pages named in `pages`. The plan is the form space_plan_preview takes, so a space can be read once, corrected by hand and built again elsewhere. Objects are never read. Things the space holds twice (an option, a template) are read once and listed in `reader_notes`.",
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

            new(
                "create_type_preview",
                "Preview the creation of one type, read only, through the space plan engine. Give the type's key and name, and the property keys it carries; the properties are taken from the space's own list, so each is declared as the space holds it, and a key the space does not hold is refused with the list of those it does. `panel` sets the header (`featured`) and the hidden section (`hidden`) in one go; its keys count as the type's own. Returns a preview id for create_type; a type that already matches says so.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias, for example home or pm."),
                        ("type", TypeProp()),
                    ]),
                async (args, ct) =>
                {
                    (SpacePlanPreview preview, _) = await plans.PreviewTypeAsync(Str(args, "space"), Obj(args, "type"), ct);
                    return new ToolOutput(preview.Rendered, PreviewContent(preview));
                },
                ToolExecutionContract.ReadOnly)
            {
                OutputSchema = PreviewOutputSchema(),
            },

            new(
                "create_type",
                "Create one type from a previous create_type_preview: repeat the same `type` and the preview_id. The live properties must still match what was reviewed. Idempotent: a second run does nothing. Only adds: a type that exists with another property list or panel is refused (a conflict) instead of rewritten; use schema_apply or space_plan_apply for that. Requires confirm:true.",
                Schema(
                    required:
                    [
                        Prop("space", "string", "Configured space alias used by the preview."),
                        Prop("preview_id", "string", "Preview id returned by create_type_preview."),
                        ("type", TypeProp()),
                        Prop("confirm", "boolean", "Must be true; omit or false refuses the write."),
                    ]),
                async (args, ct) =>
                    await plans.ApplyTypeAsync(
                        Str(args, "space"), Str(args, "preview_id"), Obj(args, "type"), Bool(args, "confirm"), ct),
                ToolExecutionContract.AdditiveVerifiableWithStableTarget),
        };
    }

    private static JsonObject TypeProp() => new()
    {
        ["type"] = "object",
        ["description"] =
            "One type: `key` (snake_case ASCII) and `name` are required; optional `plural_name`, `layout` (basic, profile, action, note), `description`, `icon`, `properties` (property keys), `panel` {featured, hidden} (property keys) and `views` (plan views, names inline in one language).",
    };

    private static SpacePlanStages Stages(JsonObject? args)
    {
        if (args is null || args["stages"] is not JsonArray array || array.Count == 0) return SpacePlanStages.Default;
        SpacePlanStages stages = SpacePlanStages.None;
        foreach (JsonNode? node in array)
        {
            string word = node?.GetValue<string>() ?? "";
            if (!Enum.TryParse(word, ignoreCase: true, out SpacePlanStages stage) || stage is SpacePlanStages.None or SpacePlanStages.Default)
                throw new ArgumentException($"Unknown stage '{word}'. Stages: schema, structure, objects, widgets, prune.");
            stages |= stage;
        }
        return stages;
    }

    private static (string, JsonObject) StagesProp() =>
        ("stages", new JsonObject
        {
            ["type"] = "array",
            ["description"] = "Which parts of the plan to cover: schema (properties, options and their colours, types), structure (panels, type display, collections, queries, templates, views, pages), objects, widgets (the sidebar, owner only). Add prune to also remove, within those stages, what the space holds and a section the plan states leaves out: widgets, collection members, templates, queries, collections, types, properties and options. A section the plan omits is left alone, but a stated empty one (`types: []`) removes everything in it. The app's own types (page, note, collection…, plus project and task) and the properties a fresh space ships are never removed; templates, widgets and options have no such guard. Default: schema and structure, no prune.",
            ["items"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray { "schema", "structure", "objects", "widgets", "prune" },
            },
        });

    private static JsonObject PlanProp() => new()
    {
        ["type"] = "object",
        ["description"] =
            "A space plan: the schema manifest's `properties`, `types` (each may add `panel` {featured, hidden}, `display` {header: list|inline, width: 0 automatic to 1}, `default_template` and `views`) and `sections`, plus `templates`, `collections`, `queries`, `pages`, `widgets` and `objects`. Keys are snake_case ASCII; nothing holds an Anytype id; references are `type:<key>`, `object:<name>`, and `$self` for the object a view lives in (a filter value). A select option is its name.",
    };

    // The same shape schema_preview returns (kind, key, name per action; conflicts;
    // skipped_conflicts), plus `problems`: the reasons a plan is refused before any
    // read of the space. The preview id is empty when there are problems.
    internal static JsonObject PreviewContent(SpacePlanPreview preview)
    {
        var actions = new JsonArray();
        foreach (SchemaPreviewAction action in preview.Actions)
            actions.Add(new JsonObject { ["kind"] = action.Kind, ["key"] = action.Key, ["name"] = action.Name });
        return new JsonObject
        {
            ["preview_id"] = preview.PreviewId,
            ["space"] = preview.SpaceAlias,
            ["actions"] = actions,
            ["problems"] = new JsonArray(preview.Problems.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["conflicts"] = new JsonArray(preview.Conflicts.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["skipped_conflicts"] = new JsonArray(preview.Notes.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        };
    }

    // An action kind is open here: the structure stage adds its own (views, page_create,
    // type_panel_add, ...) to the schema stage's, and the list grows with the engine.
    internal static JsonObject PreviewOutputSchema()
    {
        JsonObject Strings() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["preview_id"] = new JsonObject { ["type"] = "string", ["pattern"] = "^([0-9a-f]{64})?$" },
                ["space"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                            ["key"] = new JsonObject { ["type"] = "string" },
                            ["name"] = new JsonObject { ["type"] = "string" },
                        },
                        ["required"] = new JsonArray { "kind", "key", "name" },
                        ["additionalProperties"] = false,
                    },
                },
                ["problems"] = Strings(),
                ["conflicts"] = Strings(),
                ["skipped_conflicts"] = Strings(),
            },
            ["required"] = new JsonArray { "preview_id", "space", "actions", "problems", "conflicts", "skipped_conflicts" },
            ["additionalProperties"] = false,
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
