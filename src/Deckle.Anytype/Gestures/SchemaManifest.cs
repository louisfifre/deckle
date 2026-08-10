using System.Text.Json.Nodes;

namespace Deckle.Anytype;

internal sealed record SchemaManifest(
    IReadOnlyList<TypeSpec> Types,
    IReadOnlyList<PropertySpec> Properties,
    IReadOnlyList<SectionSpec> Sections)
{
    public static SchemaManifest Parse(JsonObject root)
    {
        JsonShape.RequireOnly(root, ["types", "properties", "sections"], "manifest");

        var types = new List<TypeSpec>();
        if (root.TryGetPropertyValue("types", out JsonNode? typesNode) && typesNode is not null)
        {
            if (typesNode is not JsonArray typeArray)
                throw new ArgumentException("Le champ « types » doit être un tableau.");

            foreach (JsonNode? node in typeArray)
            {
                if (node is not JsonObject obj)
                    throw new ArgumentException("Chaque entrée de « types » doit être un objet.");
                types.Add(TypeSpec.Parse(obj));
            }
        }

        var properties = new List<PropertySpec>();
        if (root.TryGetPropertyValue("properties", out JsonNode? propertiesNode) && propertiesNode is not null)
        {
            if (propertiesNode is not JsonArray propArray)
                throw new ArgumentException("Le champ « properties » doit être un tableau.");

            foreach (JsonNode? node in propArray)
            {
                if (node is not JsonObject obj)
                    throw new ArgumentException("Chaque entrée de « properties » doit être un objet.");
                properties.Add(PropertySpec.Parse(obj));
            }
        }

        var sections = new List<SectionSpec>();
        if (root.TryGetPropertyValue("sections", out JsonNode? sectionsNode) && sectionsNode is not null)
        {
            if (sectionsNode is not JsonArray sectionArray)
                throw new ArgumentException("Le champ « sections » doit être un tableau.");

            foreach (JsonNode? node in sectionArray)
            {
                if (node is not JsonObject obj)
                    throw new ArgumentException("Chaque entrée de « sections » doit être un objet.");
                sections.Add(SectionSpec.Parse(obj));
            }
        }

        if (types.Count == 0 && properties.Count == 0 && sections.Count == 0)
            throw new ArgumentException(
                "Le manifeste doit contenir au moins un type, une propriété ou une section.");

        JsonShape.RequireUnique(types.Select(t => t.Key), "types.key");
        JsonShape.RequireUnique(properties.Select(p => p.Key), "properties.key");
        JsonShape.RequireUnique(sections.Select(s => s.Name), "sections.name");

        return new SchemaManifest(types, properties, sections);
    }
}

// A section is a pinned sidebar folder: one collection object (built-in Anytype
// type key "collection") whose members are the section's TYPE objects. The
// manifest names the collection and lists the member type keys; actual sidebar
// pinning has no API endpoint and stays an in-app gesture. Section icons are
// emoji-only: the API refuses named icons on OBJECT creation (400 "icon name
// and color are not supported for object", verified live 2026-08-10) — the
// named-icon grammar belongs to types.
internal sealed record SectionSpec(
    string Name,
    TypeIconSpec? Icon,
    IReadOnlyList<string> Types)
{
    public static SectionSpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["name", "icon", "types"], "section");

        string name = SchemaManifestFields.RequiredString(obj, "name", rejectNonString: true);

        TypeIconSpec? icon = null;
        if (obj.TryGetPropertyValue("icon", out JsonNode? iconNode))
        {
            if (iconNode is not JsonObject iconObject)
                throw new ArgumentException($"Le champ « icon » de la section « {name} » doit être un objet.");
            icon = TypeIconSpec.Parse(iconObject, $"section {name}", $"la section « {name} »");
            if (icon.Format == "icon")
                throw new ArgumentException(
                    $"La section « {name} » ne peut pas porter d'icône nommée : l'API Anytype "
                    + "n'accepte que des emoji sur un objet. Utilise le format emoji.");
        }

        var types = new List<string>();
        if (obj.TryGetPropertyValue("types", out JsonNode? typesNode) && typesNode is not null)
        {
            if (typesNode is not JsonArray arr)
                throw new ArgumentException($"Le champ « types » de la section « {name} » doit être un tableau.");

            foreach (JsonNode? node in arr)
            {
                if (node is not JsonValue value ||
                    !value.TryGetValue<string>(out string? typeKey) ||
                    typeKey is null)
                    throw new ArgumentException(
                        $"Chaque type de la section « {name} » doit être une clé string.");
                types.Add(KeyRules.Validate(typeKey, "types"));
            }
        }
        if (types.Count == 0)
            throw new ArgumentException(
                $"La section « {name} » doit lister au moins une clé de type dans « types ».");
        JsonShape.RequireUnique(types, $"section {name}.types");

        return new SectionSpec(name, icon, types);
    }
}

