using System.Globalization;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Reads typed values out of the `properties` object of a v2 read. Shapes
// measured live on 2026-10-22 (a task, a report and a project):
//
//   • `properties` is an object keyed by property key, not an array of
//     {key, format, value}. A key with no value is omitted; a property with no
//     registered key appears under its opaque id.
//   • select is an ARRAY of option NAMES, single select included
//     (`"priorite":["4"]`, `"etat":["Ouvert"]`) — v1 served option keys.
//   • objects is an ARRAY of bare ids, never names.
//   • date is an RFC 3339 UTC string (`"2026-10-22T00:00:00Z"`).
//   • checkbox is a JSON bool, present only when true: absent reads as false.
//   • text is a string.
//
// Not measured: number, multi_select and files values (read here by analogy
// with select and objects, tolerantly), and whether a binned object carries an
// `archived` flag (see Archived).
public sealed class AnytypeV2PropertyMap(JsonObject? properties)
{
    public static AnytypeV2PropertyMap Of(AnytypeV2ObjectDocument document) => new(document.Properties);

    // The object's title. Empty for a type that has none (a report).
    public string Name => Text("name") ?? "";

    public string? Text(string key)
    {
        string? s = Value(key) is JsonValue v && v.TryGetValue(out string? text) ? text : null;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public bool Checkbox(string key) =>
        Value(key) is JsonValue v && v.TryGetValue(out bool ticked) && ticked;

    // The day of a date: the server appends a midnight time, the day is the fact.
    public string? Day(string key)
    {
        string? iso = Text(key);
        return iso is { Length: >= 10 } ? iso[..10] : null;
    }

    public string? Number(string key) =>
        Value(key) is JsonValue v && v.TryGetValue(out double n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : null;

    // The option names of a select or multi_select, in served order. A bare
    // string is accepted as a one-name list.
    public IReadOnlyList<string> Names(string key) => Strings(key);

    // The first option name of a single select.
    public string? Select(string key) => Strings(key).FirstOrDefault();

    public IReadOnlyList<string> ObjectIds(string key) => Strings(key);

    public int Count(string key) => Value(key) is JsonArray array ? array.Count : 0;

    // True when the object is in Anytype's bin. Whether v2 serves the flag, and
    // where, is not measured: a live object was never seen carrying it, so it is
    // read from the properties and from the root, and absent means not binned.
    public bool Archived(JsonObject? root) =>
        Checkbox("archived") || (root?["archived"] is JsonValue v && v.TryGetValue(out bool b) && b);

    private JsonNode? Value(string key) => properties is not null && properties.TryGetPropertyValue(key, out JsonNode? n) ? n : null;

    private List<string> Strings(string key)
    {
        var list = new List<string>();
        switch (Value(key))
        {
            case JsonArray array:
                foreach (JsonNode? item in array)
                    if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s))
                        list.Add(s);
                break;
            case JsonValue single when single.TryGetValue(out string? one) && !string.IsNullOrEmpty(one):
                list.Add(one);
                break;
        }
        return list;
    }
}
