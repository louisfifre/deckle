using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// One item of the file tool, the same object in its three displays, its texts
// formatted once when the folder is read. What comes from the shell comes
// later and may never come (IFileIconProvider): the item shows a neutral
// glyph until its type's icon is handed back, and in the gallery its type's
// icon until the picture of its content is, or for a text file the beginning
// of its text (TextPreviewReader). Each is asked for when a display
// realizes the item's container, at the size that display draws it, so a
// folder of thousands of items asks only for those on screen; the picture is
// let go when the container is recycled, the icons, shared per type by the
// provider's cache, are kept.
internal sealed class FileRow : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private ImageSource? _largeIcon;
    private ImageSource? _tileIcon;
    private ImageSource? _thumbnail;
    private string? _previewText;
    private CancellationTokenSource? _loading;
    private FileListDisplay _loadingDisplay;

    public FileRow(FolderEntry entry)
    {
        Entry = entry;
        Glyph = entry.IsFolder ? FoldersGlyphs.Folder : FoldersGlyphs.File;
        Modified = EntryFormat.Date(entry.Modified, FoldersText.Culture);
        Size = entry.Size is long bytes ? EntryFormat.Size(bytes, FoldersText.Culture) : "";
        Summary = EntryFormat.Summary(
            FileDetails.Kind(entry), entry.Size, entry.Modified, FoldersText.Get("Files_Summary_Separator"), FoldersText.Culture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FolderEntry Entry { get; }
    public string Name => Entry.Name;
    public string Glyph { get; }
    public string Modified { get; }
    public string Size { get; }

    // Type, size and date under the name, in the large rows (12.16).
    public string Summary { get; }

    // The list's 16-pixel icon.
    public ImageSource? Icon
    {
        get => _icon;
        private set => Set(ref _icon, value, nameof(Icon), nameof(GlyphVisibility));
    }

    public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    // The large rows' 32-pixel icon.
    public ImageSource? LargeIcon
    {
        get => _largeIcon;
        private set => Set(ref _largeIcon, value, nameof(LargeIcon), nameof(LargeGlyphVisibility));
    }

    public Visibility LargeGlyphVisibility => _largeIcon is null ? Visibility.Visible : Visibility.Collapsed;

    // The gallery's type icon, shown while there is no picture of the
    // content, and kept when there never is one.
    public ImageSource? TileIcon
    {
        get => _tileIcon;
        private set => Set(ref _tileIcon, value, nameof(TileIcon), nameof(TileGlyphVisibility));
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => Set(ref _thumbnail, value, nameof(Thumbnail), nameof(TileGlyphVisibility), nameof(TileIconVisibility));
    }

    // The beginning of a Markdown or plain-text file, shown on its tile in
    // place of a picture (TextPreviewReader).
    public string? PreviewText
    {
        get => _previewText;
        private set
        {
            if (_previewText == value) return;
            _previewText = value;
            Notify(nameof(PreviewText), nameof(PreviewTextVisibility), nameof(TileGlyphVisibility), nameof(TileIconVisibility));
        }
    }

    public Visibility PreviewTextVisibility => _previewText is null ? Visibility.Collapsed : Visibility.Visible;

    private bool HasPreview => _thumbnail is not null || _previewText is not null;

    public Visibility TileGlyphVisibility => _tileIcon is null && !HasPreview ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TileIconVisibility => HasPreview ? Visibility.Collapsed : Visibility.Visible;

    // Called on the UI thread when a display realizes the item's container.
    // pixelSize is the display's image cell in physical pixels; the gallery
    // passes the picture's too. A null answer keeps what is shown; a failure
    // is the seam's to log.
    public async Task LoadAsync(IFileIconProvider icons, FileListDisplay display, int pixelSize, int thumbnailPixelSize)
    {
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        _loadingDisplay = display;
        CancellationToken token = loading.Token;
        try
        {
            switch (display)
            {
                case FileListDisplay.List when _icon is null:
                    Icon = await icons.GetAsync(Entry.Path, Entry.IsFolder, pixelSize, token);
                    break;
                case FileListDisplay.LargeRows when _largeIcon is null:
                    LargeIcon = await icons.GetAsync(Entry.Path, Entry.IsFolder, pixelSize, token);
                    break;
                case FileListDisplay.Gallery:
                    if (_tileIcon is null) TileIcon = await icons.GetAsync(Entry.Path, Entry.IsFolder, pixelSize, token);
                    // A folder has no picture of its own here: File Explorer's
                    // folder previews are drawn from what it holds.
                    // A text file shows its beginning, read off the UI thread;
                    // the shell has no picture of it to ask for.
                    if (!Entry.IsFolder && TextPreviewReader.Previews(Entry.Path))
                    {
                        if (_previewText is not null || token.IsCancellationRequested) break;
                        string path = Entry.Path;
                        string? text = await Task.Run(() => TextPreviewReader.Read(path), token);
                        if (!token.IsCancellationRequested && text is not null) PreviewText = text;
                    }
                    else if (!Entry.IsFolder && _thumbnail is null && !token.IsCancellationRequested)
                    {
                        ImageSource? picture = await icons.GetThumbnailAsync(Entry.Path, thumbnailPixelSize, token);
                        if (!token.IsCancellationRequested && picture is not null) Thumbnail = picture;
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // A display's container went back to the recycle queue, or the display
    // was left: what that display was asking for is dropped, and the gallery's
    // picture let go. A load another display started meanwhile is not its to
    // stop.
    public void Release(FileListDisplay display)
    {
        if (_loadingDisplay == display)
        {
            _loading?.Cancel();
            _loading = null;
        }
        if (display == FileListDisplay.Gallery)
        {
            if (_thumbnail is not null) Thumbnail = null;
            if (_previewText is not null) PreviewText = null;
        }
    }

    private void Set(ref ImageSource? field, ImageSource? value, params string[] properties)
    {
        if (ReferenceEquals(field, value)) return;
        field = value;
        Notify(properties);
    }

    private void Notify(params string[] properties)
    {
        foreach (string property in properties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

// The three displays of one folder, chosen at the bottom right of the file
// tool (spec, Window: "a compact list, large rows, a gallery"; boards 12.18,
// 12.16, 12.17).
public enum FileListDisplay
{
    List,
    LargeRows,
    Gallery,
}
