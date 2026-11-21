using System.Globalization;

namespace Deckle.Folders;

// What the details panel says of several items selected at once (spec, Open
// 33: Windows' conventions). File Explorer's details pane titles them "3
// items selected" and gives the size of the files among them; a folder
// is counted, never measured, since measuring one means reading all it holds,
// which is the sizes tool's work.
public sealed record SelectionSummary(int Folders, int Files, long FileBytes)
{
    public int Items => Folders + Files;

    public static SelectionSummary Of(IEnumerable<FolderEntry> entries)
    {
        int folders = 0, files = 0;
        long bytes = 0;
        foreach (FolderEntry entry in entries)
        {
            if (entry.IsFolder)
            {
                folders++;
                continue;
            }
            files++;
            bytes += entry.Size ?? 0;
        }
        return new SelectionSummary(folders, files, bytes);
    }

    // The panel's three texts, written from the templates the interface
    // hands in, as it keeps every sentence in its resources:
    // - the title, the number of items ("3 items selected");
    // - the line above it, what they are ("1 folder · 2 files"), a kind
    //   the selection does not hold left out;
    // - the size of the files, said to leave the folders out when there are
    //   some ("3.2 MB, not counting folders"), and null when no file is selected,
    //   as File Explorer gives no size for folders alone.
    public SelectionWording Describe(SelectionTexts texts, CultureInfo culture)
    {
        string title = EntryFormat.Count(Items, texts.ItemsOne, texts.ItemsMany, culture);
        var kinds = new List<string>(2);
        if (Folders > 0) kinds.Add(EntryFormat.Count(Folders, texts.FoldersOne, texts.FoldersMany, culture));
        if (Files > 0) kinds.Add(EntryFormat.Count(Files, texts.FilesOne, texts.FilesMany, culture));
        string? size = null;
        if (Files > 0)
        {
            size = EntryFormat.Size(FileBytes, culture);
            if (Folders > 0) size = string.Format(culture, texts.SizeWithoutFolders, size);
        }
        return new SelectionWording(title, string.Join(texts.Separator, kinds), size);
    }
}

// The sentences of the panel, each "{0}" taking a number or a size. "One"
// is the form a count of one takes, "Many" every other.
public sealed record SelectionTexts(
    string ItemsOne, string ItemsMany,
    string FoldersOne, string FoldersMany,
    string FilesOne, string FilesMany,
    string Separator,
    string SizeWithoutFolders);

public sealed record SelectionWording(string Title, string Kinds, string? Size);
