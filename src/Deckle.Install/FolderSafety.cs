namespace Deckle.Install;

// What the installer may touch in a folder the person picked. A picked folder can
// be shared (C:\Tools, C:\Apps), so Deckle works in a "Deckle" folder of its own
// under it, tests that it can write there before it clears anything, and on removal
// takes only what it owns.
public static class FolderSafety
{
    public const string OwnFolderName = "Deckle";

    // The folder Deckle installs into for a pick: the pick itself when it is already
    // named Deckle, otherwise a Deckle folder below it.
    public static string OwnFolderUnder(string picked)
    {
        string full = Path.GetFullPath(picked).TrimEnd('\\', '/');
        if (full.Length == 0) full = Path.GetFullPath(picked);
        return string.Equals(Path.GetFileName(full), OwnFolderName, StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, OwnFolderName);
    }

    // True when a file can be created and deleted in the folder (created if
    // missing). Run before any wipe so a read-only pick fails before it empties
    // anything.
    public static bool CanWrite(string folder)
    {
        string probe = Path.Combine(folder, $".deckle-write-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    // Entries Deckle itself writes at the top of a data root (see AppPaths).
    private static readonly string[] OwnedFolders =
        ["backups", "recovery", "telemetry", "diagnostics", "models", "modules", "apps"];
    private static readonly string[] OwnedFilePrefixes =
        ["settings.json", "secrets.dat", "window-placement.json"];

    // Removes a data root. A root named Deckle is Deckle's own and goes whole. Any
    // other folder may be a shared one picked by an earlier version, so only the
    // entries Deckle writes go, and the folder itself only once it is empty.
    // Returns whether the root is gone.
    public static bool RemoveDataRoot(string root)
    {
        if (!Directory.Exists(root)) return true;

        string full = Path.GetFullPath(root).TrimEnd('\\', '/');
        if (string.Equals(Path.GetFileName(full), OwnFolderName, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(root, recursive: true);
            return true;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            string name = Path.GetFileName(entry);
            if (Directory.Exists(entry))
            {
                if (OwnedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                    Directory.Delete(entry, recursive: true);
            }
            else if (OwnedFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(entry);
            }
        }

        if (Directory.EnumerateFileSystemEntries(root).Any()) return false;
        Directory.Delete(root);
        return true;
    }
}
