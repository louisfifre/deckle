using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A space plan is written once, in English, and built in any language: the
// localizer takes the plan and one terms file, and returns the same plan with
// every word a person reads put in that language. Nothing else changes, so the
// engine downstream never knows a plan was translated.
//
// Two kinds of words, two lookups:
//   - schema words, by key: a type's name and plural, a property's name, an
//     option's name (an option needs its `key` in the plan). The same terms
//     file names the space for the code (DevSpace), so the code and the plan
//     cannot drift apart.
//   - free text, by its English text (the gettext model: the source string is
//     the key): view names, template names, headings, descriptions, table
//     cells. A word whose translation depends on what it describes takes a
//     context: "All|type:idea" wins over "All" for a view that lists ideas,
//     since French agrees « Toutes » with « idée » and « Tous » with « projet ».
//
// A word the terms file does not name stays in English, so a partial file
// builds a mixed space rather than failing; PlanLocalizerTests hold the shipped
// files complete.
//
// References by name (a widget's view, a type's default template, a filter
// value naming an option, `object:<collection>`) go through the same lookup as
// what they point at, with the same context, so they keep pointing at it.
internal static class PlanLocalizer
{
    private const string ObjectPrefix = "object:";
    private const string TypePrefix = "type:";

    // Fields whose string value is free text a person reads.
    private static readonly HashSet<string> TextFields =
        ["name", "plural_name", "description", "text", "header", "default_template", "view"];

    // Fields whose array of strings is free text (a table row's cells, a
    // collection's items by name).
    private static readonly HashSet<string> TextListFields = ["cells", "items"];

    public static JsonObject Localize(JsonObject plan, JsonObject terms)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(terms);
        var words = new Words(terms);
        var result = (JsonObject)plan.DeepClone();

