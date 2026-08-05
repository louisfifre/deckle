using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

public static class HomeSchema
{
    public static class Types
    {
        public const string Room = "piece";
        public const string Circuit = "circuit_elec";
        public const string DistributionBoard = "tableau_elec";
        public const string Outlet = "prise";
        public const string Lighting = "eclairage";
        public const string Control = "commande";
        public const string Opening = "ouvrant";
        public const string Appliance = "appareil";
        public const string Network = "reseau";
        public const string Sensor = "capteur";
        public const string Relay = "relais";
        public const string Panel = "panneau";
        public const string Node = "noeud";
        public const string Idea = "idee";
        public const string Errand = "course";
        public const string Tool = "outil";
        public const string Worksite = "chantier";
        public const string Task = "tache";
    }

    public static class Properties
    {
        public const string Label = "libelle";
        public const string Room = "piece";
        public const string Category = "categorie";
        public const string Existence = "existence";
        public const string Condition = "etat";
        public const string Circuit = "circuit";
        public const string ObservedOn = "date_releve";
        public const string Notes = "notes";
        public const string Documents = "documents";
        public const string Horizon = "horizon";
        public const string Aisle = "rayon";
        public const string Quantity = "quantite";
        public const string Concerns = "concerne";
        public const string ModelReference = "reference_modele";
        public const string ToolCategory = "categorie_outil";
        public const string Supplier = "fournisseur";
        public const string Invoice = "facture";
        public const string StoredIn = "range_dans";
        public const string Status = "statut";
        public const string TargetDate = "date_cible";
        public const string Worksite = "chantier";
        // App-managed (Étage objects are created in the app, the type is not
        // part of the required contract), but the relation is written via MCP.
        public const string Floor = "etage";
    }

    public static class Existence
    {
        public const string Existing = "existant";
        public const string Planned = "prevu";
        public const string Removed = "depose";
    }

    public static class Status
    {
        public const string Open = "ouvert";
        public const string InProgress = "en_cours";
        public const string Waiting = "en_attente";
        public const string Dormant = "dormant";
        public const string Done = "termine";
        public const string Abandoned = "abandonne";
    }

    public static class Condition
    {
        public const string Good = "bon";
        public const string Worn = "vetuste";
        public const string Damaged = "endommage";
        public const string OutOfService = "hors_service";
    }

    public static readonly IReadOnlyList<string> ElementTypes =
    [
        Types.Outlet, Types.Lighting, Types.Control, Types.Opening, Types.Appliance,
        Types.Network, Types.Sensor, Types.Relay, Types.Panel, Types.Node,
    ];

    // House-life types: no nomenclature code, a free title (or a body-derived one
    // for ideas), and none of the element invariants. They share the space and
    // its guarded vocabularies, not the code grammar.
    public static readonly IReadOnlyList<string> LifeTypes =
    [Types.Idea, Types.Errand, Types.Tool];

    // Work types: the house's own pilotage — free-titled like life types, but
    // deliberately not the dev-space PM model: no journal (done tasks are the
    // record), no required properties at creation, orphan tasks allowed.
    public static readonly IReadOnlyList<string> WorkTypes =
    [Types.Worksite, Types.Task];

    public static readonly IReadOnlyList<string> CreatableTypes =
    [Types.Room, Types.Circuit, Types.DistributionBoard, .. ElementTypes, .. LifeTypes, .. WorkTypes];

