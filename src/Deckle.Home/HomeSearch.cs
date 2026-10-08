using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

public sealed partial class HomeGestures
{
    public async Task<string> SearchAsync(HomeSearchFilter filter, CancellationToken ct = default) =>
        (await SearchPageAsync(filter, ct).ConfigureAwait(false)).Text;

    public async Task<HomeSearchPage> SearchPageAsync(HomeSearchFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(filter), "La limite doit être comprise entre 1 et 100.");
        if (filter.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(filter), "Le décalage ne peut pas être négatif.");

        DateTime started = DateTime.UtcNow;
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);
        // Pagination bounds the result, not the provider scan. Archive coverage
        // remains whatever the provider's object listing actually exposes.
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        IEnumerable<JsonObject> query = index.Objects;
        if (filter.Type is not null)
        {
            string type = schema.TypeKey(NormalizeType(filter.Type));
            query = query.Where(value => HomeObjectJson.TypeKey(value) == type);
        }

        if (filter.Room is not null)
        {
            string room = HomeObjectJson.Id(index.Resolve(filter.Room, [HomeSchema.Types.Room]));
            query = query.Where(value =>
                HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.InstalledIn).Contains(room)
                || HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.StoredIn).Contains(room));
        }
        (string? Selector, string Property, string[]? Types)[] relations =
        [
            (filter.Circuit, HomeSchema.Properties.Circuit, [HomeSchema.Types.Circuit]),
            (filter.Worksite, HomeSchema.Properties.Worksite, [HomeSchema.Types.Worksite]),
            (filter.System, HomeSchema.Properties.PartOf, [HomeSchema.Types.System]),
            (filter.ConnectedTo, "connected_to", [HomeSchema.Types.Point]),
            (filter.Panel, HomeSchema.Properties.Panel, [HomeSchema.Types.Panel]),
            (filter.About, HomeSchema.Properties.About, null),
        ];
        foreach (var relation in relations)
        {
            if (relation.Selector is null) continue;
            string id = HomeObjectJson.Id(index.Resolve(relation.Selector, relation.Types));
            string property = relation.Property;
            query = query.Where(value => HomeObjectJson.ObjectReferences(value, property).Contains(id));
        }

        (string? Requested, string Property)[] selects =
        [
            (filter.Category is null ? null : HomeCategories.OptionKey(filter.Category), HomeSchema.Properties.Category),
            (filter.Condition, HomeSchema.Properties.Condition),
            (filter.State, HomeSchema.Properties.State),
            (filter.Domain, "domain"),
            (filter.EquipmentCategory, "equipment_category"),
        ];
        foreach (var select in selects)
        {
            if (select.Requested is null) continue;
            string requested = select.Requested;
            string property = select.Property;
            SchemaTagInfo[] tags = schema.TagsFor(property).Values.Distinct().ToArray();
            query = query.Where(value => SearchSelectMatches(value, property, requested, tags));
        }
        if (filter.Done is bool done)
            query = query.Where(value => CheckboxValue(value, "done") == done);
        if (filter.Needed is bool needed)
            query = query.Where(value => CheckboxValue(value, "needed") == needed);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string text = filter.Text.Trim();
            query = query.Where(value => SearchText(value, index).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        HomeSearchPage result = BuildSearchPage(query, filter.Offset, filter.Limit);
        DeckleHomeSource.Log.GestureCompleted("search", Elapsed(started));
        return result;
    }

    internal static bool SearchSelectMatches(JsonObject value, string propertyKey, string requested,
        IReadOnlyList<SchemaTagInfo> tags)
    {
        JsonNode? select = HomeObjectJson.Property(value, propertyKey)?["select"];
        if (select is null) return false;
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { requested.Trim() };
        JsonObject? definition = HomeSchemaTargetData.Manifest["properties"]!.AsArray().OfType<JsonObject>()
            .FirstOrDefault(property => HomeSchema.WirePropertyKey(HomeObjectJson.String(property, "key")) == propertyKey);
        if (definition?["tags"] is JsonArray seeds)
            foreach (JsonObject seed in seeds.OfType<JsonObject>())
            {
                string key = HomeObjectJson.String(seed, "key");
                string name = HomeObjectJson.String(seed, "name");
                string label = SearchOptionLabel(propertyKey, key);
                if (!aliases.Contains(key) && !aliases.Contains(name) && !aliases.Contains(label)) continue;
                aliases.Add(key);
                aliases.Add(name);
                aliases.Add(label);
            }
        foreach (SchemaTagInfo tag in tags)
        {
            string label = SearchOptionLabel(propertyKey, tag.Key);
            if (!aliases.Contains(tag.Key) && !aliases.Contains(tag.Id)
                && !aliases.Contains(tag.Name) && !aliases.Contains(label)) continue;
            aliases.Add(tag.Id);
            aliases.Add(tag.Key);
            aliases.Add(tag.Name);
            aliases.Add(label);
        }
        aliases.Remove("");
        if (select is JsonValue scalar && scalar.TryGetValue<string>(out string? text))
            return text is not null && aliases.Contains(text);
        return select is JsonObject obj && new[] { "id", "key", "name" }
            .Any(field => aliases.Contains(HomeObjectJson.String(obj, field)));
    }

    private static string SearchOptionLabel(string propertyKey, string optionKey)
    {
        try { return HomeSchema.OptionLabel(propertyKey, optionKey); }
        catch (InvalidOperationException) { return optionKey; }
    }

    internal static HomeSearchPage BuildSearchPage(IEnumerable<JsonObject> values, int offset, int limit)
    {
        JsonObject[] ordered = values.OrderBy(HomeObjectJson.TypeKey, StringComparer.Ordinal)
            .ThenBy(HomeObjectIndex.Display, StringComparer.OrdinalIgnoreCase)
            .ThenBy(HomeObjectJson.Id, StringComparer.Ordinal).ToArray();
        JsonObject[] page = ordered.Skip(offset).Take(limit).ToArray();
        int? next = offset < ordered.Length && page.Length < ordered.Length - offset
            ? offset + page.Length : null;
        var matches = new JsonArray();
        foreach (JsonObject value in page)
            matches.Add(new JsonObject
            {
                ["id"] = HomeObjectJson.Id(value),
                ["type"] = HomeObjectJson.TypeKey(value),
                ["name"] = HomeObjectJson.Name(value),
                ["code"] = HomeObjectJson.Code(value),
            });
        string text = ordered.Length == 0 ? "Aucun résultat."
            : page.Length == 0 ? $"Aucun résultat à ce décalage ({offset}) ; {ordered.Length} résultat(s) au total."
            : string.Join("\n", page.Select(value =>
                $"{HomeObjectJson.TypeKey(value)} · {HomeObjectIndex.Display(value)} · {HomeObjectJson.Id(value)}"))
                + $"\n{offset + 1}–{offset + page.Length} sur {ordered.Length} résultat(s)."
                + (next is int cursor ? $" Suite : offset {cursor}." : "");
        return new HomeSearchPage(text, new JsonObject
        {
            ["matches"] = matches, ["total"] = ordered.Length,
            ["offset"] = offset, ["next_offset"] = next,
        });
    }
}
