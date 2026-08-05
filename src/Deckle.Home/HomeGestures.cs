using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Deckle.Anytype;

namespace Deckle.Home;

public sealed class HomeGestures
{
    private const int MaxBatchSize = 100;
    private static readonly Regex CircuitCodePattern = new(
        "^[A-Z][A-Z0-9]*(?:\\.[0-9]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GenericCodePattern = new(
        "^[A-Z0-9][A-Z0-9._-]{0,31}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AnytypeApiClient _api;
    private readonly string _spaceId;
    private readonly HomeRuntimeProvider _runtime;

    public HomeGestures(AnytypeApiClient api, string spaceId)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        _api = api;
        _spaceId = spaceId;
        _runtime = new HomeRuntimeProvider(api, spaceId);
    }

    public async Task<string> CreateAsync(
        string type,
        IReadOnlyList<HomeCreateItem> items,
        CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        type = NormalizeType(type);
        ValidateBatch(items, "create");
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("home_create", type, ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonObject> rooms = index.RoomRegistry();
        var propertyWriter = new HomePropertyWriter(_api, _spaceId, schema, index);
        var collectionWriter = new HomeCollectionWriter(_api, _spaceId, index);

        var prepared = new List<(string Display, JsonObject Payload, IReadOnlyList<string> Collections)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (HomeCreateItem item in items)
        {
            if (IsFreeTitled(type))
            {
                JsonArray lifeProperties = await propertyWriter.BuildAsync(
                    type, item.Properties, [], ct).ConfigureAwait(false);
                prepared.Add(PrepareLifeItem(type, item, lifeProperties, collectionWriter));
                continue;
            }

            if (item.Text is not null)
                throw new InvalidOperationException(
                    "Le corps est réservé aux idées et aux outils ; un objet d'inventaire est fait de propriétés.");
            string code = ValidateCode(type, RequireCode(type, item.Code));
            if (!seen.Add(code) || index.ContainsCode(code))
            {
                string suggestion = NextCodeSuggestion(type, code, index.Objects, seen);
                throw new InvalidOperationException(
                    $"Le code « {code} » existe déjà. Prochain code libre proposé : {suggestion}.");
            }

            HomeElementCode? elementCode = null;
            if (IsElement(type))
            {
                elementCode = HomeElementCode.Parse(code);
                string expectedType = HomeCategories.TypeFor(elementCode.Value.Category);
                if (!string.Equals(type, expectedType, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Le code « {code} » appartient au type {expectedType}, pas {type}.");
                if (!rooms.ContainsKey(elementCode.Value.Room))
                {
                    string known = rooms.Count == 0
                        ? "aucune"
                        : string.Join(", ", rooms.Keys.OrderBy(value => value, StringComparer.Ordinal));
                    throw new InvalidOperationException(
                        $"Code de pièce inconnu « {elementCode.Value.Room} » dans « {code} ». Pièces présentes : {known}.");
                }
                if (item.Name is not null)
                    throw new InvalidOperationException(
                        "Le titre d’un élément est son code. Place le nom humain dans la propriété « Libellé ».");
            }
            else if (type == HomeSchema.Types.Room && string.IsNullOrWhiteSpace(item.Name))
            {
                throw new ArgumentException("Une pièce exige un nom en plus de son code.", nameof(items));
            }

            RefuseCodeProperty(item.Properties);
            IReadOnlyCollection<string> reserved = IsElement(type)
                ? [HomeSchema.Properties.Room, HomeSchema.Properties.Category]
                : [];
            JsonArray properties = await propertyWriter.BuildAsync(
                type, item.Properties, reserved, ct).ConfigureAwait(false);
            IReadOnlyList<string> collections = collectionWriter.Resolve(item.Collections);

            if (elementCode is not null)
            {
                string roomId = HomeObjectJson.Id(rooms[elementCode.Value.Room]);
                properties.Add(new JsonObject
                {
                    ["key"] = HomeSchema.Properties.Room,
                    ["objects"] = new JsonArray(roomId),
                });
                properties.Add(await propertyWriter.BuildEntryAsync(
                    schema.Property(HomeSchema.Properties.Category),
                    JsonValue.Create(elementCode.Value.Category),
                    ct).ConfigureAwait(false));

                if (!properties.OfType<JsonObject>().Any(value =>
                        HomeObjectJson.String(value, "key") == HomeSchema.Properties.Existence))
                {
                    properties.Add(await propertyWriter.BuildEntryAsync(
                        schema.Property(HomeSchema.Properties.Existence),
                        JsonValue.Create(HomeSchema.Existence.Existing),
                        ct).ConfigureAwait(false));
                }
            }

            string title = IsElement(type) ? code : HumanTitle(code, item.Name);
            prepared.Add((
                code,
                new JsonObject
                {
                    ["type_key"] = type,
                    ["name"] = title,
                    ["properties"] = properties,
                },
                collections));
        }

        var memberships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string _, JsonObject payload, IReadOnlyList<string> collections) in prepared)
        {
            JsonObject created = await _api.CreateObjectAsync(_spaceId, payload, ct).ConfigureAwait(false);
            string createdId = HomeObjectJson.Id(created);
            if (collections.Count > 0 && createdId.Length == 0)
                throw new InvalidOperationException(
                    "Anytype n’a pas renvoyé l’id de l’objet créé ; son appartenance ne peut pas être appliquée.");

            foreach (string collectionId in collections)
            {
                if (!memberships.TryGetValue(collectionId, out List<string>? objectIds))
                    memberships[collectionId] = objectIds = [];
                objectIds.Add(createdId);
            }
        }

        await collectionWriter.AddAsync(memberships, ct).ConfigureAwait(false);

        DeckleHomeSource.Log.GestureCompleted("create", Elapsed(started));
        return "Créé :\n" + string.Join("\n", prepared.Select(item => $"- {type} · {item.Display}"));
    }

    public async Task<string> UpdateAsync(
        IReadOnlyList<HomeUpdateItem> items,
        CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        ValidateBatch(items, "update");
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("home_update", "batch", ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var writer = new HomePropertyWriter(_api, _spaceId, schema, index);
        var collectionWriter = new HomeCollectionWriter(_api, _spaceId, index);
        var prepared = new List<(
            string Id,
            string Display,
            JsonObject Payload,
            IReadOnlyList<string> AddToCollections,
            IReadOnlyList<string> RemoveFromCollections)>();
        var targets = new HashSet<string>(StringComparer.Ordinal);

        foreach (HomeUpdateItem item in items)
        {
            JsonObject target = index.Resolve(item.Object);
            string id = HomeObjectJson.Id(target);
            if (!targets.Add(id))
                throw new InvalidOperationException($"Le lot cible deux fois « {HomeObjectIndex.Display(target)} ».");

            string type = HomeObjectJson.TypeKey(target);
            bool element = IsElement(type);
            if (element && item.Name is not null)
                throw new InvalidOperationException(
                    $"Le titre de {HomeObjectIndex.Display(target)} est son code immuable ; modifie « Libellé ».");
            if (type == HomeSchema.Types.Idea && item.Name is not null)
                throw new InvalidOperationException(
                    "Le titre d'une idée est la première ligne de son corps ; édite le texte dans l'app.");
            if (item.Name is not null && string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Le nom ne peut pas être vide.", nameof(items));

            RefuseCodeProperty(item.Properties);
            IReadOnlyCollection<string> reserved = element
                ? [HomeSchema.Properties.Room, HomeSchema.Properties.Category]
                : [];
            JsonArray properties = await writer.BuildAsync(type, item.Properties, reserved, ct)
                .ConfigureAwait(false);
            IReadOnlyList<string> addToCollections = collectionWriter.Resolve(item.AddToCollections);
            IReadOnlyList<string> removeFromCollections = collectionWriter.Resolve(item.RemoveFromCollections);
            string? conflictingCollection = addToCollections.Intersect(
                removeFromCollections, StringComparer.Ordinal).FirstOrDefault();
            if (conflictingCollection is not null)
                throw new InvalidOperationException(
                    "Une même collection ne peut pas être ajoutée et retirée dans la même mise à jour.");

            if (item.Name is null && properties.Count == 0
                && addToCollections.Count == 0 && removeFromCollections.Count == 0)
                throw new ArgumentException(
                    $"Rien à mettre à jour pour « {item.Object} » : fournis name, properties "
                    + "ou un changement de collections.", nameof(items));

            var payload = new JsonObject();
            if (item.Name is not null)
                payload["name"] = IsFreeTitled(type)
                    ? item.Name.Trim()
                    : HumanTitle(HomeObjectJson.Code(target), item.Name);
            if (properties.Count > 0) payload["properties"] = properties;
            prepared.Add((
                id,
                HomeObjectIndex.Display(target),
                payload,
                addToCollections,
                removeFromCollections));
        }

        foreach ((
            string id,
            string _,
            JsonObject payload,
            IReadOnlyList<string> addToCollections,
            IReadOnlyList<string> removeFromCollections) in prepared)
        {
            if (payload.Count > 0)
                await _api.UpdateObjectAsync(_spaceId, id, payload, ct).ConfigureAwait(false);
            await collectionWriter.AddAsync(addToCollections, id, ct).ConfigureAwait(false);
            await collectionWriter.RemoveAsync(removeFromCollections, id, ct).ConfigureAwait(false);
        }

        DeckleHomeSource.Log.GestureCompleted("update", Elapsed(started));
        return "Mis à jour :\n" + string.Join("\n", prepared.Select(item => "- " + item.Display));
    }

    public async Task<string> GetAsync(string selector, CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        JsonObject value = index.Resolve(selector);
        DeckleHomeSource.Log.GestureCompleted("get", Elapsed(started));
        return index.Render(value);
    }

    public async Task<string> SearchAsync(HomeSearchFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);

        string? type = filter.Type is null ? null : NormalizeType(filter.Type);
        string? category = filter.Category?.Trim().ToUpperInvariant();
        if (category is not null) HomeCategories.TypeFor(category);
        string? existence = NormalizeVocabulary(HomeSchema.Properties.Existence, filter.Existence);
        string? condition = NormalizeVocabulary(HomeSchema.Properties.Condition, filter.Condition);
        string? status = NormalizeVocabulary(HomeSchema.Properties.Status, filter.Status);
        string? worksiteId = filter.Worksite is null
            ? null
            : HomeObjectJson.Id(index.Resolve(filter.Worksite, [HomeSchema.Types.Worksite]));
        string? roomId = filter.Room is null
            ? null
            : HomeObjectJson.Id(index.Resolve(filter.Room, [HomeSchema.Types.Room]));
        string? circuitId = filter.Circuit is null
            ? null
            : HomeObjectJson.Id(index.Resolve(filter.Circuit, [HomeSchema.Types.Circuit]));

        IEnumerable<JsonObject> query = index.Objects;
        if (type is not null) query = query.Where(value => HomeObjectJson.TypeKey(value) == type);
        if (roomId is not null)
            query = query.Where(value => HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.Room).Contains(roomId));
        if (circuitId is not null)
            query = query.Where(value => HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.Circuit).Contains(circuitId));
        if (category is not null)
            query = query.Where(value => SelectMatches(value, HomeSchema.Properties.Category, category));
        if (existence is not null)
            query = query.Where(value => SelectMatches(value, HomeSchema.Properties.Existence, existence));
        if (condition is not null)
            query = query.Where(value => SelectMatches(value, HomeSchema.Properties.Condition, condition));
        if (status is not null)
            query = query.Where(value => SelectMatches(value, HomeSchema.Properties.Status, status));
        if (worksiteId is not null)
            query = query.Where(value => HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.Worksite).Contains(worksiteId));
        if (filter.Done is bool done)
            query = query.Where(value => CheckboxValue(value, "done") == done);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string text = filter.Text.Trim();
            query = query.Where(value => SearchText(value, index).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        JsonObject[] matches = query.ToArray();
        DeckleHomeSource.Log.GestureCompleted("search", Elapsed(started));
        if (matches.Length == 0) return "Aucun résultat.";
        return string.Join("\n", matches.Select(value =>
            $"{HomeObjectJson.TypeKey(value)} · {HomeObjectIndex.Display(value)} · {HomeObjectJson.Id(value)}"));
    }

