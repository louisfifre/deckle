using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Deckle.Anytype;

namespace Deckle.Home;

// Reads the target space back through the raw client and holds it against the
// sheet: each sheet object is matched within its type by code, else by name
// (an idea by its first line), then every mapped property, the name, the body
// (whitespace set aside), the done box of a task and the collection
// memberships are compared.
//
// Values compare by meaning, not by spelling: an option by the manifest
// option its name stands for (the source may say « En cours » where the
// target says « In progress »), a relation by the code or name of what it
// points at, numbers as numbers. What the bridge left unmapped is not
// compared: it was never meant to arrive.
//
// Counts are returned; the differences, which name objects and values, are
// written beside the sheet only.
internal sealed partial class HomeRecreationComparer(AnytypeApiClient api)
{
    private const int PageLimit = 100;

    public async Task<HomeComparison> CompareAsync(
        HomeReplaySheet sheet, string targetSpaceId, string? detailPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSpaceId);
        var result = new HomeComparison();
        var differences = new List<HomeDifference>();

        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(api, targetSpaceId, ct).ConfigureAwait(false);
        var tags = new TagNames(api, targetSpaceId);
        IReadOnlyDictionary<string, List<string>>? memberships =
            await MembershipsAsync(targetSpaceId, index, ct).ConfigureAwait(false);
        if (memberships is null) result.CollectionsUnread = true;

        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (HomeReplayObject expected in sheet.Objects)
        {
            JsonObject? actual = Match(expected, index, matched);
            if (actual is null)
            {
                result.Missing++;
                differences.Add(new(expected.Type, expected.OldId, expected.Display, "(objet)", "présent", "absent"));
                continue;
            }
            matched.Add(HomeObjectJson.Id(actual));

            string body = actual["markdown"] is JsonValue
                ? HomeObjectJson.String(actual, "markdown")
                : HomeObjectJson.String(HomeObjectJson.Unwrap(
                    await api.GetObjectAsync(targetSpaceId, HomeObjectJson.Id(actual), ct).ConfigureAwait(false)),
                    "markdown");

            var found = new List<HomeDifference>();
            void Check(string field, string want, string got)
            {
                result.ValuesCompared++;
                if (string.Equals(want, got, StringComparison.Ordinal)) return;
                result.ValuesDifferent++;
                HomeReplaySheetReport.Increment(result.DifferentByField, field);
                found.Add(new(expected.Type, expected.OldId, expected.Display, field, want, got));
            }

            // A circuit without a human name is titled by its code.
            if (expected.Type != HomeSchema.Types.Idea)
                Check("(nom)",
                    expected.Name.Trim().Length > 0 ? expected.Name.Trim() : expected.Code ?? "",
                    HomeObjectJson.String(actual, "name").Trim());
            Check("(corps)", NormalizeBody(expected.Body), NormalizeBody(body));
            if (expected.Done is bool done)
                Check("done", done ? "true" : "false", CheckboxText(HomeObjectJson.Property(actual, "done")));

            foreach ((string key, JsonNode? value) in expected.Properties)
            {
                string format = HomeSchema.RequiredProperties.TryGetValue(key, out string? known) ? known : "text";
                JsonObject? entry = HomeObjectJson.Property(actual, key);
                string got = format switch
                {
                    "select" => entry?["select"] is JsonNode select
                        ? Canonical(key, await tags.NameAsync(key, select, ct).ConfigureAwait(false))
                        : "",
                    "multi_select" => await MultiAsync(key, entry, tags, ct).ConfigureAwait(false),
                    _ => Actual(format, entry, index),
                };
                Check(key, Expected(key, format, value), got);
            }

            if (memberships is not null && expected.Collections.Count > 0)
            {
                memberships.TryGetValue(HomeObjectJson.Id(actual), out List<string>? held);
                Check("(collections)", Set(expected.Collections), Set(held ?? []));
            }

            if (found.Count == 0) result.Equal++;
            else
            {
                result.Different++;
                differences.AddRange(found);
            }
        }

        foreach (JsonObject value in index.Objects)
            if (HomeSchema.CreatableTypes.Contains(HomeObjectJson.TypeKey(value), StringComparer.Ordinal)
                && !matched.Contains(HomeObjectJson.Id(value)))
            {
                result.Extra++;
                differences.Add(new(HomeObjectJson.TypeKey(value), "", HomeObjectIndex.Display(value),
                    "(objet)", "absent", "présent"));
            }

