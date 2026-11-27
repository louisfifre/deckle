using Deckle.Core;

namespace Deckle.Folders;

// What the address field shows at rest (boards 24/11, C1, B1): the drive, an
// ellipsis standing for the folders between the added folder and here, and
// the current folder's name. Pure, so the rule is tested without a window.
//
// DriveLabel is the root of the added folder's path without its trailing
// separator ("D:", "\server\share"). It is null when the added folder is that
// root itself, because its own name already reads "D:" and the field would say
// "D: > D:".
public sealed record AddressShortForm(string? DriveLabel, bool HidesSegments, string CurrentName)
{
    public static AddressShortForm Of(FolderPlace place)
    {
        string root = place.Root.Path;
        string driveRoot = System.IO.Path.GetPathRoot(root) ?? "";
        bool addedFolderIsDrive = FolderPaths.Comparer.Equals(FolderPaths.Normalize(root), FolderPaths.Normalize(driveRoot));
        string? drive = driveRoot.Length == 0 || addedFolderIsDrive
            ? null
            : driveRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return new AddressShortForm(drive, !place.IsRoot, place.Name);
    }
}
