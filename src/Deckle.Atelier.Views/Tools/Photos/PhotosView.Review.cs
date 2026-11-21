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

    // The panel's "Describe this photo" and "Stop", on the photo selected.
    internal PhotoFile? SelectedPhotoForReview => SelectedTiles() is [var one] ? one.Photo : null;

    internal PhotoDescriptions? DescriptionsForReview => _descriptions;

    // "Stop describing the folder" beside the folder's progress bar.
    internal void StopFolderForReview() => _run?.Stop();

    // The folder's run as the view follows it: null once its end is shown.
    internal PhotoDescriptionRun? CurrentRunForReview => _place is { } place ? _descriptions?.RunOf(place.Path) : null;
}