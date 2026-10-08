using System.Text.Json.Nodes;

namespace Deckle.Home;

// Resolves relation deltas against the detailed current object while the caller
// holds the write scope. The ordinary writer still validates every target.
internal static class HomeRelationEdits
{
    public static bool HasEdits(JsonObject? properties) => properties?.Any(pair => pair.Value is JsonObject) == true;

    public static async Task<JsonObject> ApplyAsync(string type, JsonObject values, JsonObject current,
        HomeSchemaRuntime schema, HomePropertyWriter writer, CancellationToken ct)
    {
        var result = (JsonObject)values.DeepClone();
        foreach ((string name, JsonNode? value) in values)
        {
            if (value is not JsonObject edit) continue;
            var property = schema.ResolveProperty(type, name);
            if (property.Format != "objects")
                throw new ArgumentException($"« {name} » n'est pas une relation d'objets.");
            if (edit.Count == 0 || edit.Any(pair => pair.Key is not "add" and not "remove"))
                throw new ArgumentException($"« {name} » accepte add et/ou remove.");
            foreach ((string operation, JsonNode? selectors) in edit)
                if (selectors is not JsonArray array || array.Any(item =>
                        item is not JsonValue scalar || !scalar.TryGetValue<string>(out _)))
                    throw new ArgumentException($"« {name}.{operation} » attend une liste de sélecteurs.");

            JsonObject add = await writer.BuildEntryAsync(property, edit["add"], ct).ConfigureAwait(false);
            JsonObject remove = await writer.BuildEntryAsync(property, edit["remove"], ct).ConfigureAwait(false);
            string[] additions = add["objects"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            string[] removals = remove["objects"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            if (additions.Intersect(removals, StringComparer.Ordinal).Any())
                throw new ArgumentException($"« {name} » ne peut pas ajouter et retirer le même objet.");
            string[] updated = HomeObjectJson.ObjectReferences(current, property.Key)
                .Except(removals, StringComparer.Ordinal).Concat(additions).Distinct(StringComparer.Ordinal).ToArray();
            result[name] = new JsonArray(updated.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }
        return result;
    }
}
