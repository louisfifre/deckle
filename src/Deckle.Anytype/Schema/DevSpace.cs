using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Anytype;

// A select / multi_select choice. Key travels on the wire; Name is its name in
// the display language (PmTerms).
public readonly record struct TagOption(string Key, string Name);

// One mapped property of a type. Format is one of:
//   "text" | "number" | "date" | "checkbox" | "select" | "multi_select" | "objects" | "files"
//   | "email" | "phone" | "url"
// Options is non-null only for select / multi_select.
public sealed record PropertyDef(string Key, string Label, string Format, TagOption[]? Options = null);

// Freezes the structure of the project-management space as the model v6 defines
// it: every type key, property key and select-option key the gestures send over
// the wire. It mirrors Schema/pm-schema-manifest.json, the file the schema-admin
// surface applies to build the space; PmSchemaManifestTests fails when the two
// drift (a key, a label, a format, an option or a property order).
//
// Keys are English, lowercase, words joined by `_`. Names are not here: they
// come from the terms files (Terms/pm.<language>.json, PmTerms), French for
// display, and a typed name is accepted in any shipped language.
// A property that belongs to one type carries the type as a prefix of its key
// (task_type, document_type); a property several types use keeps one key
// (documents, contacts, email).
//
// Properties are space-global in Anytype, so the keys live FLAT under Props.
// PropertiesFor() maps each type to the properties it carries, in the order the
// model declares them; the derived lookups (PropertyLabel, TagName,
// TryResolveProperty) read those tables, with no parallel list.
//
// No dependency outside the BCL by design: this is pure data the rest of the
// module reads.
public static class DevSpace
{
    // Documentation / test anchor only. The id the gestures actually target at
    // runtime comes from credentials.json (space_id), never from this constant.
    public const string DevSpaceId =
        "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.aaaaaaaaaaa";

    // ── Type keys ────────────────────────────────────────────────────────────
    //
    // A trailing `layout:` note names the Anytype object layout backing the type.
    // Every creation names no template: the type's default one applies (measured
    // on v2, receipt `source: type_default`), so the template is chosen in
    // Anytype, per type, and never by an id frozen here.
    public static class Types
    {
        public const string Epic     = "epic";     // layout: collection
        public const string Project  = "project";  // layout: basic
        public const string Task     = "task";     // layout: action
        public const string Report   = "report";   // layout: note
        public const string Idea     = "idea";     // layout: note
        public const string Document = "document"; // layout: basic
        public const string Person   = "person";   // layout: profile
        public const string Company  = "company";  // layout: profile
    }

    // ── Property keys (flat — properties are space-global) ────────────────────
    //
    // Gestures build payloads off these keys, never off display names.
    public static class Props
    {
        // « tag » is Anytype's auto-transversal multi_select: it re-attaches itself
        // to every object and resists deletion. It is deliberately NOT mapped onto
        // any type table below, so it stays out of every digest.
        public const string Tag               = "tag";               // multi_select — auto-transversal, unmapped

        // State is kept on projects only; Epic and Task carry a hidden copy the
        // MCP never writes.
        public const string Status            = "status";            // select
        public const string Priority          = "priority";          // select
        public const string DoneDefinition    = "done_definition";   // text
        public const string DueDate           = "due_date";          // date
        public const string Archive           = "archive";           // checkbox

        // Built-in action-layout completion checkbox. Tasks use it; a project has
        // none, it is closed by archiving.
        public const string Done              = "done";              // checkbox

        public const string BudgetEstimated   = "budget_estimated";  // number, euros
        public const string BudgetActual      = "budget_actual";     // number, euros
        public const string EffortEstimated   = "effort_estimated";  // number, hours (epic, project)
        public const string EffortActual      = "effort_actual";     // number, hours (epic, project)
        public const string TimeEstimated     = "time_estimated";    // number, hours (task)
        public const string TimeActual        = "time_actual";       // number, hours (task), computed from its reports

        public const string TaskType          = "task_type";         // select
        public const string Slot              = "slot";              // select
        public const string Files             = "files";             // files

        public const string DocumentType      = "document_type";     // select
        public const string DocumentGlobal    = "document_global";   // checkbox
        public const string Version           = "version";           // text

        public const string Role              = "role";              // select
        public const string Email             = "email";             // email
        public const string Phone             = "phone";             // phone
        public const string Website           = "website";           // url
        public const string Address           = "address";           // text

        public const string Epic              = "epic";              // objects — the project's epic, zero or one
        public const string DependsOn         = "depends_on";        // objects — projects
        public const string Projects          = "projects";          // objects — a task's projects, one or more
        public const string OrchestratedBy    = "orchestrated_by";   // objects — an orchestrated task's orchestration task, zero or one
        public const string Tasks             = "tasks";           // objects — a report's tasks, one or more
        public const string Documents         = "documents";         // objects
        public const string Contacts          = "contacts";          // objects — people or companies
        public const string Company           = "company";           // objects — a person's company, zero or one
    }