    internal static readonly IReadOnlyDictionary<string, string> RequiredProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Properties.Label] = "text",
            [Properties.Room] = "objects",
            [Properties.Category] = "select",
            [Properties.Existence] = "select",
            [Properties.Condition] = "select",
            [Properties.Circuit] = "objects",
            [Properties.ObservedOn] = "date",
            [Properties.Notes] = "text",
            [Properties.Documents] = "files",
            [Properties.Horizon] = "select",
            [Properties.Aisle] = "select",
            [Properties.Quantity] = "text",
            [Properties.Concerns] = "objects",
            [Properties.ModelReference] = "text",
            [Properties.ToolCategory] = "select",
            [Properties.Supplier] = "select",
            [Properties.Invoice] = "files",
            [Properties.StoredIn] = "objects",
            [Properties.Status] = "select",
            [Properties.TargetDate] = "date",
            [Properties.Worksite] = "objects",
        };

    // Objects properties whose target must carry a specific type; unlisted
    // properties (concerne, range_dans) accept any Home object.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectPropertyTargets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Properties.Room] = [Types.Room],
            [Properties.Circuit] = [Types.Circuit],
            [Properties.Worksite] = [Types.Worksite],
            [Properties.Floor] = ["etage"],
        };

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType =
        BuildRequiredByType();

    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClosedVocabularies =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [Properties.Category] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["p"] = "P — prise 230 V", ["ps"] = "PS — prise spécialisée",
                ["l"] = "L — point lumineux", ["lr"] = "LR — ruban LED",
                ["c"] = "C — commande murale", ["v"] = "V — volet / ouvrant",
                ["a"] = "A — appareil fixe", ["rj"] = "RJ — prise réseau",
                ["rb"] = "RB — baie / coffret réseau", ["rt"] = "RT — coax TV",
                ["ds"] = "DS — capteur", ["dr"] = "DR — relais",
                ["dx"] = "DX — panneau de contrôle", ["de"] = "DE — nœud ESP32",
            },
            [Properties.Existence] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Existence.Existing] = "Existant",
                [Existence.Planned] = "Prévu",
                [Existence.Removed] = "Déposé",
            },
            [Properties.Condition] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Condition.Good] = "Bon",
                [Condition.Worn] = "Vétuste",
                [Condition.Damaged] = "Endommagé",
                [Condition.OutOfService] = "Hors service",
            },
            [Properties.Horizon] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["maintenant"] = "Maintenant",
                ["bientot"] = "Bientôt",
                ["un_jour"] = "Un jour",
                ["peut_etre"] = "Peut-être",
            },
            [Properties.Aisle] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alimentaire"] = "Alimentaire",
                ["bricolage"] = "Bricolage",
                ["maison"] = "Maison",
                ["jardin"] = "Jardin",
                ["autre"] = "Autre",
            },
            [Properties.ToolCategory] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["electroportatif"] = "Électroportatif",
                ["outil_a_main"] = "Outil à main",
                ["mesure"] = "Mesure",
                ["peinture"] = "Peinture",
                ["electronique"] = "Électronique",
                ["impression_3d"] = "Impression 3D",
                ["jardin"] = "Jardin",
                ["autre"] = "Autre",
            },
            [Properties.Status] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Status.Open] = "Ouvert",
                [Status.InProgress] = "En cours",
                [Status.Waiting] = "En attente",
                [Status.Dormant] = "Dormant",
                [Status.Done] = "Terminé",
                [Status.Abandoned] = "Abandonné",
            },
            [Properties.Supplier] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["leroy_merlin"] = "Leroy Merlin",
                ["brico_depot"] = "Brico Dépôt",
                ["amazon"] = "Amazon",
                ["manomano"] = "ManoMano",
                ["occasion"] = "Occasion",
                ["autre"] = "Autre",
            },
        };

    internal static JsonObject CreateRequiredSchemaManifest()
    {
        var properties = new JsonArray();
        foreach ((string key, string format) in RequiredProperties)
        {
            var property = new JsonObject
            {
                ["key"] = key,
                ["name"] = PropertyName(key),
                ["format"] = format,
            };
            if (ClosedVocabularies.TryGetValue(key, out var vocabulary))
            {
                var tags = new JsonArray();
                foreach ((string tagKey, string name) in vocabulary)
                    tags.Add(new JsonObject { ["key"] = tagKey, ["name"] = name });
                property["tags"] = tags;
            }
            properties.Add(property);
        }

        var types = new JsonArray();
        foreach (string type in CreatableTypes)
        {
            var attached = new JsonArray();
            foreach (string property in RequiredByType[type]) attached.Add(property);
            types.Add(new JsonObject
            {
                ["key"] = type,
                ["name"] = TypeName(type),
                ["plural_name"] = TypePluralName(type),
                ["layout"] = TypeLayout(type),
                ["properties"] = attached,
            });
        }

        return new JsonObject { ["types"] = types, ["properties"] = properties };
    }

    internal static HomeSchemaRuntime Validate(SchemaSnapshot snapshot)
    {
        var failures = new List<string>();

        foreach ((string key, string expectedFormat) in RequiredProperties)
        {
            if (!snapshot.Properties.TryGetValue(key, out SchemaPropertyInfo? property))
                failures.Add($"propriété manquante {key}");
            else if (!string.Equals(property.Format, expectedFormat, StringComparison.Ordinal))
                failures.Add($"propriété {key} : format {property.Format}, attendu {expectedFormat}");
        }

        foreach ((string typeKey, IReadOnlyList<string> requiredProperties) in RequiredByType)
        {
            if (!snapshot.Types.TryGetValue(typeKey, out SchemaTypeInfo? type))
            {
                failures.Add($"type manquant {typeKey}");
                continue;
            }

            foreach (string propertyKey in requiredProperties)
                if (snapshot.Properties.TryGetValue(propertyKey, out SchemaPropertyInfo? property)
                    && !type.PropertyLinks.Any(link => LinkMatches(link, property)))
                {
                    failures.Add($"type {typeKey} : propriété non attachée {propertyKey}");
                }
        }

        foreach ((string propertyKey, IReadOnlyDictionary<string, string> vocabulary) in ClosedVocabularies)
        {
            snapshot.TagsByProperty.TryGetValue(propertyKey, out var tags);
            tags ??= new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
            foreach ((string key, string name) in vocabulary)
                if (!tags.Values.Distinct().Any(tag =>
                        string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"vocabulaire {propertyKey} : option manquante {key}");
                }
        }

        if (failures.Count > 0)
            throw new HomeSchemaException(
                "Le schéma Home n’est pas conforme : " + string.Join(" ; ", failures)
                + ". Applique le manifeste Home avec schema-admin puis réessaie.");

        return new HomeSchemaRuntime(snapshot);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildRequiredByType()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Types.Room] = [Properties.Label, Properties.Notes],
            [Types.Circuit] = [Properties.Label, Properties.Notes],
            [Types.DistributionBoard] = [Properties.Label, Properties.Notes],
        };

        string[] elementProperties =
        [
            Properties.Label, Properties.Room, Properties.Category,
            Properties.Existence, Properties.Condition, Properties.Circuit,
            Properties.ObservedOn, Properties.Notes,
        ];
        foreach (string type in ElementTypes) result[type] = elementProperties;

        result[Types.Worksite] =
        [
            Properties.Status, Properties.Concerns, Properties.TargetDate,
            Properties.Notes, Properties.Documents,
        ];
        result[Types.Task] =
        [
            Properties.Status, Properties.Concerns, Properties.Worksite,
            Properties.TargetDate, Properties.Notes,
        ];
        result[Types.Idea] = [Properties.Horizon];
        result[Types.Errand] =
        [Properties.Aisle, Properties.Quantity, Properties.Concerns, Properties.Notes];
        result[Types.Tool] =
        [
            Properties.ModelReference, Properties.ToolCategory, Properties.Supplier,
            Properties.Invoice, Properties.StoredIn, Properties.Documents, Properties.Notes,
        ];
        return result;
    }

    private static bool LinkMatches(SchemaPropertyLinkInfo link, SchemaPropertyInfo property) =>
        (link.Key.Length > 0 && string.Equals(link.Key, property.Key, StringComparison.Ordinal))
        || (link.Id.Length > 0 && string.Equals(link.Id, property.Id, StringComparison.Ordinal));

    private static string PropertyName(string key) => key switch
    {
        Properties.Label => "Libellé",
        Properties.Room => "Pièce",
        Properties.Category => "Catégorie",
        Properties.Existence => "Existence",
        Properties.Condition => "État",
        Properties.Circuit => "Circuit",
        Properties.ObservedOn => "Date de relevé",
        Properties.Notes => "Notes",
        Properties.Documents => "Documents",
        Properties.Horizon => "Horizon",
        Properties.Aisle => "Rayon",
        Properties.Quantity => "Quantité",
        Properties.Concerns => "Concerne",
        Properties.ModelReference => "Référence modèle",
        Properties.ToolCategory => "Catégorie d'outil",
        Properties.Supplier => "Fournisseur",
        Properties.Invoice => "Facture",
        Properties.StoredIn => "Rangé dans",
        Properties.Status => "Statut",
        Properties.TargetDate => "Date cible",
        Properties.Worksite => "Chantier",
        _ => key,
    };

    private static string TypeName(string key) => key switch
    {
        Types.Room => "Pièce", Types.Circuit => "Circuit", Types.DistributionBoard => "Tableau",
        Types.Outlet => "Prise", Types.Lighting => "Éclairage", Types.Control => "Commande",
        Types.Opening => "Ouvrant", Types.Appliance => "Appareil", Types.Network => "Réseau",
        Types.Sensor => "Capteur", Types.Relay => "Relais", Types.Panel => "Panneau",
        Types.Node => "Nœud", Types.Idea => "Idée", Types.Errand => "Course",
        Types.Tool => "Outil", Types.Worksite => "Chantier", Types.Task => "Tâche",
        _ => key,
    };

    // "Matériel" is the deliberate plural label of Outil: the fleet, not "Outils".
    private static string TypePluralName(string key) => key switch
    {
        Types.Room => "Pièces", Types.Circuit => "Circuits", Types.DistributionBoard => "Tableaux",
        Types.Outlet => "Prises", Types.Lighting => "Éclairages", Types.Control => "Commandes",
        Types.Opening => "Ouvrants", Types.Appliance => "Appareils", Types.Network => "Réseaux",
        Types.Sensor => "Capteurs", Types.Relay => "Relais", Types.Panel => "Panneaux",
        Types.Node => "Nœuds", Types.Idea => "Idées", Types.Errand => "Courses",
        Types.Tool => "Matériel", Types.Worksite => "Chantiers", Types.Task => "Tâches",
        _ => key,
    };

    private static string TypeLayout(string key) => key switch
    {
        Types.Idea => "note",
        Types.Errand => "action",
        Types.Task => "action",
        _ => "basic",
    };
}

public sealed class HomeSchemaException(string message) : InvalidOperationException(message);
