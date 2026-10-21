using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Anytype;

// A select / multi_select choice. Key travels on the wire; Name is the French
// display label.
public readonly record struct TagOption(string Key, string Name);

// One mapped property of a type. Format is one of:
//   "text" | "number" | "date" | "checkbox" | "select" | "multi_select" | "objects" | "files"
// Options is non-null only for select / multi_select.
public sealed record PropertyDef(string Key, string Label, string Format, TagOption[]? Options = null);

// Freezes the live structure of the project-management space (« Dev »),
// discovered against the running Anytype REST API. This file is the single
// source of truth for every type key, property key and tag-option key the
// gestures send over the wire.
//
// The keys are transcribed VERBATIM, including the malformed ones the space
// actually carries. Three traps are real, not typos to fix here — fixing them
// in the space would break existing objects, so the code must speak the wire:
//
//   • Props.TachesLiees   = "tache(s)_liee(s)"        — MALFORMED key (no accents).
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
        "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.aaaaaaaaaaa";

    // ── Default templates ─────────────────────────────────────────────────────
    //
    // On the v1 REST API an Epic created without `template_id` came out bare
    // (no properties, no views; see ProjectGesturesBugNotes). Whether v1 ever
    // applies a type's default template on its own is not established: the
    // openapi only documents `template_id` as an optional "template to use", and
    // Deckle always sends it. On v2 it is measured: POST /objects applies the
    // type default when no `template_id` is sent (receipt `source: type_default`).
    // Epic, project and task carry a template (idee and rapport have none), so
    // their creations pass its id explicitly, which holds on both versions.
    public static class Templates
    {
        public const string Epic    = "bafyreiebxtbakbgodx2demmj4ukohifxxguowaiiofrn7uvibxy24tx76m";
        public const string Project = "bafyreib5ganxcahinie6cnbdfvchxfosclfvq6pggi57r7fwfylazemr5a";
        public const string Task    = "bafyreibhy53jvgxp6euzxrn3bmchbtty5eq555m5ybf2mw43wsrwcawzre";
    }

    // ── Type keys ────────────────────────────────────────────────────────────
    //
    // A trailing `layout:` note names the Anytype object layout backing the type.
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
        public const string Chat     = "chat_derived"; // layout: chat
    }

    // ── Property keys (flat — properties are space-global) ────────────────────
    //
    // Gestures build payloads off these keys, never off display names.
    public static class Props
    {
        public const string Etat              = "etat";                     // select
        public const string Priorite          = "67c6d714341c1628147d7b1d"; // select — OPAQUE id
        // « tag » is Anytype's auto-transversal multi_select: it re-attaches itself
        // to every object and resists deletion. It is deliberately NOT mapped onto
        // any type table below, so it stays out of every digest.
        public const string Tag               = "tag";                      // multi_select — auto-transversal, unmapped
        public const string Archive           = "archive";                  // checkbox
        public const string Description       = "description";              // text
        public const string DefinitionDeFini  = "definition_de_fini";       // text
        public const string Version           = "version";                  // text
        public const string DueDate           = "due_date";                 // date

        // Built-in action-layout completion checkbox; task and project both use
        // the action layout.
        public const string Done              = "done";                     // checkbox

        public const string BudgetEstime      = "budget_estime";          // number
        public const string BudgetReel        = "budget_reel_(";          // number — TRUNCATED key, real
        public const string ChargeEstimee     = "charge_estimee";         // number
        public const string ChargeReelle      = "charge_estimee_(jours)"; // number — MISLEADING key, real

        public const string PhaseProjet       = "phase_projet"; // select
        public const string DependDe          = "depend_de";    // objects

        public const string RelationProjet    = "relation_projet";   // objects (Projet(s) lié(s))
        public const string ContactLie        = "contact_lie";       // objects
        public const string FichiersLies      = "fichier(s)_lie(s)"; // files
        public const string TypeDeTache       = "type_de_tache";     // select
        public const string Livrables         = "livrable(s)";       // multi_select

        public const string DateDuJournal     = "date_du_journal";    // date
        public const string TachesLiees       = "tache(s)_liee(s)";   // objects — Tâche(s) liée(s), MALFORMED key

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
    // Array position IS the priority level (0..5).
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
            new("surveillance", "Surveiller"),
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
    // Row order is digest order.
    static readonly PropertyDef[] EpicProps =
    [
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.DefinitionDeFini, "Définition de fini", "text"),
        new(Props.BudgetEstime, "Budget estimé", "number"),
        new(Props.BudgetReel, "Budget réel", "number"),
        new(Props.ChargeEstimee, "Charge estimée", "number"),
        new(Props.ChargeReelle, "Charge réelle", "number"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] ProjectProps =
    [
        new(Props.Description, "Description", "text"),
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.PhaseProjet, "Phase projet", "select", PhaseProjet.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.Done, "Terminé", "checkbox"),
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
        new(Props.Description, "Description", "text"),
        new(Props.Etat, "État", "select", Etat.All),
        new(Props.Priorite, "Priorité", "select", Priority.All),
        new(Props.TypeDeTache, "Type de tâche", "select", TypeDeTache.All),
        new(Props.Done, "Terminé", "checkbox"),
        new(Props.DueDate, "Date cible", "date"),
        new(Props.RelationProjet, "Projet(s) lié(s)", "objects"),
        new(Props.ContactLie, "Contact(s) lié(s)", "objects"),
        new(Props.FichiersLies, "Fichier(s) lié(s)", "files"),
        new(Props.Livrables, "Livrable(s)", "multi_select", Livrable.All),
        new(Props.DefinitionDeFini, "Définition de fini", "text"),
        new(Props.ChargeEstimee, "Charge estimée", "number"),
        new(Props.ChargeReelle, "Charge réelle", "number"),
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] RapportProps =
    [
        new(Props.DateDuJournal, "Date du journal", "date"),
        new(Props.TachesLiees, "Tâche(s) liée(s)", "objects"),
        new(Props.ContactLie, "Contact(s) lié(s)", "objects"),
        new(Props.FichiersLies, "Fichier(s) lié(s)", "files"),
    ];

    static readonly PropertyDef[] IdeeProps =
    [
        new(Props.Archive, "Archivé", "checkbox"),
    ];

    static readonly PropertyDef[] DocumentProps =
    [
        new(Props.TypeDeDocument, "Type de document", "select", TypeDeDocument.All),
        new(Props.DocumentSysteme, "Document système", "checkbox"),
        new(Props.Version, "Version", "text"),
    ];

    static readonly PropertyDef[] ChatProps =
    [
        new(Props.TachesLiees, "Tâche(s) liée(s)", "objects"),
    ];

    static readonly PropertyDef[] NoProps = [];

    public static IReadOnlyList<PropertyDef> PropertiesFor(string typeKey) => typeKey switch
    {
        Types.Epic     => EpicProps,
        Types.Project  => ProjectProps,
        Types.Task     => TaskProps,
        Types.Rapport  => RapportProps,
        Types.Idee     => IdeeProps,
        Types.Document => DocumentProps,
        Types.Chat     => ChatProps,
        _              => NoProps,
    };

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

        string? key = ResolveOption(options, nameOrKey);
        if (key is not null) return key;

        string valid = string.Join(", ", options.Select(o => $"{o.Name} ({o.Key})"));
        throw new ArgumentException(
            $"Valeur « {nameOrKey} » inconnue pour « {propKey} ». Options : {valid}.",
            nameof(nameOrKey));
    }

    public static bool HasFrozenVocabulary(string propKey) =>
        OptionsForProperty(propKey) is not null;

    // ── Internal derivation helpers ───────────────────────────────────────────

    static IEnumerable<PropertyDef> AllProps()
    {
        // Properties are space-global; the same key recurs across type tables. A
        // first-seen-wins walk yields each key's label/options once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PropertyDef[] table in
            new[] { EpicProps, ProjectProps, TaskProps, RapportProps, IdeeProps, DocumentProps, ChatProps })
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
