using System.Text.Json.Nodes;

using Deckle.Anytype.Gestures;

namespace Deckle.Anytype.Mcp.Tools;

// ─── Tool catalog ─────────────────────────────────────────────────────────────
//
// Builds the 13 MCP tools over the four gesture classes. Each descriptor pairs a
// JSON Schema (2020-12, additionalProperties:false) with a handler that reads and
// type-checks the arguments before invoking the gesture.
//
// Validation discipline: a missing required argument or a wrong-typed one throws
// ArgumentException naming the argument. The host turns any handler exception into
// a tools/call result with isError:true, so the model self-corrects — these throws
// ARE the error channel, not failures to avoid. Selector and not-found semantics
// live in the gestures; the catalog only guards shape.

public static class ToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(
        SessionGestures session, TaskGestures tasks,
        ProjectGestures projects, QueryGestures query)
    {
        return new ToolDescriptor[]
        {
            new(
                "session_start",
                "Open a work session on a task: create its journal report and surface the task with its recent reports.",
                Schema(
                    required: [Prop("task", "string", "Anchor task, name or id.")]),
                async (args, ct) =>
                    await session.StartAsync(Str(args, "task"), ct)),

            new(
                "log",
                "Append one journal line to the current session report (or a named one).",
                Schema(
                    required: [Prop("line", "string", "Journal line to append (the why of what you just did).")],
                    optional: [Prop("report", "string", "Target report, name or id; omit for the session's current report.")]),
                async (args, ct) =>
                    await session.LogAsync(Str(args, "line"), StrOpt(args, "report"), ct)),

            new(
                "get",
                "Read one object in full: its properties and markdown body.",
                Schema(
                    required: [Prop("name_or_id", "string", "Object to read, name or id.")],
                    optional: [Prop("type", "string", "Type key to disambiguate a name (epic, project, task, rapport, idee, document).")]),
                async (args, ct) =>
                    await query.GetAsync(Str(args, "name_or_id"), StrOpt(args, "type"), ct)),

            new(
                "project_overview",
                "Digest a project: its header, its tasks, and its latest reports.",
                Schema(
                    required: [Prop("project", "string", "Project, name or id.")]),
                async (args, ct) =>
                    await projects.OverviewAsync(Str(args, "project"), ct)),

            new(
                "create_task",
                "Create a task under a project.",
                Schema(
                    required:
                    [
                        Prop("project", "string", "Parent project, name or id."),
                        Prop("name", "string", "Task title."),
                        Prop("type", "string", "Task type: key or display name (production, recherche, organiser, echanger, gestion)."),
                    ],
                    optional:
                    [
                        Prop("priority", "integer", "Priority 0-5.", minimum: 0, maximum: 5),
                        Prop("body", "string", "Markdown body, e.g. a checklist of subtasks."),
                    ]),
                async (args, ct) =>
                    await tasks.CreateAsync(
                        Str(args, "project"), Str(args, "name"), Str(args, "type"),
                        IntOpt(args, "priority"), StrOpt(args, "body"), ct)),

            new(
                "task_done",
                "Mark a task as done.",
                Schema(
                    required: [Prop("task", "string", "Task, name or id.")]),
                async (args, ct) =>
                    await tasks.DoneAsync(Str(args, "task"), ct)),

            new(
                "link",
                "Link an object to one or more targets via the natural relation of its type.",
                Schema(
                    required:
                    [
                        Prop("object", "string", "Source object, name or id."),
                        ArrayProp("targets", "Targets to link, each a name or id."),
                    ]),
                async (args, ct) =>
                    await query.LinkAsync(Str(args, "object"), StrArray(args, "targets"), ct)),

            new(
                "list_projects",
                "List projects, grouped by état; filter to one state if given.",
                Schema(
                    optional: [Prop("state", "string", "État to filter on: key or display name; omit for all non-archived.")]),
                async (args, ct) =>
                    await projects.ListAsync(StrOpt(args, "state"), ct)),

            new(
                "search",
                "Search objects by text; returns compact hits (type, name, id, snippet).",
                Schema(
                    required: [Prop("text", "string", "Free-text query.")],
                    optional: [ArrayProp("types", "Type keys to restrict the search; omit for any type.")]),
                async (args, ct) =>
                    await query.SearchAsync(Str(args, "text"), StrArrayOpt(args, "types"), ct)),

            new(
                "subtask",
                "Add or toggle an inline checklist item in a task body; matches the label case-insensitively.",
                Schema(
                    required:
                    [
                        Prop("task", "string", "Task, name or id."),
                        Prop("label", "string", "Checklist item label to add or toggle."),
                    ],
                    optional: [Prop("done", "boolean", "Set the item's checked state; omit to toggle.")]),
                async (args, ct) =>
                    await tasks.SubtaskAsync(
                        Str(args, "task"), Str(args, "label"), BoolOpt(args, "done"), ct)),

            new(
                "create_project",
                "Create a project, optionally inside an epic collection and with a starting état.",
                Schema(
                    required: [Prop("name", "string", "Project title.")],
                    optional:
                    [
                        Prop("epic", "string", "Epic collection to add the project to, name or id."),
                        Prop("state", "string", "Starting état: key or display name."),
                    ]),
                async (args, ct) =>
                    await projects.CreateAsync(
                        Str(args, "name"), StrOpt(args, "epic"), StrOpt(args, "state"), ct)),

            new(
                "create_idea",
                "Capture a free-form idea as a new idee object.",
                Schema(
                    required: [Prop("content", "string", "Idea text.")]),
                async (args, ct) =>
                    await query.CreateIdeaAsync(Str(args, "content"), ct)),

            new(
                "update",
                "Set properties on an object; keys or display names map to values.",
                Schema(
                    required:
                    [
                        Prop("object", "string", "Object to update, name or id."),
                        ObjectProp("properties", "Map of property key or display name to value."),
                    ]),
                async (args, ct) =>
                    await query.UpdateAsync(Str(args, "object"), Obj(args, "properties"), ct)),
        };
    }

    // ── Schema construction ───────────────────────────────────────────────────
    //
    // Each Build entry declares its required/optional properties; Schema folds them
    // into the 2020-12 object schema the host advertises (additionalProperties:false,
    // required listing only the mandatory ones). No-arg tools never reach here.

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

    static (string, JsonObject) Prop(string name, string type, string description,
                                     int? minimum = null, int? maximum = null)
    {
        var schema = new JsonObject { ["type"] = type, ["description"] = description };
        if (minimum is not null) schema["minimum"] = minimum;
        if (maximum is not null) schema["maximum"] = maximum;
        return (name, schema);
    }

    static (string, JsonObject) ArrayProp(string name, string description) =>
        (name, new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "string" },
            ["description"] = description,
        });

    static (string, JsonObject) ObjectProp(string name, string description) =>
        (name, new JsonObject { ["type"] = "object", ["description"] = description });

    // ── Argument reading ──────────────────────────────────────────────────────
    //
    // Read-and-check accessors over the raw arguments object. Each throws
    // ArgumentException naming the argument when it is missing (required forms) or
    // mistyped — the host renders that as the model-facing isError text.

    static string Str(JsonObject? args, string name)
    {
        var value = StrOpt(args, name);
        if (value is null) throw new ArgumentException($"Missing required argument '{name}'.", name);
        return value;
    }

    static string? StrOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            throw new ArgumentException($"Argument '{name}' must be a string.", name);
        return s;
    }

    static int? IntOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        // JsonValue.TryGetValue<int> rejects fractional numbers and non-numeric kinds.
        if (node is not JsonValue value || !value.TryGetValue<int>(out var i))
            throw new ArgumentException($"Argument '{name}' must be an integer.", name);
        return i;
    }

    static bool? BoolOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonValue value || !value.TryGetValue<bool>(out var b))
            throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
        return b;
    }

    static IReadOnlyList<string> StrArray(JsonObject? args, string name)
    {
        var list = StrArrayOpt(args, name);
        if (list is null) throw new ArgumentException($"Missing required argument '{name}'.", name);
        return list;
    }

    static IReadOnlyList<string>? StrArrayOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonArray array)
            throw new ArgumentException($"Argument '{name}' must be an array of strings.", name);
        var result = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var s))
                throw new ArgumentException($"Argument '{name}' must be an array of strings.", name);
            result.Add(s);
        }
        return result;
    }

    static JsonObject Obj(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            throw new ArgumentException($"Missing required argument '{name}'.", name);
        if (node is not JsonObject obj)
            throw new ArgumentException($"Argument '{name}' must be an object.", name);
        // Detach from the parent document so the gesture owns a free-standing node.
        return (JsonObject)obj.DeepClone();
    }
}