internal sealed record TypeSpec(
    string Key,
    string Name,
    string PluralName,
    string Layout,
    TypeIconSpec? Icon,
    IReadOnlyList<string> Properties)
{
    private static readonly HashSet<string> AllowedLayouts =
        new(StringComparer.Ordinal) { "basic", "profile", "action", "note", "collection" };

    public static TypeSpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "plural_name", "layout", "icon", "properties"], "type");

        string key = SchemaManifestFields.RequiredKey(obj, "key", rejectNonString: true);
        string name = SchemaManifestFields.RequiredString(obj, "name", rejectNonString: true);
        string pluralName = SchemaManifestFields.OptionalString(obj, "plural_name", rejectNonString: true)
            ?? DefaultPluralName(name);
        string layout = SchemaManifestFields.OptionalString(obj, "layout", rejectNonString: true) ?? "basic";
        if (!AllowedLayouts.Contains(layout))
            throw new ArgumentException(
                $"Layout inconnu « {layout} » pour le type « {key} ». " +
                $"Layouts acceptés : {string.Join(", ", AllowedLayouts)}.");

        TypeIconSpec? icon = null;
        if (obj.TryGetPropertyValue("icon", out JsonNode? iconNode))
        {
            if (iconNode is not JsonObject iconObject)
                throw new ArgumentException($"Le champ « icon » du type « {key} » doit être un objet.");
            icon = TypeIconSpec.Parse(iconObject, key);
        }

        var props = new List<string>();
        if (obj.TryGetPropertyValue("properties", out JsonNode? propsNode) && propsNode is not null)
        {
            if (propsNode is not JsonArray arr)
                throw new ArgumentException($"Le champ « properties » du type « {key} » doit être un tableau.");

            foreach (JsonNode? node in arr)
            {
                if (node is not JsonValue value ||
                    !value.TryGetValue<string>(out string? prop) ||
                    prop is null)
                    throw new ArgumentException(
                        $"Chaque propriété du type « {key} » doit être une clé string.");
                props.Add(KeyRules.Validate(prop, "properties"));
            }
        }
        JsonShape.RequireUnique(props, $"type {key}.properties");
        return new TypeSpec(key, name, pluralName, layout, icon, props);
    }

    private static string DefaultPluralName(string name) =>
        name.EndsWith('s') || name.EndsWith('x') ? name : name + "s";
}

