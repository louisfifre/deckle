using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The canonical Home contract is embedded from Schema/schema-manifest.json,
// and the space speaks its stable English keys as they are: the key a type or
// property carries in the manifest is the key the live space carries. Required
// membership, formats, layouts, relations and seed tags come from that
// snapshot. French labels live in Terms/terms.fr.json (HomeTerms), never in
// code.
public static class HomeSchema
{
    public static class Types
    {
        // An ordinary basic object since 2026-11-09: a Room points at its
        // Zone, the Zone carries no property of its own.
        public const string Zone = "zone";
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
        public const string Product = "product";
        public const string Worksite = "worksite";
        public const string Todo = "todo";
    }

    public static class Properties
    {
        public const string Code = "code";
        public const string Manual = "manual";
        public const string Location = "location";
        public const string InstalledIn = "installed_in";
        public const string ConnectedTo = "connected_to";
        public const string StoredIn = "stored_in";
        public const string Zone = "zone";
        public const string Category = "category";
        public const string Circuit = "circuit";
        public const string Panel = "panel";
        public const string UpstreamPanel = "upstream_panel";
        public const string OutletCount = "outlet_count";
        public const string Earthed = "earthed";
        public const string Dcl = "dcl";
        public const string SwitchNature = "switch_nature";
        public const string Controls = "controls";
        public const string ControlledBy = "controlled_by";
        public const string ControlLink = "control_link";
        public const string MeasuredQuantity = "measured_quantity";
        public const string PowerSupply = "power_supply";
        public const string Carries = "carries";
        public const string Assignment = "assignment";
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
        public const string MemoryFrequency = "memory_frequency";
        public const string MemorySize = "memory_size";
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
        public const string Archived = "archived";
        public const string ProductCategory = "product_category";
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
    // property. A point's nature is the frozen `category` select, derived
    // from its code.
    public static readonly IReadOnlyList<string> CodedTypes =
    [Types.Room, Types.Point, Types.Circuit, Types.Panel];

    // Equipment family: a system aggregates, a device stands alone and may
    // join a system through part_of, a component exists only through its
    // mandatory part_of, a utensil holds kitchen gear and may join a system
    // like a device. The component gate lives in the gestures because the
    // schema cannot express it.
    public static readonly IReadOnlyList<string> EquipmentTypes =
    [Types.System, Types.Device, Types.Component, Types.Utensil];

    public static readonly IReadOnlyList<string> LifeTypes =
    [Types.Plant, Types.Idea, Types.Product];

    public static readonly IReadOnlyList<string> WorkTypes =
    [Types.Worksite, Types.Todo];

    // Zone leads, as in the manifest: it is free-titled, so it stays out of
    // CodedTypes while Room, its neighbour in the Structure section, keeps
    // its code.
    public static readonly IReadOnlyList<string> CreatableTypes =
    [Types.Zone, .. CodedTypes, .. EquipmentTypes, .. LifeTypes, .. WorkTypes];

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

    internal static bool IsAllowedProperty(string typeKey, string propertyKey) =>
        TargetRequiredByType.TryGetValue(typeKey, out IReadOnlyList<string>? allowed)
        && allowed.Contains(propertyKey, StringComparer.Ordinal);

    internal static IReadOnlyList<string> OptionAliases(string propertyKey, string optionKey)
    {
        JsonObject? definition = HomeSchemaTargetData.Manifest["properties"]!.AsArray().OfType<JsonObject>()
            .FirstOrDefault(property => property["key"]!.GetValue<string>() == propertyKey);
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

    private const int MaxReportedSchemaFailures = 6;

    // Tolerates surplus but fails on any compiled required property or closed
    // option the owner deleted in the app, which locks the whole surface.
    // Compile as required only what the surface cannot work without. HomeTerms
    // also throws for a required key that has no term.
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

        // The message travels to a model as a tool error, and a fresh or empty
        // space can fail thousands of checks. The first failures tell the model
        // what to repair; the remaining count tells it the size of the gap.
        if (failures.Count > 0)
        {
            string listed = string.Join(" ; ", failures.Take(MaxReportedSchemaFailures));
            int remaining = failures.Count - MaxReportedSchemaFailures;
            if (remaining > 0)
                listed += $" ; … et {remaining} autres";
            throw new HomeSchemaException(
                "Le schéma Home n’est pas conforme : " + listed
                + ". Compare le snapshot au manifeste canonique, préserve les anciennes coordonnées et migre explicitement les formats et valeurs avant de réessayer.")
            {
                FailedChecks = failures.Count,
            };
        }

        return new HomeSchemaRuntime(snapshot);
    }

    private static bool LinkMatches(SchemaPropertyLinkInfo link, SchemaPropertyInfo property) =>
        (link.Key.Length > 0 && string.Equals(link.Key, property.Key, StringComparison.Ordinal))
        || (link.Id.Length > 0 && string.Equals(link.Id, property.Id, StringComparison.Ordinal));

    private static string TypeLayout(string key) => HomeSchemaTargetData.Layouts[key];
}

// Reason is one snake_case word for the trace; the message is the French
// guidance shown to the person or the model and names canonical keys only.
public sealed class HomeSchemaException(string message, string reason = "schema_nonconformant")
    : InvalidOperationException(message)
{
    public string Reason { get; } = reason;

    // How many schema checks failed, when the exception reports a validation;
    // the trace carries the count, never the message.
    public int FailedChecks { get; init; }
}
