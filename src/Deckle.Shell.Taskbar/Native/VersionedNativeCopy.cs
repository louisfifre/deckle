using System.Diagnostics;
using System.Security.Cryptography;

namespace Deckle.Shell.Taskbar;

// Where explorer loads the native DLL from.
//
// Explorer holds the DLL's file open for as long as it runs, and an update
// wipes the install folder. A copy kept in the install folder would make the
// update fail on a locked file. So Deckle copies the DLL into its data root,
// into a folder named after the DLL's file version and a hash of its bytes,
// and explorer loads that copy. A new build is a new folder, hence a new
// path, and the old file stays locked only until explorer lets it go. The hash
// is in the name because a development build keeps the same file version while
// its bytes change.
//
//   <root>\<file version>-<hash8>\Deckle.Shell.Taskbar.Native.dll
//
// The root is <data root>\modules\taskbar\native in the app (see DefaultRoot);
// the tests pass a temporary folder.
internal sealed class VersionedNativeCopy
{
    public const string DllName = "Deckle.Shell.Taskbar.Native.dll";

    private readonly string _root;

    public VersionedNativeCopy(string root)
    {
        _root = root;
    }

    public string Root => _root;

    // The app's root: the module's data folder under the data root, honouring
    // DECKLE_DATA_ROOT like every other path (self-check runs in its own).
    public static string DefaultRoot() =>
        Path.Combine(Deckle.Core.AppPaths.GetModuleDirectory(TaskbarSettingsModule.ModuleId), "native");

    // "<file version>-<first 8 hex of SHA-256>" for the DLL at `sourcePath`.
    public static string FolderNameFor(string sourcePath)
    {
        string version = FileVersionInfo.GetVersionInfo(sourcePath).FileVersion ?? "0.0.0.0";
        byte[] hash = SHA256.HashData(File.ReadAllBytes(sourcePath));
        return $"{version}-{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
    }

    // Copies the DLL when its versioned folder is not there yet and returns the
    // path of the copy. The copy is written under a temporary name and moved
    // into place, so a crash never leaves a half file under the real name.
    public string Ensure(string sourcePath)
    {
        string folder = Path.Combine(_root, FolderNameFor(sourcePath));
        string target = Path.Combine(folder, DllName);
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"{DllName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporary);
            try
            {
                File.Move(temporary, target);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Another copy of Deckle moved the same file in first.
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return target;
    }

    // Deletes the version folders other than the one holding `keepPath`.
    // A folder whose DLL explorer still holds cannot be deleted: it stays, and
    // the next sweep tries again. Returns the folders it removed.
    public IReadOnlyList<string> Sweep(string keepPath)
    {
        var removed = new List<string>();
        if (!Directory.Exists(_root)) return removed;

        string keepFolder = Path.GetFullPath(Path.GetDirectoryName(keepPath)!)
            .TrimEnd(Path.DirectorySeparatorChar);

        foreach (string folder in Directory.EnumerateDirectories(_root))
        {
            if (string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                              keepFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Directory.Delete(folder, recursive: true);
                removed.Add(folder);
            }
            catch (IOException) { }                  // locked by explorer
            catch (UnauthorizedAccessException) { }  // same, or a read-only file
        }
        return removed;
    }
}
