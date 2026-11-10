namespace Deckle.Atelier.Views;

// The view's side of PhotosReview: the same paths a gesture takes.
internal sealed partial class PhotosView
{
    internal IReadOnlyList<PhotoTile> Tiles => _tiles;

    internal Task SetMarkForReview(IReadOnlyList<PhotoTile> tiles, PhotoMark mark) => SetMark(tiles, mark);

    internal void OpenLargeForReview(string name)
    {
        PhotoTile? tile = _tiles.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tile is not null) OpenLarge(tile);
    }

    internal void LeaveLargeForReview() => LeaveLarge(restoreFocus: true);
}
