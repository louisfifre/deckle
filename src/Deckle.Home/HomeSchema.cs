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
    }

    public static class Existence
    {
        public const string Existing = "existant";
        public const string Planned = "prevu";
        public const string Removed = "depose";
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

    public static readonly IReadOnlyList<string> CreatableTypes =
    [Types.Room, Types.Circuit, Types.DistributionBoard, .. ElementTypes];

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
                ["layout"] = "basic",
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
        _ => key,
    };

    private static string TypeName(string key) => key switch
    {
        Types.Room => "Pièce", Types.Circuit => "Circuit", Types.DistributionBoard => "Tableau",
        Types.Outlet => "Prise", Types.Lighting => "Éclairage", Types.Control => "Commande",
        Types.Opening => "Ouvrant", Types.Appliance => "Appareil", Types.Network => "Réseau",
        Types.Sensor => "Capteur", Types.Relay => "Relais", Types.Panel => "Panneau",
        Types.Node => "Nœud", _ => key,
    };

    private static string TypePluralName(string key) => key switch
    {
        Types.Room => "Pièces", Types.Circuit => "Circuits", Types.DistributionBoard => "Tableaux",
        Types.Outlet => "Prises", Types.Lighting => "Éclairages", Types.Control => "Commandes",
        Types.Opening => "Ouvrants", Types.Appliance => "Appareils", Types.Network => "Réseaux",
        Types.Sensor => "Capteurs", Types.Relay => "Relais", Types.Panel => "Panneaux",
        Types.Node => "Nœuds", _ => key,
    };
}

public sealed class HomeSchemaException(string message) : InvalidOperationException(message);
