using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Anytype.Schema;

// A select / multi_select choice. Key travels on the wire; Name is the French
// display label.
public readonly record struct TagOption(string Key, string Name);

// One mapped property of a type. Format is one of:
//   "text" | "number" | "date" | "checkbox" | "select" | "multi_select" | "objects" | "files"
// Options is non-null only for select / multi_select.
public sealed record PropertyDef(string Key, string Label, string Format, TagOption[]? Options = null);

// ─── DevSpace — frozen schema map ────────────────────────────────────────────
//
// Freezes the live structure of Louis's project-management space (« Dev »),
// discovered against the running Anytype REST API on 2026-06-12. This file is
// the single source of truth for every type key, property key and tag-option
// key the gestures send over the wire. Everything here is measured fact, not
// interpretation — indicative mood, no conditional.
//
// The keys are transcribed VERBATIM, including the malformed ones the space
// actually carries. Three traps are real, not typos to fix here — fixing them
// in the space would break existing objects, so the code must speak the wire:
//
//   • Props.RapportsLies  = "rpport(s)_lie(s)"        — MISSPELLED key.
//   • Props.BudgetReel    = "budget_reel_("           — TRUNCATED key.
//   • Props.ChargeReelle  = "charge_estimee_(jours)"  — MISLEADING key (named
//     like an estimate, but it is the *real* charge field).
//
// Priorité is a select whose own key is an OPAQUE id, and whose tag options are
// a mix of opaque ids and bare integer strings — both forms are below.
//
// Properties are space-global in Anytype, so the keys live FLAT under Props.
// PropertiesFor() maps each type to the properties it carries, in digest order;
// the derived lookups (PropertyLabel, TagName, TryResolveProperty) read those
// tables — no parallel lists.
//
// No dependency outside the BCL by design: this is pure data the rest of the
// module reads.
public static class DevSpace
{
    // Documentation / test anchor only. The id the gestures actually target at
    // runtime comes from credentials.json (space_id), never from this constant.
    public const string DevSpaceId =
        "bafyreibaltekf6yw32suoj3g57ot7gxgmpjwi37k7mx5y6mdd4f3i7p4fa.54yhp4w3lgp";

    // ── Default templates ─────────────────────────────────────────────────────
    //
    // The REST API does NOT apply a type's default template on creation: POST
    // /objects builds a bare object unless the body carries `template_id`, which
    // copies the template's block/view structure. Measured live 2026-06-12 on the
    // Dev space: only project and task carry a template (idee and rapport have
    // none), so only their creations pass an id.
    public static class Templates
    {
        public const string Project = "bafyreib5ganxcahinie6cnbdfvchxfosclfvq6pggi57r7fwfylazemr5a";
        public const string Task    = "bafyreibhy53jvgxp6euzxrn3bmchbtty5eq555m5ybf2mw43wsrwcawzre";
    }

    // ── Type keys ────────────────────────────────────────────────────────────
    //
    // Layout in parentheses is the Anytype object layout backing the type.
    // The « session » type exists in the space but is dormant and unused — it
    // is deliberately absent here so nothing references it.
    public static class Types
    {
        public const string Epic     = "epic";     // layout: collection
        public const string Project  = "project";
        public const string Task     = "task";     // layout: action
        public const string Rapport  = "rapport";  // layout: note
        public const string Idee     = "idee";
        public const string Document = "document";
    }

    // ── Property keys (flat — properties are space-global) ────────────────────
    //
    // Display name → key. Format of each property is stated in PropertyDef
    // tables below; gestures build payloads off these keys, never off names.
    public static class Props
    {
        public const string Etat              = "etat";                     // select
        public const string Priorite          = "67c6d714341c1628147d7b1d"; // select — OPAQUE id
        public const string Tag               = "tag";                      // multi_select
        public const string Archive           = "archive";                  // checkbox
        public const string DefinitionDeFini  = "definition_de_fini";       // text
        public const string Version           = "version";                  // text
        public const string DueDate           = "due_date";                 // date

        // Built-in action-layout completion checkbox. Measured live 2026-06-12 on
        // the Dev space: a completed task carries `done [checkbox] = True`.
        public const string Done              = "done";                     // checkbox

        // Budget / charge — on epic and project.
        public const string BudgetEstime      = "budget_estime";          // number
        public const string BudgetReel        = "budget_reel_(";          // number — TRUNCATED key, real
        public const string ChargeEstimee     = "charge_estimee";         // number
        public const string ChargeReelle      = "charge_estimee_(jours)"; // number — MISLEADING key, real

        // Project-only.
        public const string PhaseProjet       = "phase_projet"; // select
        public const string DependDe          = "depend_de";    // objects

        // Task-only.
        public const string RelationProjet    = "relation_projet";   // objects (Projet(s) lié(s))
        public const string ContactLie        = "contact_lie";       // objects
        public const string RapportsLies      = "rpport(s)_lie(s)";  // objects — MISSPELLED key, real
        public const string FichiersLies      = "fichier(s)_lie(s)"; // files
        public const string TypeDeTache       = "type_de_tache";     // select
        public const string Livrables         = "livrable(s)";       // multi_select

