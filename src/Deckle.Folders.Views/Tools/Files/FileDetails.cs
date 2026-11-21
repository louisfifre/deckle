using Microsoft.UI.Xaml;

namespace Deckle.Folders.Views;

// What the details panel holds for a file or a folder of the list: size, date
// modified and location (board 12.1). The kind reads as File Explorer names a
// type it knows nothing more of ("PDF File"); the names Windows gives each
// registered type come with the real file icons, behind the same seam.
internal static class FileDetails
{
    public static UIElement For(FolderEntry entry) =>
        new DetailsSheet(Kind(entry), entry.Name)
            .Section(
                FoldersText.Get("Details_Section"),
                (FoldersText.Get("Details_Size"), entry.Size is long bytes ? EntryFormat.Size(bytes, FoldersText.Culture) : null),
                (FoldersText.Get("Details_Modified"), EntryFormat.Date(entry.Modified, FoldersText.Culture)),
                (FoldersText.Get("Details_Location"), System.IO.Path.GetDirectoryName(entry.Path)))
            .Element;

    // Also the first part of a large row's line (FileRow.Summary).
    public static string Kind(FolderEntry entry)
    {
        if (entry.IsFolder) return FoldersText.Get("Kind_Folder");
        return EntryFormat.TypeExtension(entry.Name) is { } extension
            ? FoldersText.Format("Kind_File_Format", extension)
            : FoldersText.Get("Kind_File");
    }
}
