using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The errands, as ruled on 2026-08-12: three verbs over the product type.
// need puts a product on the list, taken takes it off, list reads what is to
// buy. A product is found by its name, never guessed: a name equal once case
// and accents are set aside is the product; a name only near it (a plural, a
// punctuation mark) is refused with the candidates, because two close names
// are as likely two products as one product misspelt.
public sealed partial class HomeGestures
{
    public Task<string> ProductNeedAsync(IReadOnlyList<HomeProductNeed> items, CancellationToken ct = default) =>
        ProductErrandAsync("product_need", items, needed: true, ct);

    // Taken takes names alone: buying is the moment to learn a product, so a
    // name the space does not know is created already taken.
    public Task<string> ProductTakenAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        return ProductErrandAsync(
            "product_taken", names.Select(name => new HomeProductNeed(name)).ToArray(), needed: false, ct);
    }

    private async Task<string> ProductErrandAsync(
        string gesture, IReadOnlyList<HomeProductNeed> items, bool needed, CancellationToken ct)
    {
        DateTime started = DateTime.UtcNow;
        ValidateBatch(items, gesture);
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);

        using var writeScope = await _api.AcquireWriteScopeAsync("home_" + gesture, "batch", ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var writer = new HomePropertyWriter(_api, _spaceId, schema, index);
        JsonObject[] products = index.Objects
            .Where(value => HomeObjectJson.TypeKey(value) == HomeSchema.Types.Product).ToArray();
        string verb = needed ? "besoin" : "pris";

        // Every item is matched and its write prepared before the first one
        // goes out: one refusal leaves the list as it was.
        var plan = new List<(string Line, string? TargetId, JsonObject Payload)>();
        var refusals = new List<string>();
        int candidates = 0;
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (HomeProductNeed item in items)
        {
            string name = item.Name?.Trim() ?? "";
            if (name.Length == 0)
                throw new ArgumentException("Chaque produit se nomme : fournis name.", nameof(items));
            string near = HomeNameGuard.Fold(name);
            if (!batch.TryAdd(near, name))
            {
                refusals.Add($"« {name} » et « {batch[near]} » désignent le même produit dans ce lot : garde-n'en qu'un.");
                continue;
            }

            JsonObject[] exact = products.Where(value =>
                    string.Equals(HomeObjectJson.Id(value), name, StringComparison.Ordinal)
                    || FoldName(HomeObjectJson.Name(value)) == FoldName(name))
                .ToArray();
            if (exact.Length > 1)
            {
                candidates += exact.Length;
                refusals.Add($"« {name} » correspond à plusieurs produits ; relance avec l'id du bon :\n"
                    + string.Join("\n", exact.Select(Candidate)));
                continue;
            }

            var values = new JsonObject { [HomeSchema.Properties.Needed] = needed };
            if (item.ProductCategory is not null) values[HomeSchema.Properties.ProductCategory] = item.ProductCategory;
            if (item.Quantity is double quantity) values[HomeSchema.Properties.Quantity] = quantity;

            if (exact.Length == 1)
            {
                // A product needed again comes back from the archive, or the
                // list would hide what was just asked for.
                if (needed && CheckboxValue(exact[0], HomeSchema.Properties.Archived))
                    values[HomeSchema.Properties.Archived] = false;
                JsonArray properties = await writer.BuildAsync(HomeSchema.Types.Product, values, [], ct)
                    .ConfigureAwait(false);
                plan.Add(($"{verb} · {HomeObjectIndex.Display(exact[0])} (existant)",
                    HomeObjectJson.Id(exact[0]), new JsonObject { ["properties"] = properties }));
                continue;
            }

            JsonObject[] close = products.Where(value => HomeNameGuard.Fold(HomeObjectJson.Name(value)) == near).ToArray();
            if (close.Length > 0 && !item.Force)
            {
                candidates += close.Length;
                refusals.Add($"« {name} » ressemble à :\n" + string.Join("\n", close.Select(Candidate)));
                continue;
            }

            JsonArray created = await writer.BuildAsync(HomeSchema.Types.Product, values, [], ct).ConfigureAwait(false);
            plan.Add(($"{verb} · {name} (créé)", null, new JsonObject
            {
                ["type_key"] = HomeSchema.Types.Product,
                ["name"] = name,
                ["properties"] = created,
            }));
        }

        if (refusals.Count > 0)
        {
            DeckleHomeSource.Log.NameGuardRefused(HomeSchema.Types.Product, candidates);
            string next = needed
                ? "Relance avec le nom exact ou l'id du produit voulu ; pour un produit vraiment différent, passe force: true sur l'item."
                : "Relance avec le nom exact ou l'id du produit voulu ; un produit vraiment différent se crée avec product_create (force: true).";
            throw new InvalidOperationException(
                "Rien n'a été écrit :\n" + string.Join("\n", refusals) + "\n" + next);
        }

        // One request per item, no transaction: a failure part way is told
        // item by item (see HomePartialWriteException).
        var applied = new List<string>();
        try
        {
            foreach ((string line, string? targetId, JsonObject payload) in plan)
            {
                if (targetId is null)
                    await _api.CreateObjectAsync(_spaceId, payload, ct).ConfigureAwait(false);
                else
                    await _api.UpdateObjectAsync(_spaceId, targetId, payload, ct).ConfigureAwait(false);
                applied.Add(line);
            }
        }
        catch (Exception ex) when (applied.Count > 0 && ex is not OperationCanceledException)
        {
            throw Report(HomePartialWriteException.ForSteps(
                gesture, applied, plan[applied.Count].Line,
                plan.Skip(applied.Count + 1).Select(step => step.Line).ToArray(), ex));
        }

        DeckleHomeSource.Log.GestureCompleted(gesture, Elapsed(started));
        return string.Join("\n", plan.Select(step => step.Line));
    }

    // What is to buy: the needed products that are not archived, grouped by
    // category in the order the manifest gives its options, then the
    // categories the space grew on its own, then the products without one.
    public async Task<HomeProductList> ProductListAsync(string? productCategory = null, CancellationToken ct = default)
    {
        DateTime started = DateTime.UtcNow;
        HomeSchemaRuntime schema = await _runtime.GetAsync(ct).ConfigureAwait(false);
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var writer = new HomePropertyWriter(_api, _spaceId, schema, index);
        const string Property = HomeSchema.Properties.ProductCategory;
        IReadOnlyList<SchemaTagInfo> tags = await writer.ReadTagsAsync(schema.Property(Property), ct)
            .ConfigureAwait(false);
        IReadOnlyList<string> seeded = HomeSchema.TargetSeededVocabularies[Property];

        string? wanted = productCategory is null ? null : ResolveProductCategory(productCategory.Trim(), seeded, tags);

        var groups = new Dictionary<string, (string? Key, string Label, int Rank, List<JsonObject> Items)>(StringComparer.Ordinal);
        foreach (JsonObject product in index.Objects.Where(value =>
                     HomeObjectJson.TypeKey(value) == HomeSchema.Types.Product
                     && CheckboxValue(value, HomeSchema.Properties.Needed)
                     && !CheckboxValue(value, HomeSchema.Properties.Archived)))
        {
            (string? key, string label, int rank) = ProductCategoryOf(product, seeded, tags);
            if (wanted is not null && key != wanted) continue;
            string slot = key ?? "";
            if (!groups.TryGetValue(slot, out var group))
                groups[slot] = group = (key, label, rank, []);
            group.Items.Add(product);
        }

        var ordered = groups.Values
            .OrderBy(group => group.Rank)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int total = ordered.Sum(group => group.Items.Count);

        var text = new StringBuilder(total == 0
            ? "Rien à prendre."
            : $"À prendre : {total} produit(s).");
        var data = new JsonArray();
        foreach (var group in ordered)
        {
            text.Append("\n\n").Append(group.Label).Append(" :");
            var entries = new JsonArray();
            foreach (JsonObject product in group.Items.OrderBy(HomeObjectIndex.Display, StringComparer.OrdinalIgnoreCase))
            {
                double? quantity = PropertyNumber(product, HomeSchema.Properties.Quantity);
                text.Append("\n- ").Append(HomeObjectIndex.Display(product));
                if (quantity is double amount)
                    text.Append(" (quantité ").Append(amount.ToString("0.##", French)).Append(')');
                var entry = new JsonObject
                {
                    ["id"] = HomeObjectJson.Id(product),
                    ["name"] = HomeObjectJson.Name(product),
                };
                if (quantity is double value) entry["quantity"] = value;
                entries.Add(entry);
            }
            data.Add(new JsonObject { ["category"] = group.Key, ["label"] = group.Label, ["items"] = entries });
        }

        DeckleHomeSource.Log.GestureCompleted("product_list", Elapsed(started));
        return new HomeProductList(text.ToString(), new JsonObject { ["groups"] = data, ["total"] = total });
    }

    // Rank 0 for a manifest option (in its order), 1 for an option the space
    // grew, 2 for no category at all, so the uncategorised come last.
    private static (string? Key, string Label, int Rank) ProductCategoryOf(
        JsonObject product, IReadOnlyList<string> seeded, IReadOnlyList<SchemaTagInfo> tags)
    {
        const string Property = HomeSchema.Properties.ProductCategory;
        JsonNode? select = HomeObjectJson.Property(product, Property)?["select"];
        if (select is null) return (null, "Sans catégorie", int.MaxValue);
        for (int position = 0; position < seeded.Count; position++)
            if (SearchSelectMatches(product, Property, seeded[position], tags))
                return (seeded[position], HomeSchema.OptionLabel(Property, seeded[position]), position);

        string live = select switch
        {
            JsonObject value => HomeObjectJson.String(value, "name") is { Length: > 0 } name ? name : HomeObjectJson.String(value, "key"),
            JsonValue value when value.TryGetValue<string>(out string? id) =>
                tags.FirstOrDefault(tag => tag.Id == id || tag.Key == id)?.Name ?? id ?? "",
            _ => "",
        };
        return live.Length == 0 ? (null, "Sans catégorie", int.MaxValue) : (live, live, seeded.Count);
    }

    private static string ResolveProductCategory(
        string requested, IReadOnlyList<string> seeded, IReadOnlyList<SchemaTagInfo> tags)
    {
        const string Property = HomeSchema.Properties.ProductCategory;
        string? key = seeded.FirstOrDefault(option => HomeSchema.OptionAliases(Property, option)
            .Contains(requested, StringComparer.OrdinalIgnoreCase));
        if (key is not null) return key;
        SchemaTagInfo? live = tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag.Key, requested, StringComparison.OrdinalIgnoreCase));
        if (live is not null && live.Name.Length > 0) return live.Name;
        throw new InvalidOperationException(
            $"Catégorie de produit inconnue « {requested} ». Catégories : "
            + string.Join(", ", seeded.Select(option => HomeSchema.OptionLabel(Property, option))
                .Concat(tags.Select(tag => tag.Name)).Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            + ".");
    }

    private static string Candidate(JsonObject value) =>
        $"- {HomeObjectJson.Id(value)} · {HomeObjectIndex.Display(value)} ({HomeObjectJson.TypeKey(value)})";
}
