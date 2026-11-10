namespace Deckle.Atelier;

// The boxes above the map's list (12.8b, 12.8c): "Toujours lu par un LLM"
// keeps only what every session loads, and one box per kind keeps or hides
// that kind. With every box at its default, every item is kept.
public sealed record MapListFilter(bool AlwaysReadOnly, IReadOnlySet<MapItemKind> HiddenKinds)
{
    public static MapListFilter All { get; } = new(false, new HashSet<MapItemKind>());

    public bool Keeps(MapItem item) =>
        (!AlwaysReadOnly || item.IsAlwaysRead) && !HiddenKinds.Contains(item.Kind);

    public IReadOnlyList<MapItem> Apply(IEnumerable<MapItem> items) => [.. items.Where(Keeps)];

    // The total row under the list (12.8c): the tokens of the rows shown, so
    // the figure always matches what the person reads above it.
    public static int EstimatedTokensOf(IEnumerable<MapItem> shown) => shown.Sum(i => i.EstimatedTokens ?? 0);
}