        // Rapport-only.
        public const string DateDuJournal     = "date_du_journal";    // date
        public const string SessionsLiees     = "session(s)_liee(s)"; // objects — unused (dormant session type)

        // Document-only.
        public const string TypeDeDocument    = "type_de_document"; // select
        public const string DocumentSysteme   = "document_systeme"; // checkbox
    }

    // ── État (select on Props.Etat) ──────────────────────────────────────────
    public static class Etat
    {
        public const string EnAttente = "en_attente";

        public static readonly TagOption[] All =
        [
            new("termine", "Terminé"),
            new("ouvert", "Ouvert"),
            new("en_cours", "En cours"),
            new("dormant", "Dormant"),
            new("en_attente", "En attente"),
            new("abandonne", "Abandonné"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Priorité (select on Props.Priorite) ──────────────────────────────────
    //
    // Levels 0 and 4 carry opaque ids; 1,2,3,5 are bare integer strings. Name
    // here is the integer-as-string label the space shows. Array position IS
    // the priority level (0..5).
    public static class Priority
    {
        public static readonly TagOption[] All =
        [
            new("67cc1782341c16068836b71e", "0"),
            new("1", "1"),
            new("2", "2"),
            new("3", "3"),
            new("67c6d722341c1628147d7b1e", "4"),
            new("5", "5"),
        ];

        // Level (0-5) → wire key. Out-of-range throws: a caller asking for a
        // priority the space does not define is a programming error, not data.
        public static string KeyFor(int level)
        {
            if (level < 0 || level >= All.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(level), level, "Priority level must be 0-5.");
            return All[level].Key;
        }

        // Wire key → level (0-5), or null if the key is unknown.
        public static int? LevelFor(string key)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Key, key, StringComparison.Ordinal))
                    return i;
            return null;
        }