        // Option names first: a filter anywhere below may name an option by its
        // English name, and has to find the translated one.
        var optionNames = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (result["properties"] is JsonArray properties)
            foreach (JsonObject property in properties.OfType<JsonObject>())
            {
                string key = property["key"]?.GetValue<string>() ?? "";
                if (property["name"] is JsonValue)
                    property["name"] = words.Property(key) ?? words.Text(Str(property["name"]), null);
                if (property["description"] is JsonValue)
                    property["description"] = words.Text(Str(property["description"]), null);
                if (property["tags"] is not JsonArray tags) continue;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < tags.Count; i++)
                {
                    if (tags[i] is JsonValue bare)
                    {
                        string english = Str(bare);
                        tags[i] = map[english] = words.Text(english, OptionContext(key));
                    }
                    else if (tags[i] is JsonObject tag)
                    {
                        string english = Str(tag["name"]);
                        string? optionKey = tag["key"]?.GetValue<string>();
                        string localized = (optionKey is null ? null : words.Option(key, optionKey))
                            ?? words.Text(english, OptionContext(key));
                        tag["name"] = map[english] = localized;
                    }
                }
                optionNames[key] = map;
            }

        if (result["types"] is JsonArray types)
            foreach (JsonObject type in types.OfType<JsonObject>())
            {
                string key = type["key"]?.GetValue<string>() ?? "";
                (string? name, string? plural) = words.Type(key);
                if (type["name"] is JsonValue)
                    type["name"] = name ?? words.Text(Str(type["name"]), null);
                if (type["plural_name"] is JsonValue)
                    type["plural_name"] = plural ?? words.Text(Str(type["plural_name"]), null);
                foreach ((string field, JsonNode? value) in type.ToList())
                {
                    if (field is "key" or "name" or "plural_name" or "properties") continue;
                    if (value is JsonValue leaf && leaf.TryGetValue(out string? text))
                        type[field] = Leaf(field, text, TypePrefix + key, words);
                    else
                        Walk(value, field, TypePrefix + key, words, optionNames);
                }
            }

        foreach (string section in (string[])["templates", "collections", "queries", "pages", "widgets", "objects"])
            if (result[section] is JsonArray entries)
                foreach (JsonObject entry in entries.OfType<JsonObject>())
                    Walk(entry, null, EntryContext(section, entry), words, optionNames);

        return result;
    }

    private static string OptionContext(string propertyKey) => "property:" + propertyKey;

    // What a section entry speaks of: the type it serves, or a collection (or a
    // query), whose words agree with no type.
    private const string CollectionContext = "collection";

    private static string? EntryContext(string section, JsonObject entry) => section switch
    {
        "templates" => entry["for"] is JsonValue forType ? TypePrefix + Str(forType) : null,
        "collections" or "queries" => CollectionContext,
        "widgets" => Str(entry["target"]) switch
        {
            string t when t.StartsWith(TypePrefix, StringComparison.Ordinal) => t,
            string t when t.StartsWith(ObjectPrefix, StringComparison.Ordinal) => CollectionContext,
            _ => null,
        },
        _ => null,
    };

    private static void Walk(
        JsonNode? node, string? field, string? context, Words words,
        IReadOnlyDictionary<string, Dictionary<string, string>> optionNames)
    {
        switch (node)
        {
            case JsonObject obj:
                // A data view lists one type: what it shows takes that type's words.
                if (Str(obj["type"]) == "dataview")
                {
                    string source = Str(obj["source"]);
                    if (source.StartsWith(TypePrefix, StringComparison.Ordinal)) context = source;
                    else if (source.StartsWith(ObjectPrefix, StringComparison.Ordinal)) context = CollectionContext;
                }
                // A filter's value names options of the property it filters.
                if (obj["property"] is JsonValue filtered && obj.ContainsKey("condition")
                    && optionNames.TryGetValue(Str(filtered), out Dictionary<string, string>? options))
                    obj["value"] = Options(obj["value"], options);
                foreach ((string child, JsonNode? value) in obj.ToList())
                {
                    if (child == "value" && obj.ContainsKey("condition")) continue;
                    if (value is JsonValue leaf && leaf.TryGetValue(out string? text))
                        obj[child] = Leaf(child, text, context, words);
                    else
                        Walk(value, child, context, words, optionNames);
                }
                break;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue item && item.TryGetValue(out string? text))
                    {
                        if (field is not null && TextListFields.Contains(field))
                            array[i] = Reference(text, context, words) ?? words.Text(text, context);
                    }
                    else
                        Walk(array[i], field, context, words, optionNames);
                }
                break;
        }
    }

    private static string Leaf(string field, string text, string? context, Words words)
    {
        if (field is "target" or "source")
            return Reference(text, context, words) ?? text;
        return TextFields.Contains(field) ? words.Text(text, context) : text;
    }

    // `object:<name>` names a collection or a query by its name, which is free
    // text without a type; `type:<key>` and the rest are language-free.
    private static string? Reference(string text, string? context, Words words) =>
        text.StartsWith(ObjectPrefix, StringComparison.Ordinal)
            ? ObjectPrefix + words.Text(text[ObjectPrefix.Length..], CollectionContext)
            : null;

    private static JsonNode? Options(JsonNode? value, IReadOnlyDictionary<string, string> options) => value switch
    {
        JsonValue v when v.TryGetValue(out string? name) => options.GetValueOrDefault(name, name),
        JsonArray a => new JsonArray(a.Select(n => n is JsonValue v && v.TryGetValue(out string? name)
            ? (JsonNode)options.GetValueOrDefault(name, name)
            : n?.DeepClone()).ToArray()),
        _ => value?.DeepClone(),
    };

    private static string Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    // The words of one terms file, read straight from its JSON so a plan never
    // needs the file embedded in an assembly.
    private sealed class Words(JsonObject terms)
    {
        private readonly JsonObject? _types = terms["types"] as JsonObject;
        private readonly JsonObject? _properties = terms["properties"] as JsonObject;
        private readonly JsonObject? _options = terms["options"] as JsonObject;
        private readonly JsonObject? _texts = terms["texts"] as JsonObject;

        public (string? Name, string? Plural) Type(string key) =>
            _types?[key] is JsonObject type ? (NonEmpty(type["name"]), NonEmpty(type["plural"])) : (null, null);

        public string? Property(string key) => NonEmpty(_properties?[key]);

        public string? Option(string property, string option) =>
            _options?[property] is JsonObject names ? NonEmpty(names[option]) : null;

        public string Text(string english, string? context)
        {
            if (english.Length == 0) return english;
            if (context is not null && NonEmpty(_texts?[english + "|" + context]) is { } specific) return specific;
            return NonEmpty(_texts?[english]) ?? english;
        }

        private static string? NonEmpty(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    }
}
