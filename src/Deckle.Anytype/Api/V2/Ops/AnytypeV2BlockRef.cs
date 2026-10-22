using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// How an op names the block it acts on: by id (full, or a unique suffix as the
// compact reads serve them) or by its exact text, markup included. The schemas
// say "give id or match, never both"; two factories and a private constructor
// make the invalid pair impossible to write.
public sealed record AnytypeV2BlockRef
{
    private readonly string? _id;
    private readonly string? _match;

    private AnytypeV2BlockRef(string? id, string? match)
    {
        _id = id;
        _match = match;
    }

    public static AnytypeV2BlockRef ById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new(id, null);
    }

    // The text must match exactly one block; the server refuses several and
    // lists the candidate ids.
    public static AnytypeV2BlockRef ByText(string match)
    {
        ArgumentException.ThrowIfNullOrEmpty(match);
        return new(null, match);
    }

    internal void WriteTo(JsonObject op)
    {
        if (_id is not null) op["id"] = _id;
        else op["match"] = _match;
    }
}