    // Keys Anytype itself serves on every object. They are not in the manifest:
    // the app owns them.
    public static class Native
    {
        // When the object was made. A report has no journal-date property in
        // the model v6: its date is this one.
        public const string CreatedDate = "created_date";
    }

    // ── État (select on Props.Status) ────────────────────────────────────────
    //
    // Kept on projects only. A project that is finished or abandoned is not a
    // state: it is archived (Props.Archive).
    public static class Status
    {
        public const string Ready = "ready";
        public const string Doing = "doing";
        public const string Paused = "paused";
        public const string Dormant = "dormant";

        public static readonly TagOption[] All =
        [
            Opt(Props.Status, Ready),
            Opt(Props.Status, Doing),
            Opt(Props.Status, Paused),
            Opt(Props.Status, Dormant),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(Props.Status, All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Priorité (select on Props.Priority) ──────────────────────────────────
    //
    // Array position IS the priority level (0..5).
    public static class Priority
    {
        public static readonly TagOption[] All =
        [
            Opt(Props.Priority, "p_0"),
            Opt(Props.Priority, "p_1"),
            Opt(Props.Priority, "p_2"),
            Opt(Props.Priority, "p_3"),
            Opt(Props.Priority, "p_4"),
            Opt(Props.Priority, "p_5"),
        ];

        // Out-of-range throws: a caller asking for a priority the space does not
        // define is a programming error, not data.
        public static string KeyFor(int level)
        {
            if (level < 0 || level >= All.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(level), level, "Priority level must be 0-5.");
            return All[level].Key;
        }

        public static int? LevelFor(string key)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Key, key, StringComparison.Ordinal))
                    return i;
            return null;
        }

        // Accepts a wire key, the integer-string label (« 0 ».. « 5 »), or the
        // bare level digit (which also catches « 03 » / surrounding spaces).
        public static string? Resolve(string nameOrKey)
        {
            string? direct = ResolveOption(Props.Priority, All, nameOrKey);
            if (direct is not null) return direct;

            if (int.TryParse(nameOrKey, out int level) && level >= 0 && level < All.Length)
                return All[level].Key;

            return null;
        }

        public static string? NameFor(string? key) => NameOfOption(All, key);

