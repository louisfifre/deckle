using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The Home contract of 2026-08-10, revised at the 2026-08-12 reboot and the
// 2026-08-23 electricity grill (nomenclature v3): 14 types
// in five families, English keys, human titles, the derived identity code in
// the `code` property. The applied truth lives in the home project's
// mcp/schema-manifest.json; this class is its compiled mirror for validation
// and payload building — a required SUBSET: conformity tolerates surplus, so
// manifest-only properties (needed, errand_category…) write through the live
// schema without appearing here. French labels live in Terms/terms.fr.json
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
        public const string StoredIn = "stored_in";
        public const string Floor = "floor";
        public const string Category = "category";
        public const string Condition = "condition";
        public const string Circuit = "circuit";
        public const string Panel = "panel";
        public const string OutletCount = "outlet_count";
        public const string Earthed = "earthed";
        public const string LightNature = "light_nature";
        public const string SwitchKind = "switch_kind";
        public const string Controls = "controls";
        public const string ControlledBy = "controlled_by";
        public const string ControlLink = "control_link";
        public const string PowerWatts = "power_watts";
        public const string MeasuredQuantity = "measured_quantity";
        public const string PowerSupply = "power_supply";
        public const string Nature = "nature";
        public const string PoweredBy = "powered_by";
        public const string Rating = "rating";
        public const string Cable = "cable";
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
        public const string BatteryCapacity = "battery_capacity";
        public const string StorageCapacity = "storage_capacity";
        public const string PowerRms = "power_rms";
        public const string Impedance = "impedance";
        public const string Os = "os";
        public const string Weight = "weight";
        public const string PlantFamily = "plant_family";
        public const string PlantGenus = "plant_genus";
        public const string ScientificName = "scientific_name";
        public const string Substrate = "substrate";
        public const string PlantExposure = "plant_exposure";
        public const string PlantPhoto = "plant_photo";
        public const string Horizon = "horizon";
        public const string Aisle = "aisle";
        public const string About = "about";
        public const string State = "state";
        public const string TargetDate = "target_date";
        public const string Worksite = "worksite";
    }

    public static class State
    {
        public const string Open = "ouvert";
        public const string InProgress = "en_cours";
        public const string Waiting = "en_attente";
        public const string Dormant = "dormant";
        public const string Done = "termine";
        public const string Abandoned = "abandonne";
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

    internal static readonly IReadOnlyDictionary<string, string> RequiredProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Properties.Code] = "text",
            [Properties.Notes] = "text",
            [Properties.Documents] = "files",
            [Properties.Location] = "text",
            [Properties.InstalledIn] = "objects",
            [Properties.StoredIn] = "objects",
            [Properties.Floor] = "objects",
            [Properties.Category] = "select",
            [Properties.Condition] = "select",
            [Properties.Circuit] = "objects",
            [Properties.Panel] = "objects",
            [Properties.OutletCount] = "number",
            [Properties.Earthed] = "checkbox",
            [Properties.LightNature] = "select",
            [Properties.SwitchKind] = "select",
            [Properties.Controls] = "objects",
            [Properties.ControlledBy] = "objects",
            [Properties.ControlLink] = "select",
            [Properties.PowerWatts] = "number",
            [Properties.MeasuredQuantity] = "multi_select",
            [Properties.PowerSupply] = "select",
            [Properties.Nature] = "select",
            [Properties.PoweredBy] = "objects",
            [Properties.Rating] = "select",
            [Properties.Cable] = "select",
            [Properties.OriginLabel] = "text",
            [Properties.PanelPosition] = "text",
            [Properties.DedicatedRcd] = "text",
            [Properties.RcdHead] = "text",
            [Properties.RcdType] = "select",
            [Properties.FreeSlots] = "number",
            [Properties.Conduits] = "text",
            [Properties.SubMeter] = "text",
            [Properties.Domain] = "select",
            [Properties.EquipmentCategory] = "select",
            [Properties.Manufacturer] = "select",
            [Properties.Supplier] = "select",
            [Properties.ModelRef] = "text",
            [Properties.SerialNumber] = "text",
            [Properties.PurchasePrice] = "number",
            [Properties.PurchaseDate] = "date",
            [Properties.Receipt] = "files",
            [Properties.PartOf] = "objects",
            [Properties.Quantity] = "number",
            [Properties.BatteryCapacity] = "number",
            [Properties.StorageCapacity] = "number",
            [Properties.PowerRms] = "number",
            [Properties.Impedance] = "number",
            [Properties.Os] = "select",
            [Properties.Weight] = "number",
            [Properties.PlantFamily] = "select",
            [Properties.PlantGenus] = "select",
            [Properties.ScientificName] = "text",
            [Properties.Substrate] = "multi_select",
            [Properties.PlantExposure] = "select",
            [Properties.PlantPhoto] = "files",
            [Properties.Horizon] = "select",
            [Properties.Aisle] = "select",
            [Properties.About] = "objects",
            [Properties.State] = "select",
            [Properties.TargetDate] = "date",
            [Properties.Worksite] = "objects",
        };

    // Objects properties whose target must carry a specific type; unlisted
    // properties (about, stored_in for containers to come) accept any Home
    // object. The floor property is special-cased in the writer: its targets
    // are the app-created collection objects of the runtime floor type.
    // powered_by (2026-08-23): the departure of a circuit that has no panel —
    // the driver point feeding a 24 V LED circuit.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectPropertyTargets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Properties.InstalledIn] = [Types.Room],
            [Properties.StoredIn] = [Types.Room],
            [Properties.Circuit] = [Types.Circuit],
            [Properties.Panel] = [Types.Panel],
            [Properties.PartOf] = [Types.System],
            [Properties.Worksite] = [Types.Worksite],
            [Properties.Controls] = [Types.Point],
            [Properties.ControlledBy] = [Types.Point],
            [Properties.PoweredBy] = [Types.Point],
        };

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Types.Room] =
            [Properties.Code, Properties.Floor, Properties.Notes, Properties.Documents],
            [Types.Point] =
            [
                Properties.Code, Properties.Category, Properties.InstalledIn,
                Properties.Condition, Properties.Controls, Properties.ControlledBy,
                Properties.Circuit, Properties.Panel, Properties.OutletCount,
                Properties.Earthed, Properties.LightNature, Properties.PowerWatts,
                Properties.SwitchKind, Properties.ControlLink, Properties.PowerSupply,
                Properties.MeasuredQuantity, Properties.Location, Properties.Notes,
            ],
            [Types.Circuit] =
            [
                Properties.Code, Properties.Nature, Properties.Panel, Properties.PoweredBy,
                Properties.Rating, Properties.DedicatedRcd, Properties.PanelPosition,
                Properties.OriginLabel, Properties.Cable, Properties.Notes,
            ],
            [Types.Panel] =
            [
                Properties.Code, Properties.InstalledIn, Properties.RcdHead, Properties.RcdType,
                Properties.FreeSlots, Properties.Conduits, Properties.SubMeter,
                Properties.Notes, Properties.Documents,
            ],
            [Types.System] =
            [
                Properties.Domain, Properties.EquipmentCategory, Properties.Manufacturer,
                Properties.InstalledIn, Properties.StoredIn, Properties.Notes,
                Properties.Documents,
            ],
            [Types.Device] =
            [
                Properties.Domain, Properties.EquipmentCategory, Properties.Manufacturer,
                Properties.Supplier, Properties.ModelRef, Properties.SerialNumber,
                Properties.PurchasePrice, Properties.PurchaseDate, Properties.Receipt,
                Properties.PartOf, Properties.StoredIn, Properties.InstalledIn,
                Properties.Quantity, Properties.BatteryCapacity,
                Properties.StorageCapacity, Properties.PowerRms, Properties.Impedance,
                Properties.Os, Properties.Weight, Properties.Documents, Properties.Notes,
            ],
            [Types.Component] =
            [
                Properties.Domain, Properties.EquipmentCategory, Properties.Manufacturer,
                Properties.Supplier, Properties.ModelRef, Properties.SerialNumber,
                Properties.PurchasePrice, Properties.PurchaseDate, Properties.Receipt,
                Properties.PartOf, Properties.StoredIn, Properties.Quantity,
                Properties.BatteryCapacity,
                Properties.StorageCapacity, Properties.PowerRms, Properties.Impedance,
                Properties.Weight, Properties.Documents, Properties.Notes,
            ],
            [Types.Utensil] =
            [
                Properties.EquipmentCategory, Properties.Manufacturer, Properties.Supplier,
                Properties.ModelRef, Properties.PurchasePrice, Properties.PurchaseDate,
                Properties.Receipt, Properties.PartOf, Properties.StoredIn,
                Properties.Quantity, Properties.Documents, Properties.Notes,
            ],
            [Types.Plant] =
            [
                Properties.PlantFamily, Properties.PlantGenus, Properties.ScientificName,
                Properties.Substrate, Properties.PlantExposure,
                Properties.PlantPhoto, Properties.InstalledIn, Properties.Notes,
            ],
            [Types.Idea] = [Properties.Horizon],
            [Types.Errand] =
            [Properties.Aisle, Properties.Quantity, Properties.About, Properties.Notes],
            [Types.Worksite] =
            [
                Properties.State, Properties.About, Properties.TargetDate,
                Properties.Notes, Properties.Documents,
            ],
            [Types.Todo] =
            [
                Properties.State, Properties.About, Properties.Worksite,
                Properties.TargetDate, Properties.Notes,
            ],
        };

    // Closed vocabularies: options are applied, never invented. Open selects
    // (equipment_category, manufacturer, supplier, os, plant_*, substrate) are
    // absent here on purpose — their options grow from Louis in the app and
    // resolve against the live space only. Supplier left the closed set at the
    // 2026-08-12 reboot: real purchases (Decathlon, LDLC, Rakuten…) outgrew
    // the compiled six. The electricity selects born on 2026-08-23 (nature,
    // rating, cable, control_link, measured_quantity) are open too: the
    // manifest seeds them, Louis extends them in the app (« on pré-remplit,
    // mais on peut ajouter toujours »), and a compiled list would lock the
    // guichet the day he renames or drops an option. Category stays closed:
    // it is the code grammar.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedVocabularies =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Properties.Category] =
            ["p", "ps", "pj", "pf", "l", "lr", "c", "v", "a", "ds", "dr", "dx", "de"],
            [Properties.Condition] = ["bon", "vetuste", "endommage", "hors_service"],
            [Properties.LightNature] = ["plafonnier", "applique", "spot", "dcl"],
            [Properties.SwitchKind] = ["interrupteur_simple", "va_et_vient", "poussoir"],
            [Properties.PowerSupply] = ["poe", "pile", "cinq_volts", "vingt_quatre_volts", "secteur"],
            [Properties.RcdType] = ["type_a", "type_ac", "inconnu"],
            [Properties.Domain] =
            [
                "audio", "informatique", "electronique", "outillage",
                "cuisine", "electromenager", "jardin",
            ],
            [Properties.Horizon] = ["maintenant", "bientot", "un_jour", "peut_etre"],
            [Properties.Aisle] = ["alimentaire", "bricolage", "maison", "jardin", "autre"],
            [Properties.State] =
            [
                State.Open, State.InProgress, State.Waiting,
                State.Dormant, State.Done, State.Abandoned,
            ],
        };

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
            if (ClosedVocabularies.TryGetValue(key, out IReadOnlyList<string>? optionKeys))
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
                string name = OptionLabel(propertyKey, key);
                if (!tags.Values.Distinct().Any(tag =>
                        string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"vocabulaire {propertyKey} : option manquante {key}");
                }
            }
        }

        if (failures.Count > 0)
            throw new HomeSchemaException(
                "Le schéma Home n’est pas conforme : " + string.Join(" ; ", failures)
                + ". Applique le manifeste Home avec schema-admin puis réessaie.");

        return new HomeSchemaRuntime(snapshot, FloorTypeKey(snapshot));
    }

    // The floor type is created in the app (the API refuses collection
    // layouts), so its key is a live discovery, not a compiled constant: the
    // nominal key first, else the collection-layout type named Zone. Absent
    // type = floor features refuse with guidance instead of failing the
    // whole schema closed.
    internal static string? FloorTypeKey(SchemaSnapshot snapshot)
    {
        if (snapshot.Types.ContainsKey(Types.Floor)) return Types.Floor;
        return snapshot.Types.Values.FirstOrDefault(type =>
                string.Equals(type.Layout, "collection", StringComparison.Ordinal)
                && type.Name is "Zone" or "Zones")
            ?.Key;
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
