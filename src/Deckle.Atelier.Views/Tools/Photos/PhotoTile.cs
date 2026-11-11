using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// One photo of the gallery. Built off the UI thread when the folder is read,
// with the mark the store held then; from there the tile is what the gallery
// shows, and the store follows it in the background (PhotoMarkQueue).
//
// The picture comes later and may never come: the tile shows its placeholder
// until the icon seam hands one back. It is asked for when the gallery
// realizes the tile's container and dropped when the container is recycled,
// so a folder of thousands of photos holds only the pictures on screen.
internal sealed class PhotoTile : INotifyPropertyChanged
{
    private PhotoMark _mark;
    private ImageSource? _thumbnail;
    private CancellationTokenSource? _loading;

    public PhotoTile(PhotoFile photo, PhotoMark mark)
    {
        Photo = photo;
        _mark = mark;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PhotoFile Photo { get; }
    public string Name => Photo.Name;

    public PhotoMark Mark
    {
        get => _mark;
        set
        {
            if (_mark == value) return;
            _mark = value;
            Changed(nameof(Mark));
            Changed(nameof(MarkVisibility));
            Changed(nameof(MarkGlyph));
            Changed(nameof(AccessibleName));
        }
    }

    public Visibility MarkVisibility => _mark == PhotoMark.None ? Visibility.Collapsed : Visibility.Visible;

    public string MarkGlyph => _mark == PhotoMark.Delete ? PhotoGlyphs.Delete : PhotoGlyphs.Keep;

    // What a screen reader says for the tile: its name, then its mark.
    public string AccessibleName => _mark switch
    {
        PhotoMark.Keep => AtelierText.Format("Photos_Tile_Keep_Format", Name),
        PhotoMark.Delete => AtelierText.Format("Photos_Tile_Delete_Format", Name),
        _ => Name,
    };

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            _thumbnail = value;
            Changed(nameof(Thumbnail));
            Changed(nameof(PlaceholderVisibility));
        }
    }

    public Visibility PlaceholderVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    // Called on the UI thread when the tile's container is realized. A null
    // answer keeps the placeholder; a failure is the seam's to log.
    public async Task LoadThumbnailAsync(IFileIconProvider icons, int pixelSize)
    {
        if (_thumbnail is not null) return;
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        try
        {
            ImageSource? image = await icons.GetThumbnailAsync(Photo.Path, pixelSize, loading.Token);
            if (!loading.IsCancellationRequested && image is not null) Thumbnail = image;
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The container went back to the recycle queue: the picture is let go.
    public void ReleaseThumbnail()
    {
        _loading?.Cancel();
        _loading = null;
        if (_thumbnail is not null) Thumbnail = null;
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