        // v2 serves a select as its option NAME (measured 2026-10-22:
        // `"priority":["4"]`), where v1 served the wire key. The name is the
        // level, so the position of the matching option is the answer.
        public static int? LevelForName(string? name)
        {
            if (name is null) return null;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Name, name.Trim(), StringComparison.Ordinal))
                    return i;
            return null;
        }
    }

    // ── Type de tâche (select on Props.TaskType) ──────────────────────────────
    public static class TaskType
    {
        public const string Orchestration = "orchestration";

        public static readonly TagOption[] All =
        [
            // First: orchestration tasks are the most important ones. Their
            // done definition is that every task they orchestrate is ticked or
            // archived.
            Opt(Props.TaskType, Orchestration),
            Opt(Props.TaskType, "research"),
            Opt(Props.TaskType, "production"),
            Opt(Props.TaskType, "organizing"),
            Opt(Props.TaskType, "exchange"),
            Opt(Props.TaskType, "management"),
            Opt(Props.TaskType, "monitoring"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(Props.TaskType, All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Créneau (select on Props.Slot) ────────────────────────────────────────
    //
    // The part of the day a task belongs to; not an hour. A task takes Cap unless
    // told otherwise (model v6, section Task).
    public static class Slot
    {
        public static readonly string[] Keys = [Props.Slot];
        public static string Name => Label(Props.Slot);

        // The option a task takes when none is asked, by key: the live space
        // may name it in any language (PmTerms.OptionNames).
        public const string Default = "cap";

        public static readonly TagOption[] All =
        [
            Opt(Props.Slot, "cap"),
            Opt(Props.Slot, "passion"),
            Opt(Props.Slot, "training"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(Props.Slot, All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Type de document (select on Props.DocumentType) ───────────────────────
    public static class DocumentType
    {
        public static readonly TagOption[] All =
        [
            Opt(Props.DocumentType, "tip"),
            Opt(Props.DocumentType, "nomenclature"),
            Opt(Props.DocumentType, "reference"),
            Opt(Props.DocumentType, "specification"),
            Opt(Props.DocumentType, "instructions"),
            // Not « research »: the server keeps option keys unique across the
            // whole space, and task_type already owns that one (measured on a
            // throwaway space, 2026-11-09).
            Opt(Props.DocumentType, "research_note"),
            Opt(Props.DocumentType, "architecture"),
            Opt(Props.DocumentType, "resume"),
            Opt(Props.DocumentType, "proposal"),
            Opt(Props.DocumentType, "minutes"),
            Opt(Props.DocumentType, "procedure"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(Props.DocumentType, All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Rôle (select on Props.Role) ───────────────────────────────────────────
    //
    // A person's role towards us, not their trade (model v6, validated by Louis
    // on 2026-11-09).
    public static class Role
    {
        public static readonly string[] Keys = [Props.Role];
        public static string Name => Label(Props.Role);

        public static readonly TagOption[] All =
        [
            Opt(Props.Role, "administrative"),
            Opt(Props.Role, "decision"),
            Opt(Props.Role, "finance"),
            Opt(Props.Role, "product"),
            Opt(Props.Role, "technical"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(Props.Role, All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Per-type property tables ──────────────────────────────────────────────
    //
    // Row order is the order the model declares them, which is the digest order
    // and the order the views offer them.
    static readonly PropertyDef[] EpicProps =
    [
        new(Props.Status, Label(Props.Status), "select", Status.All),
        new(Props.Priority, Label(Props.Priority), "select", Priority.All),
        new(Props.DoneDefinition, Label(Props.DoneDefinition), "text"),
        new(Props.BudgetEstimated, Label(Props.BudgetEstimated), "number"),
        new(Props.BudgetActual, Label(Props.BudgetActual), "number"),
        new(Props.EffortEstimated, Label(Props.EffortEstimated), "number"),
        new(Props.EffortActual, Label(Props.EffortActual), "number"),
        new(Props.Documents, Label(Props.Documents), "objects"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] ProjectProps =
    [
        new(Props.Status, Label(Props.Status), "select", Status.All),
        new(Props.Priority, Label(Props.Priority), "select", Priority.All),
        new(Props.DoneDefinition, Label(Props.DoneDefinition), "text"),
        new(Props.DueDate, Label(Props.DueDate), "date"),
        new(Props.BudgetEstimated, Label(Props.BudgetEstimated), "number"),
        new(Props.BudgetActual, Label(Props.BudgetActual), "number"),
        new(Props.EffortEstimated, Label(Props.EffortEstimated), "number"),
        new(Props.EffortActual, Label(Props.EffortActual), "number"),
        new(Props.Epic, Label(Props.Epic), "objects"),
        new(Props.DependsOn, Label(Props.DependsOn), "objects"),
        new(Props.Documents, Label(Props.Documents), "objects"),
        new(Props.Contacts, Label(Props.Contacts), "objects"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] TaskProps =
    [
        // Anytype refuses to rename its native `done` (403, measured 2026-11-11),
        // in the app as through the API: it keeps its English name.
        new(Props.Done, Label(Props.Done), "checkbox"),
        new(Props.TaskType, Label(Props.TaskType), "select", TaskType.All),
        new(Props.Priority, Label(Props.Priority), "select", Priority.All),
        new(Props.DueDate, Label(Props.DueDate), "date"),
        new(Props.Slot, Label(Props.Slot), "select", Slot.All),
        new(Props.DoneDefinition, Label(Props.DoneDefinition), "text"),
        new(Props.Projects, Label(Props.Projects), "objects"),
        new(Props.OrchestratedBy, Label(Props.OrchestratedBy), "objects"),
        new(Props.Documents, Label(Props.Documents), "objects"),
        new(Props.Contacts, Label(Props.Contacts), "objects"),
        new(Props.TimeEstimated, Label(Props.TimeEstimated), "number"),
        new(Props.TimeActual, Label(Props.TimeActual), "number"),
        new(Props.Files, Label(Props.Files), "files"),
        new(Props.Status, Label(Props.Status), "select", Status.All),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] ReportProps =
    [
        new(Props.Tasks, Label(Props.Tasks), "objects"),
        new(Props.Contacts, Label(Props.Contacts), "objects"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] IdeaProps =
    [
        new(Props.Documents, Label(Props.Documents), "objects"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] DocumentProps =
    [
        new(Props.DocumentType, Label(Props.DocumentType), "select", DocumentType.All),
        new(Props.DocumentGlobal, Label(Props.DocumentGlobal), "checkbox"),
        new(Props.Version, Label(Props.Version), "text"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] PersonProps =
    [
        new(Props.Company, Label(Props.Company), "objects"),
        new(Props.Role, Label(Props.Role), "select", Role.All),
        new(Props.Email, Label(Props.Email), "email"),
        new(Props.Phone, Label(Props.Phone), "phone"),
        new(Props.Website, Label(Props.Website), "url"),
        new(Props.Address, Label(Props.Address), "text"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] CompanyProps =
    [
        new(Props.Email, Label(Props.Email), "email"),
        new(Props.Phone, Label(Props.Phone), "phone"),
        new(Props.Website, Label(Props.Website), "url"),
        new(Props.Address, Label(Props.Address), "text"),
        new(Props.Archive, Label(Props.Archive), "checkbox"),
    ];

    static readonly PropertyDef[] NoProps = [];

    public static IReadOnlyList<PropertyDef> PropertiesFor(string typeKey) => typeKey switch
    {
        Types.Epic     => EpicProps,
        Types.Project  => ProjectProps,
        Types.Task     => TaskProps,
        Types.Report   => ReportProps,
        Types.Idea     => IdeaProps,
        Types.Document => DocumentProps,
        Types.Person   => PersonProps,
        Types.Company  => CompanyProps,
        _              => NoProps,
    };

    // Every type key the tables know, in the model's order.
    public static IReadOnlyList<string> TypeKeys { get; } =
    [
        Types.Epic, Types.Project, Types.Task, Types.Report,
        Types.Idea, Types.Document, Types.Person, Types.Company,
    ];

    public static bool TryResolveProperty(string typeKey, string nameOrKey, out string key, out string format)
    {
        foreach (PropertyDef def in PropertiesFor(typeKey))
        {
            if (string.Equals(def.Key, nameOrKey, StringComparison.Ordinal) ||
                PmTerms.IsPropertyName(def.Key, nameOrKey))
            {
                key = def.Key;
                format = def.Format;
                return true;
            }
        }

        key = "";
        format = "";
        return false;
    }

    public static string? PropertyLabel(string key)
    {
        foreach (PropertyDef def in AllProps())
            if (string.Equals(def.Key, key, StringComparison.Ordinal))
                return def.Label;
        return null;
    }

    public static string? TagName(string tagKey)
    {
        foreach (PropertyDef def in AllProps())
            if (def.Options is { } options)
            {
                string? name = NameOfOption(options, tagKey);
                if (name is not null) return name;
            }
        return null;
    }

    // A miss throws ArgumentException listing the valid options — that error
    // text is the model-facing affordance, not a failure to hide.
    //
    // A property with NO frozen vocabulary (e.g. « tag ») is a programming
    // error here: passing its value on would let an unknown option reach the
    // wire. Gestures route those through HasFrozenVocabulary to LiveTagResolver.
    public static string ResolveTag(string propKey, string nameOrKey)
    {
        TagOption[]? options = OptionsForProperty(propKey);
        if (options is null)
            throw new InvalidOperationException(
                $"« {propKey} » n'a pas de vocabulaire figé : ses options doivent être " +
                "résolues sur l'espace, pas via DevSpace.ResolveTag.");

        string? key = ResolveOption(propKey, options, nameOrKey);
        if (key is not null) return key;

        string valid = string.Join(", ", options.Select(o => $"{o.Name} ({o.Key})"));
        throw new ArgumentException(
            $"Valeur « {nameOrKey} » inconnue pour « {propKey} ». Options : {valid}.",
            nameof(nameOrKey));
    }

    public static bool HasFrozenVocabulary(string propKey) =>
        OptionsForProperty(propKey) is not null;

    // ── Internal derivation helpers ───────────────────────────────────────────

    static string Label(string propKey) => PmTerms.Display.PropertyName(propKey);

    static TagOption Opt(string propKey, string optionKey) =>
        new(optionKey, PmTerms.Display.OptionName(propKey, optionKey));

    static IEnumerable<PropertyDef> AllProps()
    {
        // Properties are space-global; the same key recurs across type tables. A
        // first-seen-wins walk yields each key's label/options once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PropertyDef[] table in
            new[] { EpicProps, ProjectProps, TaskProps, ReportProps, IdeaProps, DocumentProps,
                    PersonProps, CompanyProps })
            foreach (PropertyDef def in table)
                if (seen.Add(def.Key))
                    yield return def;
    }

    static TagOption[]? OptionsForProperty(string propKey)
    {
        foreach (PropertyDef def in AllProps())
            if (string.Equals(def.Key, propKey, StringComparison.Ordinal) && def.Options is { } options)
                return options;
        return null;
    }

    static string? ResolveOption(string propKey, TagOption[] options, string nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey)) return null;
        foreach (TagOption option in options)
            if (string.Equals(option.Key, nameOrKey, StringComparison.Ordinal) ||
                PmTerms.IsOptionName(propKey, option.Key, nameOrKey))
                return option.Key;
        return null;
    }

    static string? NameOfOption(TagOption[] options, string? key)
    {
        if (key is null) return null;
        foreach (TagOption option in options)
            if (string.Equals(option.Key, key, StringComparison.Ordinal))
                return option.Name;
        return null;
    }
}