    public async Task<string> DeleteAsync(
        string selector,
        bool confirm,
        CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);

        if (!confirm)
        {
            HomeObjectIndex previewIndex = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
            JsonObject preview = previewIndex.Resolve(selector);
            RefuseElementDelete(preview);
            DeckleHomeSource.Log.GestureCompleted("delete_preview", Elapsed(started));
            return $"Suppression à confirmer : {HomeObjectIndex.Display(preview)} ({HomeObjectJson.TypeKey(preview)}) · id {HomeObjectJson.Id(preview)}. Relance delete avec cet id exact et confirm:true.";
        }

        using var writeScope = await _api.AcquireWriteScopeAsync("home_delete", "object", ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        JsonObject target = index.Resolve(selector);
        RefuseElementDelete(target);
        string id = HomeObjectJson.Id(target);
        if (!string.Equals(selector.Trim(), id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "La confirmation doit reprendre l’id exact fourni par la prévisualisation.");

        JsonObject[] references = index.Objects.Where(value =>
                HomeObjectJson.Id(value) != id
                && HomeObjectJson.ObjectReferences(value).Contains(id))
            .ToArray();
        if (references.Length > 0)
            throw new InvalidOperationException(
                $"Suppression refusée : {HomeObjectIndex.Display(target)} est encore référencé par "
                + string.Join(", ", references.Select(HomeObjectIndex.Display)) + ".");

        await _api.DeleteObjectAsync(_spaceId, id, ct).ConfigureAwait(false);
        DeckleHomeSource.Log.GestureCompleted("delete", Elapsed(started));
        return $"Mis à la corbeille : {HomeObjectIndex.Display(target)}.";
    }

    // Typed pilotage verbs. Creation stays deliberately loose (a name
    // suffices, properties land when known) and a task may live without a
    // chantier: the chantier is for real works, not every chore.
    public Task<string> CreateWorksiteAsync(
        string name,
        JsonObject? properties,
        IReadOnlyList<string>? collections,
        CancellationToken ct = default) =>
        CreateAsync(
            HomeSchema.Types.Worksite,
            [new HomeCreateItem(null, name, properties, collections)],
            ct);

    public Task<string> CreateTaskAsync(
        string name,
        string? worksite,
        JsonObject? properties,
        CancellationToken ct = default)
    {
        if (worksite is not null)
        {
            properties = properties is null ? [] : (JsonObject)properties.DeepClone();
            if (properties.Any(pair =>
                    string.Equals(pair.Key, HomeSchema.Properties.Worksite, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Key, "Chantier", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    "Le chantier est déjà fourni en propriété ; ne le passe qu'une fois.", nameof(worksite));
            properties[HomeSchema.Properties.Worksite] = worksite;
        }
        return CreateAsync(
            HomeSchema.Types.Task,
            [new HomeCreateItem(null, name, properties, null)],
            ct);
    }

    // Completion is the record: done tasks ARE the history of a chantier, so
    // there is no separate intervention journal. done is Anytype's native
    // action-layout checkbox, outside the Home property contract — its entry
    // is built directly rather than through the writer.
    public async Task<string> CompleteAsync(string selector, CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("home_complete", "object", ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        JsonObject target = index.Resolve(selector);
        string type = HomeObjectJson.TypeKey(target);
        string id = HomeObjectJson.Id(target);

        if (type is HomeSchema.Types.Task or HomeSchema.Types.Errand)
        {
            var payload = new JsonObject
            {
                ["properties"] = new JsonArray(
                    new JsonObject { ["key"] = "done", ["checkbox"] = true }),
            };
            await _api.UpdateObjectAsync(_spaceId, id, payload, ct).ConfigureAwait(false);
            DeckleHomeSource.Log.GestureCompleted("complete", Elapsed(started));
            return $"Terminé : {HomeObjectIndex.Display(target)}.";
        }

        if (type == HomeSchema.Types.Worksite)
        {
            var writer = new HomePropertyWriter(_api, _spaceId, schema, index);
            JsonObject entry = await writer.BuildEntryAsync(
                schema.Property(HomeSchema.Properties.Status),
                JsonValue.Create(HomeSchema.Status.Done),
                ct).ConfigureAwait(false);
            await _api.UpdateObjectAsync(
                _spaceId, id, new JsonObject { ["properties"] = new JsonArray(entry) }, ct).ConfigureAwait(false);
            int open = TasksOf(index, id).Count(task => !CheckboxValue(task, "done"));
            DeckleHomeSource.Log.GestureCompleted("complete", Elapsed(started));
            return $"Chantier terminé : {HomeObjectIndex.Display(target)}."
                + (open > 0 ? $" Attention : {open} tâche(s) encore ouverte(s)." : "");
        }

        throw new InvalidOperationException(
            $"« {HomeObjectIndex.Display(target)} » ({type}) ne se termine pas : "
            + "complete s'applique aux tâches, aux courses et aux chantiers.");
    }

    public async Task<string> WorksiteOverviewAsync(string selector, CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        await _runtime.GetAsync(ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        JsonObject worksite = index.Resolve(selector, [HomeSchema.Types.Worksite]);
        string id = HomeObjectJson.Id(worksite);

        JsonObject[] tasks = TasksOf(index, id).ToArray();
        JsonObject[] open = tasks.Where(task => !CheckboxValue(task, "done")).ToArray();
        JsonObject[] done = tasks.Where(task => CheckboxValue(task, "done")).ToArray();

        var builder = new StringBuilder(index.Render(worksite));
        if (tasks.Length == 0)
        {
            builder.Append("\n\nAucune tâche.");
        }
        else
        {
            builder.Append($"\n\nTâches ouvertes ({open.Length}) :");
            if (open.Length == 0) builder.Append(" aucune.");
            foreach (JsonObject task in open) builder.Append('\n').Append(TaskLine(task, index));
            builder.Append($"\n\nTâches terminées ({done.Length}) :");
            if (done.Length == 0) builder.Append(" aucune.");
            foreach (JsonObject task in done) builder.Append('\n').Append(TaskLine(task, index));
        }

        DeckleHomeSource.Log.GestureCompleted("chantier_overview", Elapsed(started));
        return builder.ToString();
    }

    private static IEnumerable<JsonObject> TasksOf(HomeObjectIndex index, string worksiteId) =>
        index.Objects.Where(value =>
            HomeObjectJson.TypeKey(value) == HomeSchema.Types.Task
            && HomeObjectJson.ObjectReferences(value, HomeSchema.Properties.Worksite).Contains(worksiteId));

    private static string TaskLine(JsonObject task, HomeObjectIndex index)
    {
        var parts = new List<string> { HomeObjectIndex.Display(task) };
        JsonObject? status = HomeObjectJson.Property(task, HomeSchema.Properties.Status);
        if (status is not null)
        {
            string rendered = HomeObjectJson.Render(status, index.DisplayForId);
            if (rendered.Length > 0) parts.Add(rendered);
        }
        JsonObject? due = HomeObjectJson.Property(task, HomeSchema.Properties.TargetDate);
        if (due is not null)
        {
            string rendered = HomeObjectJson.Render(due, index.DisplayForId);
            if (rendered.Length > 0) parts.Add("cible " + rendered);
        }
        return "- " + string.Join(" · ", parts);
    }

    private static string NormalizeType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le type Home ne peut pas être vide.", nameof(value));
        value = value.Trim().ToLowerInvariant();
        if (!HomeSchema.CreatableTypes.Contains(value))
            throw new ArgumentException(
                $"Type Home inconnu « {value} ». Types admis : {string.Join(", ", HomeSchema.CreatableTypes)}.",
                nameof(value));
        return value;
    }

    private static void ValidateBatch<T>(IReadOnlyList<T> items, string operation)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException($"{operation} exige au moins une entrée.", nameof(items));
        if (items.Count > MaxBatchSize)
            throw new ArgumentException($"{operation} accepte au maximum {MaxBatchSize} entrées.", nameof(items));
    }

    private static string ValidateCode(string type, string value)
    {
        if (IsElement(type)) return HomeElementCode.Parse(value).Value;
        if (type == HomeSchema.Types.Room) return HomeElementCode.ValidateRoomCode(value);

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le code ne peut pas être vide.", nameof(value));
        value = value.Trim().ToUpperInvariant();
        Regex pattern = type == HomeSchema.Types.Circuit ? CircuitCodePattern : GenericCodePattern;
        if (!pattern.IsMatch(value))
            throw new ArgumentException($"Code invalide « {value} » pour le type {type}.", nameof(value));
        return value;
    }

    private static bool IsElement(string type) => HomeSchema.ElementTypes.Contains(type);

    // Life and work types share the same free-titled shape: no code, a plain
    // name (or a body-derived one for ideas), none of the element invariants.
    private static bool IsFreeTitled(string type) =>
        HomeSchema.LifeTypes.Contains(type) || HomeSchema.WorkTypes.Contains(type);

    private static string RequireCode(string type, string? code) =>
        code ?? throw new ArgumentException(
            $"Le type {type} exige un code normatif.", nameof(code));

    // A life object carries no code: a course or outil is titled by its free
    // name, an idée by the first line of its text (the dev-space capture shape:
    // short text becomes the whole title, long text keeps its head as title and
    // the full text as body).
    private static (string Display, JsonObject Payload, IReadOnlyList<string> Collections) PrepareLifeItem(
        string type, HomeCreateItem item, JsonArray properties, HomeCollectionWriter collectionWriter)
    {
        if (item.Code is not null)
            throw new InvalidOperationException(
                $"Le type {type} ne porte pas de code : son titre est libre.");

        string? name = item.Name?.Trim();
        string? text = item.Text?.Trim();
        var payload = new JsonObject { ["type_key"] = type };

        if (type == HomeSchema.Types.Idea)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Une idée est son texte : fournis « text ».", nameof(item));
            if (name is not null)
                throw new InvalidOperationException(
                    "Une idée n'a pas de titre : sa première ligne en tient lieu.");
            bool isShort = text.Length <= 80 && !text.Contains('\n');
            payload["name"] = isShort ? text : FirstWords(text, 80);
            if (!isShort) payload["body"] = text;
        }
        else
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"Un objet {type} exige un nom.", nameof(item));
            if (text is not null && type != HomeSchema.Types.Tool)
                throw new InvalidOperationException(
                    $"Un objet {type} n'a pas de corps : utilise la propriété « Notes ».");
            payload["name"] = name;
            if (!string.IsNullOrEmpty(text)) payload["body"] = text;
        }

        if (properties.Count > 0) payload["properties"] = properties;
        return (payload["name"]!.GetValue<string>(), payload, collectionWriter.Resolve(item.Collections));
    }

