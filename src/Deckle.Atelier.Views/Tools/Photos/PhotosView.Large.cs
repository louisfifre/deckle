using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// A photo seen large inside the window (spec, Photos; board 12.5). It takes
// the gallery's place and keeps the panel, which then shows the photo's rank
// ("13 sur 412"); the arrow keys and the side buttons move to the neighbours,
// Escape and the button at the top right go back to the gallery, on the same
// photo. The picture is the icon seam's, asked for at the frame's size; with
// none, the frame keeps its placeholder.
internal sealed partial class PhotosView
{
    private PhotoTile? _largeTile;
    private CancellationTokenSource? _largeLoading;

    private void OpenLarge(PhotoTile tile)
    {
        _largeTile = tile;
        Gallery.Visibility = Visibility.Collapsed;
        Large.Visibility = Visibility.Visible;
        ShowLargeTile();
        LeaveLargeButton.Focus(FocusState.Programmatic);
    }

    private void MoveLarge(int step)
    {
        if (_largeTile is null) return;
        int index = _tiles.IndexOf(_largeTile) + step;
        if (index < 0 || index >= _tiles.Count) return;
        _largeTile = _tiles[index];
        ShowLargeTile();
    }

    // The gallery's selection follows the large photo, so going back lands
    // on it and the panel shows it.
    private void ShowLargeTile()
    {
        if (_largeTile is not { } tile) return;
        int index = _tiles.IndexOf(tile);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < _tiles.Count - 1;

        _filling = true;
        try
        {
            Gallery.SelectedItem = tile;
        }
        finally
        {
            _filling = false;
        }
        _ = ShowPanelAsync();
        _ = LoadLargeAsync(tile);
    }

    private async Task LoadLargeAsync(PhotoTile tile)
    {
        _largeLoading?.Cancel();
        var loading = new CancellationTokenSource();
        _largeLoading = loading;
        LargeImage.Source = null;
        LargePlaceholder.Visibility = Visibility.Visible;

        double longer = Math.Max(LargeFrame.ActualWidth, LargeFrame.ActualHeight);
        if (longer <= 0) longer = 960;
        try
        {
            var image = await _host.Icons.GetThumbnailAsync(tile.Photo.Path, PixelSize(longer), loading.Token);
            if (loading.IsCancellationRequested || image is null) return;
            LargeImage.Source = image;
            LargePlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LeaveLarge(bool restoreFocus)
    {
        if (_largeTile is not { } tile) return;
        _largeTile = null;
        _largeLoading?.Cancel();
        LargeImage.Source = null;
        Large.Visibility = Visibility.Collapsed;
        Gallery.Visibility = Visibility.Visible;
        if (!restoreFocus) return;

        Gallery.ScrollIntoView(tile);
        _ = ShowPanelAsync();
        Gallery.UpdateLayout();
        (Gallery.ContainerFromItem(tile) as GridViewItem)?.Focus(FocusState.Programmatic);
    }

    // The placeholder keeps the 3:2 of the tiles, as large as the frame allows.
    private void OnLargeFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Min(e.NewSize.Width, e.NewSize.Height * 3 / 2);
        LargePlaceholder.Width = Math.Max(0, width);
        LargePlaceholder.Height = Math.Max(0, width * 2 / 3);
    }

    private void OnLeaveLargeClick(object sender, RoutedEventArgs e) => LeaveLarge(restoreFocus: true);

    private void OnPreviousClick(object sender, RoutedEventArgs e) => MoveLarge(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => MoveLarge(+1);
}
