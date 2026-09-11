using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The canonical Home contract is embedded from Schema/schema-manifest.json;
// this class projects its stable English keys onto the historical wire keys
// used by the live space. Required membership, formats, relations, and seed
// tags come from that snapshot. French labels live in Terms/terms.fr.json
// (HomeTerms), never in code.
public static class HomeSchema
{
    public static class Types
    {
        // App-managed: the collection layout cannot be created through the
        // Anytype API, so the type is born in the app and its real key is
        // discovered from the live snapshot (see FloorTypeKey).
        public const string Floor = "floor";
        public const string Room = "room";
        public const string Point = "point";
        public const string Circuit = "circuit";
        public const string Panel = "panel";
        public const string System = "system";
        public const string Device = "device";
        public const string Component = "component";
        public const string Utensil = "utensil";
        public const string Plant = "plant";
        public const string Idea = "idea";
        public const string Errand = "errand";
        public const string Worksite = "worksite";
        public const string Todo = "todo";
    }

    public static class Properties
    {
        public const string Code = "code";
        public const string Notes = "notes";
        public const string Documents = "documents";
        public const string Location = "location";
        public const string InstalledIn = "installed_in";
        public const string ConnectedTo = "connected_to";
        public const string StoredIn = "stored_in";
        public const string Floor = "floor";
        public const string Category = "category";
        public const string Condition = "condition";
        public const string Circuit = "circuit";
        public const string Panel = "panel";
        public const string UpstreamPanel = "upstream_panel";
        public const string OutletCount = "outlet_count";
        public const string Earthed = "earthed";
        public const string Dcl = "dcl";
        public const string LightNature = "light_nature";
        public const string SwitchKind = "switch_kind";
        public const string Controls = "controls";
        public const string ControlledBy = "controlled_by";
        public const string ControlLink = "control_link";
        public const string PowerWatts = "power_watts";
        public const string MeasuredQuantity = "measured_quantity";
        public const string PowerSupply = "power_supply";
        public const string Carries = "carries";
        public const string Assignment = "assignment";
        public const string Nature = "nature";
        public const string PoweredBy = "powered_by";
        public const string Rating = "rating";
        public const string Cable = "cable";
        public const string Pipe = "pipe";
        public const string Poe = "poe";
        public const string OriginLabel = "origin_label";
        public const string PanelPosition = "panel_position";
        public const string DedicatedRcd = "dedicated_rcd";
        public const string RcdHead = "rcd_head";
        public const string RcdType = "rcd_type";
        public const string FreeSlots = "free_slots";
        public const string Conduits = "conduits";
        public const string SubMeter = "sub_meter";
        public const string Domain = "domain";
        public const string EquipmentCategory = "equipment_category";
        public const string Manufacturer = "manufacturer";
        public const string Supplier = "supplier";
        public const string ModelRef = "model_ref";
        public const string SerialNumber = "serial_number";
        public const string PurchasePrice = "purchase_price";
        public const string PurchaseDate = "purchase_date";
        public const string Receipt = "receipt";
        public const string PartOf = "part_of";
        public const string Quantity = "quantity";
        public const string RatedInputPower = "rated_input_power";
        public const string RatedOutputPower = "rated_output_power";
        public const string BulbBase = "bulb_base";
        public const string LightingCapabilities = "lighting_capabilities";
        public const string ColorTemperatureMin = "color_temperature_min";
        public const string ColorTemperatureMax = "color_temperature_max";
        public const string LuminousFlux = "luminous_flux";
        public const string LuminousFluxReferenceTemperature = "luminous_flux_reference_temperature";
        public const string SupportedProtocols = "supported_protocols";
        public const string BatteryCapacity = "battery_capacity";
        public const string StorageCapacity = "storage_capacity";
        public const string PowerRms = "power_rms";
        public const string Impedance = "impedance";
        public const string Os = "os";
        public const string Weight = "weight";
        public const string Socket = "socket";
        public const string Chipset = "chipset";
        public const string MemoryType = "memory_type";
        public const string MemoryFrequency = "memory_frequency_mhz";
        public const string MemorySize = "memory_size_gb";
        public const string CoreCount = "core_count";
        public const string ThreadCount = "thread_count";
        public const string TechnicalInterface = "technical_interface";
        public const string StorageMedium = "storage_medium";
        public const string PlantFamily = "plant_family";
        public const string PlantGenus = "plant_genus";
        public const string ScientificName = "scientific_name";
        public const string Substrate = "substrate";
        public const string PlantExposure = "plant_exposure";
        public const string PlantPhoto = "plant_photo";
        public const string Environment = "environment";
        public const string AcquisitionDate = "acquisition_date";
        public const string LastRepotting = "last_repotting";
        public const string Horizon = "horizon";
        public const string Aisle = "aisle";
        public const string ProductCategory = "product_category";
        public const string ErrandCategory = "errand_category";
        public const string About = "about";
        public const string State = "state";
        public const string TargetDate = "target_date";
        public const string Worksite = "worksite";
        public const string Priority = "priority";
        public const string DependsOn = "depends_on";
        public const string EstimatedBudget = "estimated_budget";
        public const string ActualBudget = "actual_budget";
        public const string EstimatedEffort = "estimated_effort";
        public const string ActualEffort = "actual_effort";
        public const string DefinitionOfDone = "definition_of_done";
    }