    private static string FirstWords(string content, int maxLength)
    {
        string firstLine = content.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= maxLength) return firstLine;
        int cut = firstLine.LastIndexOf(' ', maxLength);
        return (cut > 0 ? firstLine[..cut] : firstLine[..maxLength]).TrimEnd() + "…";
    }

    private static bool CheckboxValue(JsonObject value, string propertyKey)
    {
        JsonNode? checkbox = HomeObjectJson.Property(value, propertyKey)?["checkbox"];
        return checkbox is JsonValue scalar && scalar.TryGetValue<bool>(out bool state) && state;
    }

    private static string HumanTitle(string code, string? name)
    {
        string? label = name?.Trim();
        return string.IsNullOrEmpty(label) ? code : $"{code} — {label}";
    }

    private static void RefuseCodeProperty(JsonObject? properties)
    {
        if (properties is null) return;
        if (properties.Any(pair => string.Equals(pair.Key, "code", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Le code est porté par le titre et reste immuable ; il ne peut pas être fourni comme propriété.");
    }

    private static void RefuseElementDelete(JsonObject value)
    {
        if (IsElement(HomeObjectJson.TypeKey(value)))
            throw new InvalidOperationException(
                $"Un élément ne se supprime pas. Passe {HomeObjectIndex.Display(value)} à Existence = Déposé avec update.");
    }

    private static string NextCodeSuggestion(
        string type,
        string code,
        IReadOnlyList<JsonObject> existing,
        IReadOnlyCollection<string> batch)
    {
        if (!IsElement(type)) return "un autre code unique";
        HomeElementCode parsed = HomeElementCode.Parse(code);
        var used = new HashSet<int>();
        foreach (string candidate in existing.Select(HomeObjectJson.Code).Concat(batch))
        {
            try
            {
                HomeElementCode other = HomeElementCode.Parse(candidate);
                if (other.Room == parsed.Room && other.Category == parsed.Category) used.Add(other.Sequence);
            }
            catch (ArgumentException) { }
        }
        for (int sequence = 1; sequence <= 99; sequence++)
            if (!used.Contains(sequence)) return $"{parsed.Room}-{parsed.Category}{sequence:00}";
        return "aucun (séquence 01–99 épuisée)";
    }

    private static string? NormalizeVocabulary(string propertyKey, string? value)
    {
        if (value is null) return null;
        IReadOnlyDictionary<string, string> vocabulary = HomeSchema.ClosedVocabularies[propertyKey];
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
        JsonNode? select = HomeObjectJson.Property(value, propertyKey)?["select"];
        if (select is null) return false;
        string actual = select switch
        {
            JsonValue scalar when scalar.TryGetValue<string>(out string? text) => text ?? "",
            JsonObject obj => HomeObjectJson.String(obj, "key") is { Length: > 0 } key
                ? key
                : HomeObjectJson.String(obj, "name"),
            _ => "",
        };
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (HomeSchema.ClosedVocabularies.TryGetValue(propertyKey, out var vocabulary)
            && vocabulary.TryGetValue(expected, out string? name))
            return string.Equals(actual, name, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static string SearchText(JsonObject value, HomeObjectIndex index)
    {
        var builder = new StringBuilder(HomeObjectIndex.Display(value));
        if (value["properties"] is JsonArray properties)
            foreach (JsonNode? node in properties)
                if (node is JsonObject property)
                    builder.Append(' ').Append(HomeObjectJson.Render(property, index.DisplayForId));
        return builder.ToString();
    }

    private static double Elapsed(DateTime started) => (DateTime.UtcNow - started).TotalMilliseconds;
}
