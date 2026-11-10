using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// One row of the file list, its texts formatted once when the folder is read.
internal sealed class FileRow
{
    public FileRow(FolderEntry entry, IconSource icon)
    {
        Entry = entry;
        Icon = icon;
        Modified = EntryFormat.Date(entry.Modified, AtelierText.Culture);
        Size = entry.Size is long bytes ? EntryFormat.Size(bytes, AtelierText.Culture) : "";
    }

    public FolderEntry Entry { get; }
    public IconSource Icon { get; }
    public string Name => Entry.Name;
    public string Modified { get; }
    public string Size { get; }
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