    public static class State
    {
        public const string Open = "open";
        public const string InProgress = "in_progress";
        public const string Waiting = "waiting";
        public const string Dormant = "dormant";
        public const string Done = "done";
        public const string Abandoned = "abandoned";
    }

    // Inventory types whose identity is an immutable code in the `code`
    // property. The point type absorbs the ten former wall-point types; its
    // nature is the frozen `category` select, derived from the code.
    public static readonly IReadOnlyList<string> CodedTypes =
    [Types.Room, Types.Point, Types.Circuit, Types.Panel];

    // Equipment family: a Système aggregates, an Appareil stands alone, a
    // Composant only exists through its mandatory part_of, an Ustensile de
    // cuisine (2026-08-12 grill) holds kitchen gear and may join a Système
    // like an Appareil. The composant gate lives in the gestures, not here —
    // schema cannot express it.
    public static readonly IReadOnlyList<string> EquipmentTypes =
    [Types.System, Types.Device, Types.Component, Types.Utensil];

    public static readonly IReadOnlyList<string> LifeTypes =
    [Types.Plant, Types.Idea, Types.Errand];

    public static readonly IReadOnlyList<string> WorkTypes =
    [Types.Worksite, Types.Todo];

    public static readonly IReadOnlyList<string> CreatableTypes =
    [.. CodedTypes, .. EquipmentTypes, .. LifeTypes, .. WorkTypes];

    internal static readonly IReadOnlyDictionary<string, string> TargetRequiredProperties =
        HomeSchemaTargetData.RequiredProperties;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TargetRequiredByType =
        HomeSchemaTargetData.RequiredByType;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TargetObjectPropertyTargets =
        HomeSchemaTargetData.ObjectPropertyTargets;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TargetClosedVocabularies =
        HomeSchemaTargetData.ClosedVocabularies;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TargetSeededVocabularies =
        HomeSchemaTargetData.SeededVocabularies;

    internal static readonly IReadOnlyDictionary<string, string> RequiredProperties = TargetRequiredProperties;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType = TargetRequiredByType;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectPropertyTargets = TargetObjectPropertyTargets;
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedVocabularies = TargetClosedVocabularies;

    public static JsonObject TargetManifest => HomeSchemaTargetData.CloneManifest();

    public static string WireTypeKey(string canonicalType) => canonicalType switch
    {
        "product" => Types.Errand,
        "zone" => Types.Floor,
        _ => canonicalType,
    };

    public static string WirePropertyKey(string canonicalProperty) => canonicalProperty switch
    {
        "zone" => Properties.Floor,
        "manual" => Properties.Documents,
        "product_category" => Properties.ErrandCategory,
        "memory_size" => Properties.MemorySize,
        "memory_frequency" => Properties.MemoryFrequency,
        "switch_nature" => Properties.SwitchKind,
        _ => canonicalProperty,
    };

    internal static bool IsAllowedProperty(string typeKey, string propertyKey) =>
        string.Equals(WireTypeKey(typeKey), Types.Floor, StringComparison.Ordinal)
            ? HomeSchemaTargetData.ZoneProperties.Contains(WirePropertyKey(propertyKey), StringComparer.Ordinal)
            : TargetRequiredByType.TryGetValue(WireTypeKey(typeKey), out IReadOnlyList<string>? allowed)
                && allowed.Contains(WirePropertyKey(propertyKey), StringComparer.Ordinal);

