using System.Security.AccessControl;
using System.Security.Principal;

namespace Deckle.Folders.Tests;

// Makes Windows refuse to list one folder of a scratch folder to the user the
// tests run as, the way a folder of another account refuses it, and lifts the
// refusal when disposed so the scratch folder can be removed. Only the right
// to list is denied: the folder can still be seen in its parent and deleted.
internal sealed class DenyListing : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly FileSystemAccessRule _rule;

    private DenyListing(string folder)
    {
        _folder = new DirectoryInfo(folder);
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The test's user has no security identifier.");
        _rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectorySecurity security = _folder.GetAccessControl();
        security.AddAccessRule(_rule);
        _folder.SetAccessControl(security);
    }

    public static DenyListing On(string folder) => new(folder);

    public void Dispose()
    {
        DirectorySecurity security = _folder.GetAccessControl();
        security.RemoveAccessRule(_rule);
        _folder.SetAccessControl(security);
    }
}