        if (detailPath is not null)
            HomeReplaySheet.WriteJson(detailPath, new HomeComparisonDetails { Differences = differences });
        return result;
    }

    // The first target object of the same type, not matched yet, with the
    // same code, else the same name. Twins in the source meet twins in the
    // target one by one.
    private static JsonObject? Match(HomeReplayObject expected, HomeObjectIndex index, IReadOnlySet<string> matched)
    {
        IEnumerable<JsonObject> pool = index.Objects.Where(value =>
            HomeObjectJson.TypeKey(value) == expected.Type && !matched.Contains(HomeObjectJson.Id(value)));
        if (!string.IsNullOrEmpty(expected.Code))
            return pool.FirstOrDefault(value =>
                string.Equals(HomeObjectJson.Code(value), expected.Code, StringComparison.OrdinalIgnoreCase));
        string name = expected.Selector.Trim();
        return pool.FirstOrDefault(value =>
            string.Equals(HomeNameGuard.ComparedName(value).Trim(), name, StringComparison.Ordinal));
    }

    // What a sheet value means, in the same form Actual gives the target's.
    internal static string Expected(string propertyKey, string format, JsonNode? value)
    {
        if (value is null) return "";
        IEnumerable<string> Items() => value is JsonArray array
            ? array.Select(node => node is JsonValue item && item.TryGetValue(out string? text) ? text ?? "" : "")
            : [Scalar(value)];
        return format switch
        {
            "select" => Canonical(propertyKey, Scalar(value)),
            "multi_select" => Set(Items().Select(name => Canonical(propertyKey, name))),
            "objects" => Set(Items().Select(selector => selector.Trim().ToLowerInvariant())),
            "number" => NumberText(value),
            "checkbox" => value is JsonValue box && box.TryGetValue(out bool flag) && flag ? "true" : "false",
            _ => Scalar(value).Trim(),
        };
    }

    // A target value in the comparison form. Select and multi-select are
    // read through the tag names (TagNames) by the caller.
    internal static string Actual(string format, JsonObject? entry, HomeObjectIndex index)
    {
        if (entry is null) return format == "checkbox" ? "false" : "";
        return format switch
        {
            "objects" => Set((entry["objects"] as JsonArray ?? [])
                .Select(node => node is JsonValue item && item.TryGetValue(out string? id) ? id ?? "" : "")
                .Select(id => SelectorOf(id, index).Trim().ToLowerInvariant())),
            "number" => entry["number"] is JsonNode number ? NumberText(number) : "",
            "checkbox" => CheckboxText(entry),
            "date" => entry["date"] is JsonValue date && date.TryGetValue(out string? text) ? text?.Trim() ?? "" : "",
            _ => entry["text"] is JsonValue scalar && scalar.TryGetValue(out string? text2) ? text2?.Trim() ?? "" : "",
        };
    }

    private static async Task<string> MultiAsync(string key, JsonObject? entry, TagNames tags, CancellationToken ct)
    {
        if (entry?["multi_select"] is not JsonArray options) return "";
        var names = new List<string>();
        foreach (JsonNode? option in options)
            if (option is not null) names.Add(Canonical(key, await tags.NameAsync(key, option, ct).ConfigureAwait(false)));
        return Set(names);
    }

    // The manifest option a name stands for, under any of its names (key,
    // French label, manifest name); an option the manifest does not seed
    // (an open vocabulary) compares by its name, case set aside.
    internal static string Canonical(string propertyKey, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0) return "";
        if (HomeSchema.TargetSeededVocabularies.TryGetValue(propertyKey, out IReadOnlyList<string>? keys))
            foreach (string key in keys)
                if (HomeSchema.OptionAliases(propertyKey, key).Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    return "option:" + key;
        return "nom:" + trimmed.ToLowerInvariant();
    }

    private static string SelectorOf(string id, HomeObjectIndex index)
    {
        JsonObject? value = index.Objects.FirstOrDefault(candidate => HomeObjectJson.Id(candidate) == id);
        if (value is null) return id;
        string code = HomeObjectJson.Code(value);
        return code.Length > 0 && HomeSchema.CodedTypes.Contains(HomeObjectJson.TypeKey(value), StringComparer.Ordinal)
            ? code
            : HomeNameGuard.ComparedName(value);
    }

    internal static string NormalizeBody(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "" : Whitespace().Replace(body, " ").Trim();

    private static string CheckboxText(JsonObject? entry) =>
        entry?["checkbox"] is JsonValue box && box.TryGetValue(out bool flag) && flag ? "true" : "false";

    private static string NumberText(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double number)) return number.ToString("R", CultureInfo.InvariantCulture);
            if (value.TryGetValue(out long integer)) return ((double)integer).ToString("R", CultureInfo.InvariantCulture);
            if (value.TryGetValue(out int small)) return ((double)small).ToString("R", CultureInfo.InvariantCulture);
            if (value.TryGetValue(out string? text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number.ToString("R", CultureInfo.InvariantCulture);
        }
        return node.ToJsonString();
    }

    private static string Scalar(JsonNode value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue(out string? text) => text ?? "",
        _ => value.ToJsonString(),
    };

    private static string Set(IEnumerable<string> values) =>
        string.Join(" | ", values.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    // Object id → names of the collections that hold it, read through every
    // collection of the target. Null when one cannot be read: memberships
    // are then left out of the comparison and the result says so.
    private async Task<IReadOnlyDictionary<string, List<string>>?> MembershipsAsync(
        string spaceId, HomeObjectIndex index, CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (JsonObject collection in index.Objects.Where(value => HomeObjectJson.Layout(value) == "collection"))
        {
            IReadOnlyList<string>? members = await HomeSpaceExporter.ReadMembersAsync(
                api, spaceId, HomeObjectJson.Id(collection), ct).ConfigureAwait(false);
            if (members is null) return null;
            foreach (string member in members)
            {
                if (!result.TryGetValue(member, out List<string>? names)) result[member] = names = [];
                names.Add(HomeObjectJson.String(collection, "name").Trim());
            }
        }
        return result;
    }

    // The name of an option as the target holds it. A read may carry the
    // option whole ({id, key, name}) or only its id; an id is looked up in
    // the property's tags, read once per property.
    private sealed class TagNames(AnytypeApiClient api, string spaceId)
    {
        private Dictionary<string, string>? _propertyIds;
        private readonly Dictionary<string, Dictionary<string, string>> _names = new(StringComparer.Ordinal);

        public async Task<string> NameAsync(string propertyKey, JsonNode option, CancellationToken ct)
        {
            if (option is JsonObject whole && HomeObjectJson.String(whole, "name") is { Length: > 0 } name)
                return name;
            string id = option switch
            {
                JsonObject value => HomeObjectJson.String(value, "id"),
                JsonValue scalar when scalar.TryGetValue(out string? text) => text ?? "",
                _ => "",
            };
            if (id.Length == 0) return "";
            Dictionary<string, string> names = await NamesAsync(propertyKey, ct).ConfigureAwait(false);
            return names.TryGetValue(id, out string? found) ? found : id;
        }

        private async Task<Dictionary<string, string>> NamesAsync(string propertyKey, CancellationToken ct)
        {
            if (_names.TryGetValue(propertyKey, out Dictionary<string, string>? cached)) return cached;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            _names[propertyKey] = names;

            _propertyIds ??= await ReadPagesAsync(
                offset => api.ListPropertiesForSpaceAsync(spaceId, offset, PageLimit, ct),
                value => (HomeObjectJson.String(value, "key"), HomeObjectJson.String(value, "id"))).ConfigureAwait(false);
            if (!_propertyIds.TryGetValue(propertyKey, out string? propertyId)) return names;

            foreach ((string id, string name) in await ReadPagesAsync(
                         offset => api.ListPropertyTagsForSpaceAsync(spaceId, propertyId, offset, PageLimit, ct),
                         value => (HomeObjectJson.String(value, "id"), HomeObjectJson.String(value, "name")))
                         .ConfigureAwait(false))
                names[id] = name;
            return names;
        }

        private static async Task<Dictionary<string, string>> ReadPagesAsync(
            Func<int, Task<JsonObject>> page, Func<JsonObject, (string Key, string Value)> select)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int offset = 0; ;)
            {
                JsonObject root = await page(offset).ConfigureAwait(false);
                int returned = 0;
                foreach (JsonObject raw in (root["data"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    returned++;
                    (string key, string value) = select(HomeObjectJson.Unwrap(raw));
                    if (key.Length > 0) result[key] = value;
                }
                if (root["pagination"]?["has_more"]?.GetValue<bool>() != true || returned == 0) break;
                offset += returned;
            }
            return result;
        }
    }
}

// Counts of a comparison, safe to print: object counts, value counts, and the
// property keys that differ (manifest keys, never values).
internal sealed class HomeComparison
{
    public int Equal { get; set; }
    public int Different { get; set; }
    public int Missing { get; set; }
    public int Extra { get; set; }
    public int ValuesCompared { get; set; }
    public int ValuesDifferent { get; set; }
    public bool CollectionsUnread { get; set; }
    public SortedDictionary<string, int> DifferentByField { get; } = new(StringComparer.Ordinal);
}

internal sealed class HomeComparisonDetails
{
    [JsonPropertyName("differences")]
    public List<HomeDifference> Differences { get; set; } = [];
}

internal sealed record HomeDifference(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("old_id")] string OldId,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("actual")] string Actual);
