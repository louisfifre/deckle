namespace Deckle.Anytype;

// The halves of an object a read can ask for. Both is the server default.
[Flags]
public enum AnytypeV2ObjectParts
{
    Properties = 1,
    Blocks = 2,
}

// What a GET on an object returns, as the served read options allow:
//
//   • Outline — every block's {indent, id, type} plus its text cut to 80 runes,
//     instead of the blocks. The cheap way to find where to edit. A checkbox
//     entry does not say whether it is ticked.
//   • Block — only that block's subtree. Mutually exclusive with Outline; the
//     server answers 400, so the combination is refused before it is sent.
//   • Parts — properties, blocks or both (null, the default).
//   • FullIds — the export shape, with full ids everywhere. The default
//     compact shape relabels minted block ids to short suffixes, which the
//     write ops accept back.
//   • Markdown — the markdown rendering instead of the AnyBlock document.
public sealed record AnytypeV2ObjectReadOptions
{
    public bool Outline { get; init; }

    public string? Block { get; init; }

    public AnytypeV2ObjectParts? Parts { get; init; }

    public bool FullIds { get; init; }

    public bool Markdown { get; init; }

    internal IReadOnlyList<KeyValuePair<string, string>> ToQuery()
    {
        if (Outline && Block is not null)
            throw new ArgumentException("Outline and Block are mutually exclusive: ask for the outline or one subtree.");

        var query = new List<KeyValuePair<string, string>>();
        if (Outline) query.Add(new("outline", "true"));
        if (Block is not null) query.Add(new("block", Block));
        if (Parts is { } parts)
        {
            var names = new List<string>();
            if (parts.HasFlag(AnytypeV2ObjectParts.Properties)) names.Add("properties");
            if (parts.HasFlag(AnytypeV2ObjectParts.Blocks)) names.Add("blocks");
            if (names.Count > 0) query.Add(new("include", string.Join(",", names)));
        }
        if (FullIds) query.Add(new("ids", "full"));
        if (Markdown) query.Add(new("format", "md"));
        return query;
    }
}
