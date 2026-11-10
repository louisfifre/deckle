namespace Deckle.Atelier;

// Whether a file's content is on this disk. A cloud placeholder (OneDrive's
// "available online only", any Cloud Files sync root, a file moved to offline
// storage) has a name and a size here but its bytes elsewhere: drawing its
// thumbnail would read it, and reading it downloads it. File Attribute
// Constants (Microsoft Learn) name the three marks:
//   FILE_ATTRIBUTE_OFFLINE (0x1000): "the file data is physically moved to
//     offline storage";
//   FILE_ATTRIBUTE_RECALL_ON_OPEN (0x40000): "the item is virtual. Opening the
//     item ... will cause at least some of it to be fetched from a remote store";
//   FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (0x400000): "not all of its data is on
//     local storage ... Reading the file ... will cause at least some of the
//     file ... content to be fetched".
// System.IO.FileAttributes names only the first; the two others are cast.
public static class FileContentPresence
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes NotLocal = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    public static bool IsLocal(FileAttributes attributes) => (attributes & NotLocal) == 0;
}
