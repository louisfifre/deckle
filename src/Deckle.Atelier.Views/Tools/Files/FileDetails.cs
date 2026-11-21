using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// What the details panel holds for a file or a folder of the list: size, date
// modified and location (board 12.1). The kind reads as File Explorer names a
// type it knows nothing more of ("PDF File"); the names Windows gives each
// registered type come with the real file icons, behind the same seam.
internal static class FileDetails
{
    public static UIElement For(FolderEntry entry) =>
        new DetailsSheet(Kind(entry), entry.Name)
            .Section(
                AtelierText.Get("Details_Section"),
                (AtelierText.Get("Details_Size"), entry.Size is long bytes ? EntryFormat.Size(bytes, AtelierText.Culture) : null),
                (AtelierText.Get("Details_Modified"), EntryFormat.Date(entry.Modified, AtelierText.Culture)),
                (AtelierText.Get("Details_Location"), System.IO.Path.GetDirectoryName(entry.Path)))
            .Element;

    // Also the first part of a large row's line (FileRow.Summary).
    public static string Kind(FolderEntry entry)
    {
        if (entry.IsFolder) return AtelierText.Get("Kind_Folder");
        return EntryFormat.TypeExtension(entry.Name) is { } extension
            ? AtelierText.Format("Kind_File_Format", extension)
            : AtelierText.Get("Kind_File");
    }
}
