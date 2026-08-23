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
        "Anytype project-management space for Deckle. Shared model: Deckle is the "
        + "permanent Epic; each finite workstream is a Project (chantier); each "
        + "executable unit is a Task. Subtasks are inline '- [ ]' checklist items "
        + "in the task body, not separate objects. The built-in done checkbox is "
        + "the canonical completion signal for projects and tasks; état remains "
        + "planning state, and archive only removes an object from active views. "
        + "Before work that changes the space, call "
        + "session_start on the anchor task, then pass its report_id to log as "
        + "you journal the why; plain reads need no session. Shared vocabulary — états: "
        + "termine, ouvert, en_cours, dormant, en_attente, abandonne; priority "
        + "0-5, 5 highest; content is French. Fill properties at creation: "
        + "date cible and définition de fini everywhere, plus estimated budget "
        + "and charge on projects — their 'réel' counterparts are set at "
        + "validation, so the estimate/actual delta stays readable. Select "
        + "options are applied, never created: new options come from the user, "
        + "in Anytype. Names "
        + "resolve to objects; an ambiguous name returns candidate ids so you "
        + "can retry with one.");

    public static readonly McpSurfaceDescriptor Dialogues = new(
        "deckle-anytype-dialogues",
        "Deckle Anytype Dialogues",
        "Anytype dialogue chats for mediated LLM discussions. Create a dialogue "
        + "chat for start, challenge, or dialogue work; post turns as system, "
        + "claude, codex, or louis; read the chat before each new turn and use "
        + "after_order_id to continue from the last seen message. These tools are "
        + "not project-management reports and do not journal work sessions.");

    public static readonly McpSurfaceDescriptor SchemaAdmin = new(
        "deckle-anytype-schema-admin",
        "Deckle Anytype Schema Admin",
        "Anytype schema administration surface. It inspects configured space "
        + "aliases, previews additive type/property/tag/description changes, then "
        + "applies a previous preview only when confirm:true is passed. It also exposes "
        + "two bounded cross-space utilities: additive collection membership and "
        + "select writes addressed by existing tag keys. It never accepts a "
        + "raw space_id; use configured aliases such as dev or home. Schema "
        + "manifests stay additive only: no delete, key rename, property format "
        + "change, or property removal.");

    public static readonly McpSurfaceDescriptor All = new(
        "deckle-anytype-all",
        "Deckle Anytype All",
        ProjectManagement.Instructions + "\n\n" + Dialogues.Instructions);
}
