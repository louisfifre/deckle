using System.Text.Json.Nodes;

using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// Builds the base MCP tools over the gesture classes. Each descriptor pairs a
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
        ProjectGestures projects, QueryGestures query, ReadGestures reads, DocumentGestures documents)
    {
        return new ToolDescriptor[]
        {
            new(
                "open_report",
                "Open a work report on a task: create a journal report linked to it, dated today, and answer report_id, then one line « task title · project ». Call it as soon as work on a task starts; a report always belongs to a task. Pass the report_id to log.",
                Schema(
                    required: [Prop("task", "string", "Task id, as task_properties gives it; names are refused.")]),
                async (args, ct) =>
                    await session.OpenReportAsync(Str(args, "task"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "log",
                "Append one journal line to a session report. Answers « Noté. »; the line is not echoed.",
                Schema(
                    required:
                    [
                        Prop("line", "string", "Journal line to append: the why of what you just did."),
                        Prop("report", "string", "report_id returned by open_report; report names are refused."),
                    ]),
                async (args, ct) =>
                    await session.LogAsync(Str(args, "line"), Str(args, "report"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplicationWithStableTarget),

            // ── typed reads: one per need, named by what they return ──────────

            new(
                "task_properties",
                "A task's id, title with its done box, and properties: type, priority, due date, project by name, Description, Définition de fini. No body.",
                Schema(required: [Prop("task", "string", "Task, name or id.")]),
                async (args, ct) => await reads.TaskPropertiesAsync(Str(args, "task"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "task_checklist",
                "A task's id, title, and its subtasks as '- [ ]' / '- [x]' lines, then the checked count.",
                Schema(required: [Prop("task", "string", "Task, name or id.")]),
                async (args, ct) => await reads.TaskChecklistAsync(Str(args, "task"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "task_notes",
                "A task's id, title, and the notes section of its body (what to know before working on it). The checklist is left out.",
                Schema(required: [Prop("task", "string", "Task, name or id.")]),
                async (args, ct) => await reads.TaskNotesAsync(Str(args, "task"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "project_overview",
                "A project's id, title with its done box, properties, its open tasks one per line, and its last three reports with their ids.",
                Schema(
                    required: [Prop("project", "string", "Project, name or id.")]),
                async (args, ct) =>
                    await projects.OverviewAsync(Str(args, "project"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "epic_properties",
                "An epic's id, title and properties: état, priority, Définition de fini, budget and charge. Its projects are listed by list_projects.",
                Schema(required: [Prop("epic", "string", "Epic, name or id.")]),
                async (args, ct) => await reads.EpicPropertiesAsync(Str(args, "epic"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "report_journal",
                "A session report's id, journal date, linked tasks by name, and its last journal entries (20 by default), with the count of earlier entries.",
                Schema(
                    required: [Prop("report", "string", "Report, id (from open_report or project_overview) or name.")],
                    optional: [Prop("last", "integer", "How many trailing entries to return. Default 20.", minimum: 1, maximum: 200)]),
                async (args, ct) =>
                    await reads.ReportJournalAsync(Str(args, "report"), IntOpt(args, "last") ?? 20, ct),
                ToolExecutionContract.ReadOnly),

            new(
                "idea_text",
                "An idea's id, title and text.",
                Schema(required: [Prop("idea", "string", "Idea, name or id.")]),
                async (args, ct) => await reads.IdeaTextAsync(Str(args, "idea"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "document_outline",
                "A document's id, title, type, version, and its headings with each section's length in characters. Read a section with document_section.",
                Schema(required: [Prop("document", "string", "Document, name or id.")]),
                async (args, ct) => await reads.DocumentOutlineAsync(Str(args, "document"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "document_section",
                "The text under one heading of a document. Omit heading for the text before the first heading (the whole body when it has none). An unknown heading is refused with the present headings listed.",
                Schema(
                    required: [Prop("document", "string", "Document, name or id.")],
                    optional: [Prop("heading", "string", "Heading text, without the leading '#'.")]),
                async (args, ct) =>
                    await reads.DocumentSectionAsync(Str(args, "document"), StrOpt(args, "heading"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "create_task",
                "Create a task under a project from the task template. Subtasks go in checklist, what to know in notes; both fill the template's sections. Answers with the new task's id.",
                Schema(
                    required:
                    [
                        Prop("project", "string", "Parent project, name or id."),
                        Prop("name", "string", "Task title."),
                        Prop("type", "string", "Task type, key or display name. Keys: production (build, deliver), recherche (investigate), organiser (structure, tidy), echanger (discuss, decide with someone), gestion (manage, admin), surveillance (watch for a measured recurrence)."),
                    ],
                    optional:
                    [
                        Prop("priority", "integer", "Priority 0-5, 5 = highest.", minimum: 0, maximum: 5),
                        ArrayProp("checklist", "Subtask labels, one checklist item each."),
                        Prop("notes", "string", "Markdown for the notes section: what to know before working on the task."),
                    ]),
                async (args, ct) =>
                    await tasks.CreateAsync(
                        Str(args, "project"), Str(args, "name"), Str(args, "type"),
                        IntOpt(args, "priority"), StrArrayOpt(args, "checklist"), StrOpt(args, "notes"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "complete",
                "Set a project or task's built-in completion checkbox: omit done to mark it done, pass done:false to reopen it. This is the canonical finished signal for finite chantiers and executable tasks, distinct from the état select. Nothing is archived and inline checklist items are untouched.",
                Schema(
                    required: [Prop("object", "string", "Project or task, name or id.")],
                    optional: [Prop("done", "boolean", "Completion state; omit to mark done, pass false to reopen.")]),
                async (args, ct) =>
                    await query.CompleteAsync(Str(args, "object"), BoolOpt(args, "done") ?? true, ct),
                ToolExecutionContract.OverwritingIdempotent),

            new(
                "archive",
                "Set an object's Archivé checkbox: omit archived to archive it (take it out of the views), pass archived:false to bring it back. Works on any object that carries the checkbox — not reports, which stay searchable. Archiving is the lifecycle mechanism; the état select is separate.",
                Schema(
                    required: [Prop("object", "string", "Object, name or id.")],
                    optional: [Prop("archived", "boolean", "Archive state; omit to archive, pass false to restore.")]),
                async (args, ct) =>
                    await query.ArchiveAsync(Str(args, "object"), BoolOpt(args, "archived") ?? true, ct),
                ToolExecutionContract.OverwritingIdempotent),

            new(
                "link",
                "Link an object to targets via the natural relation of each (source, target) pair: task -> project, rapport -> task(s) (a report links the task(s) it concerns; its project is derived through them), project -> project (depend_de — the source depends on the target), project -> epic (collection membership). Targets are appended, never replaced.",
                Schema(
                    required:
                    [
                        Prop("object", "string", "Source object, name or id."),
                        ArrayProp("targets", "Targets to link, each a name or id."),
                    ]),
                async (args, ct) =>
                    await query.LinkAsync(Str(args, "object"), StrArray(args, "targets"), ct),
                ToolExecutionContract.AdditiveUncertain),

            new(
                "list_projects",
                "List projects, grouped by état; filter to one state if given.",
                Schema(
                    optional: [Prop("state", "string", "État to filter on, key or display name: termine, ouvert, en_cours, dormant, en_attente, abandonne. Omit for all non-archived.")]),
                async (args, ct) =>
                    await projects.ListAsync(StrOpt(args, "state"), ct),
                ToolExecutionContract.ReadOnly),

            new(
                "search",
                "Find objects by words in their name and body: one line per hit, type · name, no id. Pass context:true to tell candidates apart: project and task hits add Description and Définition de fini, every hit adds up to three body lines. Then read the chosen object with its typed read, which answers with its id.",
                Schema(
                    required: [Prop("text", "string", "Free-text query.")],
                    optional:
                    [
                        ArrayProp("types", "Type keys to restrict the search; omit for any type.",
                                  itemEnum: ["epic", "project", "task", "rapport", "idee", "document"]),
                        Prop("context", "boolean", "Add Description, Définition de fini and up to three body lines per hit. Omit for identities only."),
                    ]),
                async (args, ct) =>
                    await query.SearchAsync(
                        Str(args, "text"), StrArrayOpt(args, "types"),
                        BoolOpt(args, "context") ?? false, ct),
                ToolExecutionContract.ReadOnly),

            new(
                "subtask",
                "Set a subtask of a task's checklist to the requested state; the label matches case-insensitively. When none matches, a new item is added to the checklist in that state.",
                Schema(
                    required:
                    [
                        Prop("task", "string", "Task, name or id."),
                        Prop("label", "string", "Checklist item label to add or set."),
                        Prop("done", "boolean", "Exact checked state to set."),
                    ]),
                async (args, ct) =>
                    await tasks.SubtaskAsync(
                        Str(args, "task"), Str(args, "label"), Bool(args, "done"), ct),
                ToolExecutionContract.OverwritingIdempotent),

            new(
                "create_epic",
                "Create the permanent epic container at the top of the Epic / Chantier / Task model.",
                Schema(
                    required: [Prop("name", "string", "Epic title.")],
                    optional: [Prop("state", "string", "Starting état, key or display name: termine, ouvert, en_cours, dormant, en_attente, abandonne.")]),
                async (args, ct) =>
                    await projects.CreateEpicAsync(
                        Str(args, "name"), StrOpt(args, "state"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "create_project",
                "Create a finite chantier from the project template, optionally attach it to its epic, and set its starting état.",
                Schema(
                    required: [Prop("name", "string", "Project title.")],
                    optional:
                    [
                        Prop("epic", "string", "Epic collection to add the project to, name or id. Existing projects can be attached later with link."),
                        Prop("state", "string", "Starting état, key or display name: termine, ouvert, en_cours, dormant, en_attente, abandonne."),
                    ]),
                async (args, ct) =>
                    await projects.CreateAsync(
                        Str(args, "name"), StrOpt(args, "epic"), StrOpt(args, "state"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "create_idea",
                "Capture a free-form idea as a new idee object.",
                Schema(
                    required: [Prop("content", "string", "Idea text.")]),
                async (args, ct) =>
                    await query.CreateIdeaAsync(Str(args, "content"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "create_document",
                "Create a stable reference document in the Anytype Dev space. Use documents for durable reference material: architecture, instructions, nomenclature, specifications, research, tips. Body edits after creation go through replace_section; property edits go through update.",
                Schema(
                    required:
                    [
                        Prop("name", "string", "Document title."),
                        Prop("type", "string", "Document type, key or display name: astuce, nomenclature, reference, specification, instructions, rapport (Recherche), architecture."),
                    ],
                    optional:
                    [
                        Prop("body", "string", "Initial markdown body."),
                        Prop("version", "string", "Document version, when the document is versioned."),
                        Prop("system", "boolean", "Set Document système when this is a system/reference document Deckle should treat as doctrine."),
                    ]),
                async (args, ct) =>
                    await documents.CreateAsync(
                        Str(args, "name"), Str(args, "type"),
                        StrOpt(args, "body"), StrOpt(args, "version"),
                        BoolOpt(args, "system") ?? false, ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new(
                "update",
                "Rename an object and/or set its properties: pass `name` to retitle it, `properties` to set fields (keys or display names map to values). At least one of the two is required. Select and multi-select values must name existing options — an unknown value is rejected with the valid options listed; options are created by hand in Anytype, never through this tool. The markdown body is not a property and cannot be written with this tool.",
                Schema(
                    required:
                    [
                        Prop("object", "string", "Stable Anytype object id (bafy…); names are refused so a rename remains replayable."),
                    ],
                    optional:
                    [
                        Prop("name", "string", "New title for the object; omit to leave it unchanged. Rejected on rapport/idee, whose title is the first line of their body."),
                        ObjectProp("properties", "Map of property key or display name to value."),
                    ]),
                async (args, ct) =>
                    await query.UpdateAsync(Str(args, "object"), StrOpt(args, "name"), ObjOpt(args, "properties"), ct),
                ToolExecutionContract.OverwritingIdempotentWithStableTarget),

            new(
                "replace_section",
                "Replace the body under a markdown heading in an object's body, keeping every other section intact, then verify the write landed. Strict: the heading must already exist — the match is on the heading text, case-insensitive, and an absent or ambiguous heading is refused (with the present headings listed) so a mistyped title never creates a stray section. The heading line stays; only the lines under it, down to the next same-or-higher heading, are replaced. This is the body counterpart of update, which writes properties only.",
                Schema(
                    required:
                    [
                        Prop("object", "string", "Object whose body to edit, name or id."),
                        Prop("heading", "string", "Exact text of the section heading to replace, without the leading '#'."),
                        Prop("content", "string", "New markdown content to place under the heading; write it naturally, without escaping."),
                    ]),
                async (args, ct) =>
                    await query.ReplaceSectionAsync(
                        Str(args, "object"), Str(args, "heading"), Str(args, "content"), ct),
                ToolExecutionContract.OverwritingIdempotent),
        };
    }

    // ── Schema construction ───────────────────────────────────────────────────

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
                                     int? minimum = null, int? maximum = null,
                                     IReadOnlyList<string>? oneOf = null)
    {
        var schema = new JsonObject { ["type"] = type, ["description"] = description };
        if (minimum is not null) schema["minimum"] = minimum;
        if (maximum is not null) schema["maximum"] = maximum;
        if (oneOf is not null) schema["enum"] = ToEnum(oneOf);
        return (name, schema);
    }

    static (string, JsonObject) ArrayProp(string name, string description,
                                          IReadOnlyList<string>? itemEnum = null)
    {
        var items = new JsonObject { ["type"] = "string" };
        if (itemEnum is not null) items["enum"] = ToEnum(itemEnum);
        return (name, new JsonObject
        {
            ["type"] = "array",
            ["items"] = items,
            ["description"] = description,
        });
    }

    // Hard enums are reserved for strictly-keyed vocabularies (type keys). The
    // select vocabularies (état, type de tâche) accept key OR display name, so
    // they stay prose-enumerated — an enum would reject the display-name form.
    static JsonArray ToEnum(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }

    static (string, JsonObject) ObjectProp(string name, string description) =>
        (name, new JsonObject { ["type"] = "object", ["description"] = description });

    // ── Argument reading ──────────────────────────────────────────────────────

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

    static bool Bool(JsonObject? args, string name) =>
        BoolOpt(args, name)
        ?? throw new ArgumentException($"Missing required argument '{name}'.", name);

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

    static JsonObject? ObjOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonObject obj)
            throw new ArgumentException($"Argument '{name}' must be an object.", name);
        return (JsonObject)obj.DeepClone();
    }
}
