namespace Deckle.Projects.Views;

// What the self-check drives to photograph Home in each of its states. Each
// step takes the path a click takes.
internal sealed partial class HomePage
{
    public bool IsEmptyStateShown => EmptyState.Visibility == Microsoft.UI.Xaml.Visibility.Visible;

    public bool HasFold => _fold is not null;

    // Opens or closes the "Without a category" fold, as its header does.
    public void SetFoldOpenForReview(bool open)
    {
        if (_fold is not null) _fold.IsOpen = open;
    }

    // The card of a project by the name it shows, or null when it is not drawn
    // (in a closed fold, or past the three rows).
    public ProjectCard? CardForReview(string name)
    {
        string? id = _context.Store.Projects.FirstOrDefault(p => p.DisplayName == name)?.Id;
        if (id is null) return null;
        foreach (CategorySection section in _categorySections)
            if (section.Cards.CardOf(id) is { } card) return card;
        return _fold?.Cards.CardOf(id);
    }

    // The columns of the first category's grid, so a check can compare them with
    // what CardGridCapacity says for the width.
    public int FirstGridColumnsForReview => _categorySections.FirstOrDefault()?.Cards.ColumnsForReview ?? 0;

    public bool FirstGridShowsMoreTileForReview => _categorySections.FirstOrDefault()?.Cards.ShowsMoreTile ?? false;
}
