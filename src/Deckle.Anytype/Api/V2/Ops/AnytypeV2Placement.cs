using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Where an op puts something: after or before a sibling, inside a container, or
// at an end. The schemas allow at most one of after/before/inside, and
// "position" (first|last) only beside inside or alone, never beside after or
// before. The factories below are the whole set of legal combinations.
//
// The same shape places a block, a view or a type property; only blocks have
// a container to go inside, so the view and property ops refuse Inside.
public sealed record AnytypeV2Placement
{
    private readonly string? _after;
    private readonly string? _before;
    private readonly string? _inside;
    private readonly string? _position;

    private AnytypeV2Placement(string? after, string? before, string? inside, string? position)
    {
        _after = after;
        _before = before;
        _inside = inside;
        _position = position;
    }

    // At the start of the document, view list or property section.
    public static AnytypeV2Placement Start { get; } = new(null, null, null, "first");

    // At the end. This is also what an omitted placement means.
    public static AnytypeV2Placement End { get; } = new(null, null, null, "last");

    public static AnytypeV2Placement After(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new(reference, null, null, null);
    }

    public static AnytypeV2Placement Before(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new(null, reference, null, null);
    }

    // As children of a block, at its first or last child slot.
    public static AnytypeV2Placement Inside(string container, bool atStart = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        return new(null, null, container, atStart ? "first" : null);
    }

    // True for Start and End: no block is named, the whole document is the
    // target. move_block needs a named block to move relative to.
    internal bool IsDocumentLevel => _after is null && _before is null && _inside is null;

    internal bool IsInside => _inside is not null;

    internal void WriteTo(JsonObject op)
    {
        if (_after is not null) op["after"] = _after;
        if (_before is not null) op["before"] = _before;
        if (_inside is not null) op["inside"] = _inside;
        if (_position is not null) op["position"] = _position;
    }
}
