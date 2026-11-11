using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// A surface is the unit the resident host mounts for one authenticated client.
// The host owns transport and identity; the surface opens a fresh tool graph for
// every stateless request and owns its model-facing descriptor. Domain adapters
// can declare surfaces from their own assemblies without being referenced here.
public sealed record McpSurface(
    string Id,
    Func<AnytypeApiClient, McpSurfaceBinding> Open);

public sealed record McpSurfaceBinding(
    IReadOnlyList<ToolDescriptor> Tools,
    McpSurfaceDescriptor Descriptor);

public sealed record McpSurfaceDescriptor(string Name, string Title, string Instructions)
{
    public static readonly McpSurfaceDescriptor ProjectManagement = new(
        "deckle-anytype",
        "Deckle Anytype",
        "Anytype project-management space for Deckle. Shared model: Epic, then "
        + "Project, then Task. Deckle is the permanent Epic. A Project is a durable "
        + "subject of the work, such as transcription, the design system or "
        + "observability, never a module, a deliverable nor a stage: its name does "
        + "not change with the work, and the work is added to it as tasks. A project "
        + "has no done checkbox; it is closed by archiving it, finished or abandoned "
        + "alike, and archive refuses while one of its tasks is open. A Task is one "
        + "executable unit that moves its subject forward, about an hour of work; "
        + "the built-in done checkbox is the completion signal of a task, which is "
        + "ticked when finished and archived without the tick when abandoned. New "
        + "work joins the project of its subject, dormant ones included, and a "
        + "project is created only for a subject the space does not hold yet. "
        + "Subtasks are inline '- [ ]' checklist items "
        + "in the task body, not separate objects. État is kept on projects only and "
        + "changed by the person or on their request; epics and tasks carry a hidden "
        + "one that is never written. "
        + "Work on a task follows one ritual. Place yourself first: search, or a "
        + "typed read given words (project_overview of a project, task_properties "
        + "of a task). See the project's tasks, read the task, then call "
        + "open_report with the task id. If the work deserves a task and none "
        + "exists, create it first. A report always belongs to a task and is "
        + "opened as soon as work on a task starts: no report without a task. "
        + "Pass its report_id to log as you journal the why; plain reads need no "
        + "report. When work resumes from a summary, after a compaction or in a "
        + "new session, the task and its report are the record: read the "
        + "report's journal and the task's checklist before the next action, and "
        + "trust them where they differ from the summary. Reads are typed and "
        + "named by what they return (task_properties, task_checklist, task_notes, "
        + "project_overview, epic_properties, report_journal, idea_text, document_outline, "
        + "document_section); each answers with the object id first. They accept "
        + "words, not only an exact name or an id: an exact name wins, else a "
        + "single match wins, else the candidates are listed. So a typed read given "
        + "words finds the object and reads it in one call; search is only for when "
        + "the type is not known. When the work belongs to "
        + "a task of this space, open the first answer by placing it in its project, "
        + "so the person sees at once whether the session aims at the right one. "
        + "Shared vocabulary — project états: "
        + "ready (Prêt), doing (En cours), paused (En pause), dormant (Dormant); "
        + "priority 0-5, 5 highest; content is French. Fill properties at "
        + "creation: on a project, état, priority and a broad done_definition "
        + "(the subject is finished when…); on a task, type, project, a fine "
        + "done_definition (what lets it be ticked), time_estimated and, once it "
        + "is placed on a day, due_date. A task's real time is computed from its "
        + "reports by close_report, never typed. Documents and contacts are linked "
        + "from the work they serve with link. Select "
        + "options are applied, never created: new options come from the user, "
        + "in Anytype. Names "
        + "resolve to objects: a read takes the one object a name resembles, a "
        + "write needs the exact name or the id; an ambiguous or inexact name "
        + "returns candidate ids so you can retry with one. open_report and log "
        + "take an id only.");

    public static readonly McpSurfaceDescriptor SchemaAdmin = new(
        "deckle-anytype-schema-admin",
        "Deckle Anytype Schema Admin",
        "Anytype schema administration surface. It inspects configured space "
        + "aliases, previews additive type/property/tag/description changes, then "
        + "applies a previous preview only when confirm:true is passed. It also exposes "
        + "two bounded cross-space utilities: additive collection membership and "
        + "select writes addressed by existing tag keys. It never accepts a "
        + "raw space_id; use configured aliases such as dev or home. Schema "
        + "manifests stay additive for the space: no delete, key rename, or "
        + "property format change. A type's property list is the exception: when "
        + "a manifest lists a type's properties, the type's regular list becomes "
        + "exactly that list, in that order. A live property the manifest omits "
        + "is detached from the type, never deleted, and the preview names every "
        + "detachment and reorder before confirm. Header and hidden properties "
        + "are untouched: the app owns them.");
}
