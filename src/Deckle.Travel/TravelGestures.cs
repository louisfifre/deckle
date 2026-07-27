using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Travel;

// The guarded trip-preparation operations. The surface builds, plans, records
// and reads; it exposes no deletion — the user deletes in the app. All writes
// pass through these guards before reaching Anytype; the MCP catalog validates
// argument shape only.
public sealed class TravelGestures
{
    private const int MaxBatchSize = 100;

    private readonly AnytypeApiClient _api;
    private readonly string _spaceId;
    private readonly TravelRuntimeProvider _runtime;

    public TravelGestures(AnytypeApiClient api, string spaceId)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        _api = api;
        _spaceId = spaceId;
        _runtime = new TravelRuntimeProvider(api, spaceId);
    }

    public async Task<string> CreateAsync(
        string type,
        IReadOnlyList<TravelCreateItem> items,
        CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        type = NormalizeType(type);
        ValidateBatch(items, "create");
        TravelSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("travel_create", type, ct).ConfigureAwait(false);
        TravelObjectIndex index = await TravelObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var writer = new TravelPropertyWriter(_api, _spaceId, schema, index);

        var prepared = new List<(string Name, JsonObject Payload)>();
        foreach (TravelCreateItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException(
                    "Un objet Travel s’identifie par son nom ; le nom ne peut pas être vide.", nameof(items));

            JsonArray properties = await writer.BuildAsync(type, item.Properties, ct).ConfigureAwait(false);
            switch (type)
            {
                case TravelSchema.Types.Stay:
                    RequireProperties(properties, item.Name,
                        [TravelSchema.Properties.StartDate, TravelSchema.Properties.EndDate]);
                    break;
                case TravelSchema.Types.Expense:
                    RequireProperties(properties, item.Name,
                        [TravelSchema.Properties.Amount, TravelSchema.Properties.Date,
                         TravelSchema.Properties.ExpenseCategory]);
                    await EnsureExpenseStayAsync(properties, item.Name, index, writer, schema, ct)
                        .ConfigureAwait(false);
                    break;
            }

            prepared.Add((
                item.Name.Trim(),
                new JsonObject
                {
                    ["type_key"] = type,
                    ["name"] = item.Name.Trim(),
                    ["properties"] = properties,
                }));
        }

        var report = new List<string>();
        foreach ((string name, JsonObject payload) in prepared)
        {
            JsonObject created = await _api.CreateObjectAsync(_spaceId, payload, ct).ConfigureAwait(false);
            report.Add($"- {type} · {name}");

            // A single-city stay has one degenerate stage, created in the same
            // gesture as the stay: same name, same dates, linked to it. A
            // multi-city trip renames it and adds the others explicitly.
            if (type == TravelSchema.Types.Stay)
            {
                string stayId = TravelObjectJson.Id(TravelObjectJson.Unwrap(created));
                if (stayId.Length == 0)
                    throw new InvalidOperationException(
                        $"Anytype n’a pas renvoyé l’id du séjour « {name} » ; son étape ne peut pas être créée.");

                var stageProperties = new JsonArray
                {
                    new JsonObject
                    {
                        ["key"] = TravelSchema.Properties.Stay,
                        ["objects"] = new JsonArray(stayId),
                    },
                };
                foreach (string dateKey in (string[])
                    [TravelSchema.Properties.StartDate, TravelSchema.Properties.EndDate])
                {
                    if (FindEntry(payload["properties"] as JsonArray, dateKey) is JsonObject entry)
                        stageProperties.Add(entry.DeepClone().AsObject());
                }

                await _api.CreateObjectAsync(_spaceId, new JsonObject
                {
                    ["type_key"] = TravelSchema.Types.Stage,
                    ["name"] = name,
                    ["properties"] = stageProperties,
                }, ct).ConfigureAwait(false);
                report.Add($"- {TravelSchema.Types.Stage} · {name} (étape dégénérée du séjour)");
            }
        }

        DeckleTravelSource.Log.GestureCompleted("create", Elapsed(started));
        return "Créé :\n" + string.Join("\n", report);
    }

    public async Task<string> UpdateAsync(
        IReadOnlyList<TravelUpdateItem> items,
        CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        ValidateBatch(items, "update");
        TravelSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("travel_update", "batch", ct).ConfigureAwait(false);
        TravelObjectIndex index = await TravelObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var writer = new TravelPropertyWriter(_api, _spaceId, schema, index);
        var prepared = new List<(string Id, string Display, JsonObject Payload)>();
        var targets = new HashSet<string>(StringComparer.Ordinal);

        foreach (TravelUpdateItem item in items)
        {
            JsonObject target = index.Resolve(item.Object);
            string id = TravelObjectJson.Id(target);
            if (!targets.Add(id))
                throw new InvalidOperationException(
                    $"Le lot cible deux fois « {TravelObjectIndex.Display(target)} ».");

            if (item.Name is not null && string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Le nom ne peut pas être vide.", nameof(items));

            string type = TravelObjectJson.TypeKey(target);
            JsonArray properties = await writer.BuildAsync(type, item.Properties, ct).ConfigureAwait(false);
            if (item.Name is null && properties.Count == 0)
                throw new ArgumentException(
                    $"Rien à mettre à jour pour « {item.Object} » : fournis name ou properties.",
                    nameof(items));

            var payload = new JsonObject();
            if (item.Name is not null) payload["name"] = item.Name.Trim();
            if (properties.Count > 0) payload["properties"] = properties;
            prepared.Add((id, TravelObjectIndex.Display(target), payload));
        }

        foreach ((string id, string _, JsonObject payload) in prepared)
            await _api.UpdateObjectAsync(_spaceId, id, payload, ct).ConfigureAwait(false);

        DeckleTravelSource.Log.GestureCompleted("update", Elapsed(started));
        return "Mis à jour :\n" + string.Join("\n", prepared.Select(item => "- " + item.Display));
    }

    public async Task<string> GetAsync(string selector, CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);
        TravelObjectIndex index = await TravelObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        JsonObject value = index.Resolve(selector);
        DeckleTravelSource.Log.GestureCompleted("get", Elapsed(started));
        return index.Render(value);
    }

    public async Task<string> SearchAsync(TravelSearchFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);
        TravelObjectIndex index = await TravelObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);

        string? type = filter.Type is null ? null : NormalizeType(filter.Type);
        string? category = NormalizeCategory(filter.Category);
        string? mode = NormalizeVocabulary(TravelSchema.Properties.Mode, filter.Mode);
        string? stayId = filter.Stay is null
            ? null
            : TravelObjectJson.Id(index.Resolve(filter.Stay, [TravelSchema.Types.Stay]));

        IEnumerable<JsonObject> query = index.Objects.Where(value =>
            TravelSchema.CreatableTypes.Contains(TravelObjectJson.TypeKey(value)));
        if (type is not null)
            query = query.Where(value => TravelObjectJson.TypeKey(value) == type);
        if (stayId is not null)
            query = query.Where(value =>
                TravelObjectJson.ObjectReferences(value, TravelSchema.Properties.Stay).Contains(stayId)
                || TravelObjectJson.Id(value) == stayId);
        if (category is not null)
            query = query.Where(value =>
                SelectMatches(value, TravelSchema.Properties.ActivityCategory, category)
                || SelectMatches(value, TravelSchema.Properties.ExpenseCategory, category)
                || SelectMatches(value, TravelSchema.Properties.PlaceCategory, category));
        if (mode is not null)
            query = query.Where(value => SelectMatches(value, TravelSchema.Properties.Mode, mode));
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string text = filter.Text.Trim();
            query = query.Where(value =>
                SearchText(value, index).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        JsonObject[] matches = query.ToArray();
        DeckleTravelSource.Log.GestureCompleted("search", Elapsed(started));
        if (matches.Length == 0) return "Aucun résultat.";
        return string.Join("\n", matches.Select(value =>
            $"{TravelObjectJson.TypeKey(value)} · {TravelObjectIndex.Display(value)} · {TravelObjectJson.Id(value)}"));
    }

    // The expense's stay resolves from its date when exactly one stay covers
    // it; otherwise the gesture requires the stay explicitly and fails clearly
    // rather than guessing.
    private async Task EnsureExpenseStayAsync(
        JsonArray properties,
        string expenseName,
        TravelObjectIndex index,
        TravelPropertyWriter writer,
        TravelSchemaRuntime schema,
        CancellationToken ct)
    {
        if (FindEntry(properties, TravelSchema.Properties.Stay) is not null) return;

        string dateText = (FindEntry(properties, TravelSchema.Properties.Date)?["date"] as JsonValue)
            ?.GetValue<string>() ?? "";
        if (!TryParseDay(dateText, out DateOnly day))
            throw new InvalidOperationException(
                $"La date de la dépense « {expenseName} » est illisible ; le séjour ne peut pas être résolu.");

        var covering = new List<JsonObject>();
        foreach (JsonObject stay in index.OfType(TravelSchema.Types.Stay))
        {
            if (TryParseDay(TravelObjectJson.DateValue(stay, TravelSchema.Properties.StartDate), out DateOnly start)
                && TryParseDay(TravelObjectJson.DateValue(stay, TravelSchema.Properties.EndDate), out DateOnly end)
                && start <= day && day <= end)
            {
                covering.Add(stay);
            }
        }

        if (covering.Count != 1)
        {
            string detail = covering.Count == 0
                ? "aucun séjour ne couvre cette date"
                : "plusieurs séjours la couvrent : "
                  + string.Join(", ", covering.Select(TravelObjectIndex.Display));
            throw new InvalidOperationException(
                $"Le séjour de la dépense « {expenseName} » ne peut pas être résolu depuis sa date ({detail}). "
                + "Fournis le séjour explicitement.");
        }

        properties.Add(await writer.BuildEntryAsync(
            schema.Property(TravelSchema.Properties.Stay),
            JsonValue.Create(TravelObjectJson.Id(covering[0])),
            ct).ConfigureAwait(false));
    }

    private static void RequireProperties(
        JsonArray properties, string name, IReadOnlyList<string> requiredKeys)
    {
        string[] missing = requiredKeys
            .Where(key => FindEntry(properties, key) is null)
            .Select(key => TravelTerms.Current.PropertyName(key))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException(
                $"« {name} » : propriétés obligatoires manquantes — {string.Join(", ", missing)}.");
    }

    private static JsonObject? FindEntry(JsonArray? properties, string key)
    {
        if (properties is null) return null;
        foreach (JsonNode? node in properties)
            if (node is JsonObject entry
                && string.Equals(TravelObjectJson.String(entry, "key"), key, StringComparison.Ordinal))
                return entry;
        return null;
    }

    private static bool TryParseDay(string value, out DateOnly day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
        {
            day = DateOnly.FromDateTime(moment.Date);
            return true;
        }
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out day);
    }

    private static string NormalizeType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le type Travel ne peut pas être vide.", nameof(value));
        value = value.Trim().ToLowerInvariant();
        if (!TravelSchema.CreatableTypes.Contains(value))
            throw new ArgumentException(
                $"Type Travel inconnu « {value} ». Types admis : {string.Join(", ", TravelSchema.CreatableTypes)}.",
                nameof(value));
        return value;
    }

    // The category filter reaches the three category selects at once; the
    // caller narrows with the type filter when the families collide.
    private static string? NormalizeCategory(string? value)
    {
        if (value is null) return null;
        string trimmed = value.Trim();
        foreach (string propertyKey in (string[])
            [TravelSchema.Properties.ActivityCategory, TravelSchema.Properties.ExpenseCategory])
        {
            IReadOnlyDictionary<string, string> vocabulary = TravelSchema.ClosedVocabularies[propertyKey];
            KeyValuePair<string, string>[] matches = vocabulary.Where(pair =>
                    string.Equals(pair.Key, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value, trimmed, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 1) return matches[0].Key;
        }
        // Place categories are live options; match them by the requested text.
        return trimmed;
    }

    private static string? NormalizeVocabulary(string propertyKey, string? value)
    {
        if (value is null) return null;
        IReadOnlyDictionary<string, string> vocabulary = TravelSchema.ClosedVocabularies[propertyKey];
        KeyValuePair<string, string>[] matches = vocabulary.Where(pair =>
                string.Equals(pair.Key, value.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Value, value.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new ArgumentException(
                $"Valeur inconnue « {value} ». Valeurs admises : {string.Join(", ", vocabulary.Values)}.");
        return matches[0].Key;
    }

    private static bool SelectMatches(JsonObject value, string propertyKey, string expected)
    {
        JsonNode? select = TravelObjectJson.Property(value, propertyKey)?["select"];
        if (select is null) return false;
        string actual = select switch
        {
            JsonValue scalar when scalar.TryGetValue<string>(out string? text) => text ?? "",
            JsonObject obj => TravelObjectJson.String(obj, "key") is { Length: > 0 } key
                ? key
                : TravelObjectJson.String(obj, "name"),
            _ => "",
        };
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (TravelSchema.ClosedVocabularies.TryGetValue(propertyKey, out var vocabulary)
            && vocabulary.TryGetValue(expected, out string? name))
            return string.Equals(actual, name, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static void ValidateBatch<T>(IReadOnlyList<T> items, string operation)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException($"{operation} exige au moins une entrée.", nameof(items));
        if (items.Count > MaxBatchSize)
            throw new ArgumentException($"{operation} accepte au maximum {MaxBatchSize} entrées.", nameof(items));
    }

    private static string SearchText(JsonObject value, TravelObjectIndex index)
    {
        var builder = new StringBuilder(TravelObjectIndex.Display(value));
        if (value["properties"] is JsonArray properties)
            foreach (JsonNode? node in properties)
                if (node is JsonObject property)
                    builder.Append(' ').Append(TravelObjectJson.Render(property, index.DisplayForId));
        return builder.ToString();
    }

    private static double Elapsed(DateTime started) => (DateTime.UtcNow - started).TotalMilliseconds;
}
