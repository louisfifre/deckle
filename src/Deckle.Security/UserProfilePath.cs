namespace Deckle.Security;

// A file path as it may appear in an exception message: the Windows user name is
// replaced by the variable that stands for its folder. The message reaches the log
// and the screen, and a path under the profile names the person; the variable
// names the same place on every machine and the reader can still open it.
public static class UserProfilePath
{
    public static string Mask(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        foreach ((string variable, Environment.SpecialFolder folder) in Roots)
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && StartsWithFolder(full, root))
                return variable + full[root.Length..];
        }
        return full;
    }

    // The narrower folder first: %LOCALAPPDATA% sits inside %USERPROFILE%.
    private static readonly (string, Environment.SpecialFolder)[] Roots =
    [
        ("%LOCALAPPDATA%", Environment.SpecialFolder.LocalApplicationData),
        ("%APPDATA%", Environment.SpecialFolder.ApplicationData),
        ("%USERPROFILE%", Environment.SpecialFolder.UserProfile),
    ];

    private static bool StartsWithFolder(string full, string root)
        => full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
           && (full.Length == root.Length
               || full[root.Length] == System.IO.Path.DirectorySeparatorChar);
}