internal sealed record PropertySpec(
    string Key,
    string Name,
    string Format,
    IReadOnlyList<TagSpec> Tags)
{
    private static readonly HashSet<string> AllowedFormats =
        new(StringComparer.Ordinal)
        {
            "text", "number", "select", "multi_select", "date", "files",
            "checkbox", "url", "email", "phone", "objects",
        };

    public static PropertySpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "format", "tags"], "property");

        string key = SchemaManifestFields.RequiredKey(obj, "key", rejectNonString: false);
        string name = SchemaManifestFields.RequiredString(obj, "name", rejectNonString: false);
        string format = SchemaManifestFields.RequiredString(obj, "format", rejectNonString: false);
        if (!AllowedFormats.Contains(format))
            throw new ArgumentException(
                $"Format inconnu « {format} » pour « {key} ». Formats : {string.Join(", ", AllowedFormats)}.");

        var tags = new List<TagSpec>();
        if (obj.TryGetPropertyValue("tags", out JsonNode? tagsNode) && tagsNode is not null)
        {
            if (tagsNode is not JsonArray arr)
                throw new ArgumentException($"Le champ « tags » de « {key} » doit être un tableau.");

            foreach (JsonNode? node in arr)
            {
                if (node is JsonValue value && value.TryGetValue<string>(out string? nameValue) && nameValue is not null)
                {
                    nameValue = nameValue.Trim();
                    if (nameValue.Length == 0)
                        throw new ArgumentException($"Chaque tag string de « {key} » doit être non vide.");
                    tags.Add(new TagSpec(nameValue, null, null));
                }
                else if (node is JsonObject tagObject)
                    tags.Add(TagSpec.Parse(tagObject));
                else
                    throw new ArgumentException(
                        $"Chaque tag de « {key} » doit être une string ou un objet.");
            }
        }
        JsonShape.RequireUnique(tags.Select(t => t.MatchKey), $"property {key}.tags");

        return new PropertySpec(key, name, format, tags);
    }

}

internal static class SchemaManifestFields
{
    public static string RequiredKey(JsonObject obj, string name, bool rejectNonString) =>
        KeyRules.Validate(RequiredString(obj, name, rejectNonString), name);

    public static string RequiredString(JsonObject obj, string name, bool rejectNonString) =>
        OptionalString(obj, name, rejectNonString)
        ?? throw new ArgumentException($"Champ requis manquant « {name} ».");

    public static string? OptionalString(JsonObject obj, string name, bool rejectNonString)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node is null)
            return null;
        if (node is JsonValue value && value.TryGetValue<string>(out string? text))
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (rejectNonString)
            throw new ArgumentException($"Le champ « {name} » doit être une string.");
        return null;
    }
}

internal sealed record TagSpec(string Name, string? Key, string? Color)
{
    public string MatchKey => Key ?? Name;

    public static TagSpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "color"], "tag");

        string name = obj["name"] is JsonValue nameValue
            && nameValue.TryGetValue<string>(out string? n)
            && !string.IsNullOrWhiteSpace(n)
            ? n.Trim()
            : throw new ArgumentException("Chaque tag objet doit porter un champ name.");

        string? key = obj["key"] is JsonValue keyValue
            && keyValue.TryGetValue<string>(out string? k)
            && !string.IsNullOrWhiteSpace(k)
            ? KeyRules.Validate(k.Trim(), "tag.key")
            : null;

        string? color = obj["color"] is JsonValue colorValue
            && colorValue.TryGetValue<string>(out string? c)
            && !string.IsNullOrWhiteSpace(c)
            ? c.Trim()
            : null;

        return new TagSpec(name, key, color);
    }
}

internal static class JsonShape
{
    public static void RequireOnly(JsonObject obj, IReadOnlyCollection<string> allowed, string owner)
    {
        foreach (string key in obj.Select(kv => kv.Key))
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Champ inconnu « {key} » dans « {owner} ». Champs acceptés : {string.Join(", ", allowed)}.");
    }

    public static void RequireUnique(IEnumerable<string> keys, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            if (!seen.Add(key))
                throw new ArgumentException($"Doublon interdit dans « {label} » : {key}.");
    }
}

internal static class KeyRules
{
    public static string Validate(string value, string name)
    {
        value = value.Trim();
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]) ||
            value.Any(c => c != '_' && !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)))
        {
            throw new ArgumentException(
                $"« {name} » doit être une clé snake_case ASCII, sans accent ni espace : {value}.");
        }
        return value;
    }
}
