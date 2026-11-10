using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// One row of the file list, its texts formatted once when the folder is read.
// The row shows a neutral glyph at once and the type's icon once the icon
// provider hands one back (IFileIconProvider), so the icon is the one
// property that changes after the row is shown.
internal sealed class FileRow : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public FileRow(FolderEntry entry)
    {
        Entry = entry;
        Glyph = entry.IsFolder ? AtelierGlyphs.Folder : AtelierGlyphs.File;
        Modified = EntryFormat.Date(entry.Modified, AtelierText.Culture);
        Size = entry.Size is long bytes ? EntryFormat.Size(bytes, AtelierText.Culture) : "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FolderEntry Entry { get; }
    public string Name => Entry.Name;
    public string Glyph { get; }
    public string Modified { get; }
    public string Size { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlyphVisibility)));
        }
    }

    public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
}

// The three displays of one folder, chosen at the bottom right of the file
// tool (spec, Window: "a compact list, large rows, a gallery"; boards 12.18,
// 12.16, 12.17). Only the list is built.
internal enum FileListDisplay
{
    List,
    LargeRows,
    Gallery,
}
