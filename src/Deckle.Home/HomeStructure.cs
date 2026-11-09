using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Deckle.Home;

// What the source space holds beyond its objects, read as the source speaks it
// (old type keys, old property keys, old ids): the settings and views of each
// Home type, the views and items of each section collection, each template whole, the
// widgets in order. It rides in the replay sheet under `structure`, so the
// export, the replay and the structure write can each be run from the file.
//
// Nothing here is remapped: the remap is a pure step applied when the
// structure is written (HomeStructureRemap), because it needs the ids the
// replay created. A sheet written before the structure existed has no
// `structure` and a version below StructureVersion: it loads and simply
// carries nothing to recreate.
internal sealed class HomeStructure
{
    [JsonPropertyName("types")]
    public List<HomeStructureType> Types { get; set; } = [];

    [JsonPropertyName("collections")]
    public List<HomeStructureCollection> Collections { get; set; } = [];

    [JsonPropertyName("templates")]
    public List<HomeStructureTemplate> Templates { get; set; } = [];

    [JsonPropertyName("widgets")]
    public List<HomeStructureWidget> Widgets { get; set; } = [];

    // Counts only, by name: what was exported, what was skipped, what could
    // not be read. Safe to print.
    [JsonPropertyName("report")]
    public SortedDictionary<string, int> Report { get; set; } = new(StringComparer.Ordinal);

    internal void Count(string key, int by = 1)
    {
        if (by <= 0) return;
        Report[key] = Report.TryGetValue(key, out int value) ? value + by : by;
    }
}

// A Home type of the source: its settings and its own views. `Key` is the
// source key (errand, floor), `TypeId` the source's object id of the type, the
// one a widget points at.
internal sealed class HomeStructureType
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("type_id")]
    public string TypeId { get; set; } = "";

    [JsonPropertyName("default_view")]
    public string? DefaultView { get; set; }

    // The source id of the template new objects start from.
    [JsonPropertyName("default_template")]
    public string? DefaultTemplate { get; set; }

    // Every property the type lists, with its section when it has one.
    [JsonPropertyName("properties")]
    public List<HomeStructureProperty> Properties { get; set; } = [];

    // The views of the type's dataview, as the source serves them.
    [JsonPropertyName("views")]
    public JsonArray Views { get; set; } = [];
}

internal sealed class HomeStructureProperty
{
    [JsonPropertyName("property")]
    public string Property { get; set; } = "";

    // "featured", "hidden" or absent.
    [JsonPropertyName("section")]
    public string? Section { get; set; }
}

// A collection of the source that is not a Home object (the section
// collections). The replay does not create it, so the writer finds it again on
// the target by its name, or creates it, and fills it with its items.
internal sealed class HomeStructureCollection
{
    [JsonPropertyName("old_id")]
    public string OldId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // The source ids of the members, in the order the source stores them. A
    // sheet exported before the items were carried has none.
    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];

    // The icon as the v2 object read serves it, kept as served; null when the
    // read gave none.
    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Icon { get; set; }

    [JsonPropertyName("views")]
    public JsonArray Views { get; set; } = [];
}

// A template whole, the AnyBlock document the source serves for it.
internal sealed class HomeStructureTemplate
{
    [JsonPropertyName("old_id")]
    public string OldId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // The source key of the type it is for.
    [JsonPropertyName("template_for")]
    public string TemplateFor { get; set; } = "";

    [JsonPropertyName("document")]
    public JsonObject Document { get; set; } = [];
}

internal sealed class HomeStructureWidget
{
    [JsonPropertyName("old_id")]
    public string OldId { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("view_id")]
    public string? ViewId { get; set; }
}
