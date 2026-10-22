namespace Deckle.Anytype;

// Object links rendered by name, shared by the reads that show them. v2 serves
// only ids, so a name costs one properties read per link.
internal static class LinkedNames
{
    // A relation can hold many objects; each read is a request through the
    // shared 1 rps gate. Past this many, the rest is only counted.
    internal const int MaxReads = 8;

    // The names, comma-joined, or null for no link. Duplicate ids are read
    // once; links beyond MaxReads are counted as « +N ». An untitled linked
    // object reads « (sans titre) »: v1 fell back on the object's snippet,
    // which a properties-only v2 read does not carry.
    internal static async Task<string?> JoinAsync(
        AnytypeV2ObjectReader reader, IReadOnlyList<string> ids, CancellationToken ct)
    {
        string[] distinct = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0) return null;

        var names = new List<string>(Math.Min(distinct.Length, MaxReads) + 1);
        foreach (string id in distinct.Take(MaxReads))
        {
            AnytypeV2ObjectDocument linked = await reader.ReadAsync(
                id, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
            string name = AnytypeV2PropertyMap.Of(linked).Name;
            names.Add(name.Length > 0 ? name : "(sans titre)");
        }
        if (distinct.Length > MaxReads) names.Add($"+{distinct.Length - MaxReads}");
        return string.Join(", ", names);
    }
}