    internal static IReadOnlyList<string> OptionAliases(string propertyKey, string optionKey)
    {
        JsonObject? definition = HomeSchemaTargetData.Manifest["properties"]!.AsArray().OfType<JsonObject>()
            .FirstOrDefault(property => WirePropertyKey(property["key"]!.GetValue<string>()) == propertyKey);
        JsonObject? option = (definition?["tags"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(tag => tag["key"]!.GetValue<string>() == optionKey);
        return new[] { optionKey, OptionLabel(propertyKey, optionKey), option?["name"]?.GetValue<string>() ?? optionKey }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string OptionLabel(string propertyKey, string optionKey) =>
        HomeTerms.Current.OptionName(propertyKey, optionKey);

    internal static IReadOnlyList<string> OptionLabels(string propertyKey) =>
        ClosedVocabularies.TryGetValue(propertyKey, out IReadOnlyList<string>? keys)
            ? keys.Select(key => OptionLabel(propertyKey, key)).ToArray()
            : [];

    internal static JsonObject CreateRequiredSchemaManifest()
    {
        HomeTerms terms = HomeTerms.Current;
        var properties = new JsonArray();
        foreach ((string key, string format) in RequiredProperties)
        {
            var property = new JsonObject
            {
                ["key"] = key,
                ["name"] = terms.PropertyName(key),
                ["format"] = format,
            };
            if (TargetSeededVocabularies.TryGetValue(key, out IReadOnlyList<string>? optionKeys))
            {
                var tags = new JsonArray();
                foreach (string optionKey in optionKeys)
                    tags.Add(new JsonObject
                    {
                        ["key"] = optionKey,
                        ["name"] = terms.OptionName(key, optionKey),
                    });
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
                ["name"] = terms.TypeName(type),
                ["plural_name"] = terms.TypePluralName(type),
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

        foreach ((string propertyKey, IReadOnlyList<string> optionKeys) in ClosedVocabularies)
        {
            snapshot.TagsByProperty.TryGetValue(propertyKey, out var tags);
            tags ??= new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
            foreach (string key in optionKeys)
            {
                IReadOnlyList<string> names = OptionAliases(propertyKey, key);
                if (!tags.Values.Distinct().Any(tag =>
                        names.Contains(tag.Key, StringComparer.OrdinalIgnoreCase)
                        || names.Contains(tag.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    failures.Add($"vocabulaire {propertyKey} : option manquante {key}");
                }
            }
        }

        if (failures.Count > 0)
            throw new HomeSchemaException(
                "Le schéma Home n’est pas conforme : " + string.Join(" ; ", failures)
                + ". Compare le snapshot au manifeste canonique, préserve les anciennes coordonnées et migre explicitement les formats et valeurs avant de réessayer.");

        return new HomeSchemaRuntime(snapshot, FloorTypeKey(snapshot));
    }

    // The floor type is created in the app (the API refuses collection
    // layouts), so its key is a live discovery, not a compiled constant: the
    // nominal key first, else the collection-layout type named Zone. Absent
    // type = floor features refuse with guidance instead of failing the
    // whole schema closed.
    internal static string? FloorTypeKey(SchemaSnapshot snapshot)
    {
        if (snapshot.Types.TryGetValue(Types.Floor, out SchemaTypeInfo? nominal))
        {
            if (nominal.Layout != "collection")
                throw new HomeSchemaException("Le type Zone attendu doit avoir un layout collection.");
            return Types.Floor;
        }
        SchemaTypeInfo[] matches = snapshot.Types.Values.Where(type =>
                string.Equals(type.Layout, "collection", StringComparison.Ordinal)
                && type.Name is "Zone" or "Zones").ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Key,
            _ => throw new HomeSchemaException("Plusieurs types Zone correspondent : précise le schéma avant d'écrire."),
        };
    }

    private static bool LinkMatches(SchemaPropertyLinkInfo link, SchemaPropertyInfo property) =>
        (link.Key.Length > 0 && string.Equals(link.Key, property.Key, StringComparison.Ordinal))
        || (link.Id.Length > 0 && string.Equals(link.Id, property.Id, StringComparison.Ordinal));

    private static string TypeLayout(string key) => key switch
    {
        Types.Idea => "note",
        Types.Errand => "action",
        Types.Worksite => "action",
        Types.Todo => "action",
        _ => "basic",
    };
}

public sealed class HomeSchemaException(string message) : InvalidOperationException(message);