        // Resolve a user string to the wire key, accepting: a wire key, the
        // integer-string label (« 0 ».. « 5 »), or the bare level digit (which
        // also catches « 03 » / surrounding spaces). Null when nothing matches.
        public static string? Resolve(string nameOrKey)
        {
            string? direct = ResolveOption(All, nameOrKey);
            if (direct is not null) return direct;

            if (int.TryParse(nameOrKey, out int level) && level >= 0 && level < All.Length)
                return All[level].Key;

            return null;
        }

        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Type de tâche (select on Props.TypeDeTache) ───────────────────────────
    public static class TypeDeTache
    {
        public static readonly TagOption[] All =
        [
            new("production", "Produire"),
            new("recherche", "Chercher"),
            new("organiser", "Organiser"),
            new("echanger", "Échanger"),
            new("gestion", "Gérer"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Livrable(s) (multi_select on Props.Livrables) ─────────────────────────
    public static class Livrable
    {
        public static readonly TagOption[] All =
        [
            new("document_de_cadrage", "Texte"),
            new("regle_de_cadrage", "Règle de cadrage"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Type de document (select on Props.TypeDeDocument) ─────────────────────
    public static class TypeDeDocument
    {
        public static readonly TagOption[] All =
        [
            new("astuce", "Astuce"),
            new("nomenclature", "Nomenclature"),
            new("reference", "Référence"),
            new("specification", "Spécification"),
            new("instructions", "Instructions"),
            new("rapport", "Recherche"),
            new("architecture", "Architecture"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Phase projet (select on Props.PhaseProjet) ────────────────────────────
    public static class PhaseProjet
    {
        public static readonly TagOption[] All =
        [
            new("cadrage", "Cadrage"),
        ];

        public static string? Resolve(string nameOrKey) => ResolveOption(All, nameOrKey);
        public static string? NameFor(string? key) => NameOfOption(All, key);
    }

    // ── Per-type property tables ──────────────────────────────────────────────
    //
    // The mapped properties each type carries, in digest order. The derived
    // lookups below read these tables so there is a single data source.
    static readonly PropertyDef[] EpicProps =
    [
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.Tag, "Tag", "multi_select"),
        new(Props.Version, "Version", "text"),
        new(Props.DefinitionDeFini, "Définition de fini", "text"),
        new(Props.DueDate, "Date cible", "date"),
        new(Props.BudgetEstime, "Budget estimé", "number"),
        new(Props.BudgetReel, "Budget réel", "number"),
        new(Props.ChargeEstimee, "Charge estimée", "number"),
        new(Props.ChargeReelle, "Charge réelle", "number"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] ProjectProps =
    [
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.PhaseProjet, "Phase projet", "select", PhaseProjet.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.Tag, "Tag", "multi_select"),
        new(Props.Version, "Version", "text"),
        new(Props.DefinitionDeFini, "Définition de fini", "text"),
        new(Props.DueDate, "Date cible", "date"),
        new(Props.BudgetEstime, "Budget estimé", "number"),
        new(Props.BudgetReel, "Budget réel", "number"),
        new(Props.ChargeEstimee, "Charge estimée", "number"),
        new(Props.ChargeReelle, "Charge réelle", "number"),
        new(Props.DependDe, "Dépend de", "objects"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] TaskProps =
    [
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.TypeDeTache, "Type de tâche", "select", TypeDeTache.All),
        new(Props.Done, "Terminé", "checkbox"),
        new(Props.Tag, "Tag", "multi_select"),
        new(Props.DueDate, "Date cible", "date"),
        new(Props.RelationProjet, "Projet(s) lié(s)", "objects"),
        new(Props.ContactLie, "Contact(s) lié(s)", "objects"),
        new(Props.RapportsLies, "Rapport(s) lié(s)", "objects"),
        new(Props.FichiersLies, "Fichier(s) lié(s)", "files"),
        new(Props.Livrables, "Livrable(s)", "multi_select", Livrable.All),
        new(Props.DefinitionDeFini, "Définition de fini", "text"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] RapportProps =
    [
        new(Props.DateDuJournal, "Date du journal", "date"),
        new(Props.RelationProjet, "Projet(s) lié(s)", "objects"),
        new(Props.ContactLie, "Contact(s) lié(s)", "objects"),
        new(Props.FichiersLies, "Fichier(s) lié(s)", "files"),
        new(Props.SessionsLiees, "Session(s) liée(s)", "objects"),
        new(Props.Tag, "Tag", "multi_select"),
    ];

    static readonly PropertyDef[] IdeeProps =
    [
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.Tag, "Tag", "multi_select"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] DocumentProps =
    [
        new(Props.TypeDeDocument, "Type de document", "select", TypeDeDocument.All),
        new(Props.DocumentSysteme, "Document système", "checkbox"),
        new(Props.Tag, "Tag", "multi_select"),
        new(Props.Version, "Version", "text"),
    ];

    static readonly PropertyDef[] NoProps = [];

    // The type's mapped properties in digest order, or empty for an unknown type.
    public static IReadOnlyList<PropertyDef> PropertiesFor(string typeKey) => typeKey switch
    {
        Types.Epic     => EpicProps,
        Types.Project  => ProjectProps,
        Types.Task     => TaskProps,
        Types.Rapport  => RapportProps,
        Types.Idee     => IdeeProps,
        Types.Document => DocumentProps,
        _              => NoProps,
    };

    // Resolves a property name-or-key on a given type to its wire key + format.
    // Key match is exact; name match is case-insensitive against the label.
    public static bool TryResolveProperty(string typeKey, string nameOrKey, out string key, out string format)
    {
        foreach (PropertyDef def in PropertiesFor(typeKey))
        {
            if (string.Equals(def.Key, nameOrKey, StringComparison.Ordinal) ||
                string.Equals(def.Label, nameOrKey, StringComparison.OrdinalIgnoreCase))
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

    // Display label of a property key across every type, or null if unknown.
    public static string? PropertyLabel(string key)
    {
        foreach (PropertyDef def in AllProps())
            if (string.Equals(def.Key, key, StringComparison.Ordinal))
                return def.Label;
        return null;
    }

    // Display name of a tag key across every vocabulary, or null if unknown.
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

    // Resolve a tag value (key or display name) for a property's vocabulary.
    // PUBLIC: gestures call this when building select/multi_select payloads.
    // Throws ArgumentException listing the valid options on a miss — that error
    // text is the model-facing affordance, not a failure to hide.
    //
    // A property with no frozen vocabulary (e.g. « tag », a free multi_select)
    // has nothing to match against: the value passes through unchanged, since
    // such tags are space-managed, not enumerable here.
    public static string ResolveTag(string propKey, string nameOrKey)
    {
        TagOption[]? options = OptionsForProperty(propKey);
        if (options is null) return nameOrKey;

        string? key = ResolveOption(options, nameOrKey);
        if (key is not null) return key;

        string valid = string.Join(", ", options.Select(o => $"{o.Name} ({o.Key})"));
        throw new ArgumentException(
            $"Valeur « {nameOrKey} » inconnue pour « {propKey} ». Options : {valid}.",
            nameof(nameOrKey));
    }

    // ── Internal derivation helpers ───────────────────────────────────────────

    static IEnumerable<PropertyDef> AllProps()
    {
        // Properties are space-global; the same key recurs across type tables. A
        // first-seen-wins walk yields each key's label/options once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PropertyDef[] table in
            new[] { EpicProps, ProjectProps, TaskProps, RapportProps, IdeeProps, DocumentProps })
            foreach (PropertyDef def in table)
                if (seen.Add(def.Key))
                    yield return def;
    }

    // The vocabulary backing a select/multi_select property key, or null when the
    // property is not a known-vocabulary select.
    static TagOption[]? OptionsForProperty(string propKey)
    {
        foreach (PropertyDef def in AllProps())
            if (string.Equals(def.Key, propKey, StringComparison.Ordinal) && def.Options is { } options)
                return options;
        return null;
    }

    // Name-or-key matcher for a vocabulary (key exact, name case-insensitive).
    static string? ResolveOption(TagOption[] options, string nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey)) return null;
        foreach (TagOption option in options)
            if (string.Equals(option.Key, nameOrKey, StringComparison.Ordinal) ||
                string.Equals(option.Name, nameOrKey, StringComparison.OrdinalIgnoreCase))
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
