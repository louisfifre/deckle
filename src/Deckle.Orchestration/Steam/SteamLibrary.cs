using Microsoft.Win32;

namespace Deckle.Orchestration;

// Finds Steam and the games it installed, from Steam's own files only: the
// SteamPath registry value, steamapps\libraryfolders.vdf and the
// steamapps\appmanifest_*.acf files of each library. Read-only, no network.
//
// The two text readers (ReadLibraryPaths, ReadManifest) are pure and tested on
// fixtures; LocateSteamRoot and ScanLibraries touch the machine.
public static class SteamLibrary
{
    // "Steamworks Common Redistributables": a shared runtime package every
    // Steam install carries, never a game the user plays.
    private const string RedistributablesAppId = "228980";

    // HKCU\Software\Valve\Steam\SteamPath, written by the Steam client with
    // forward slashes ("c:/program files (x86)/steam"); else the default
    // install folder when it exists; else null.
    public static string? LocateSteamRoot()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string value && TryNormalizeRoot(value) is { } root)
                return root;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Fall through to the default folder.
            DeckleOrchestrationSource.Log.SteamPathUnreadable("registry", ex.GetType().Name, ex.Message);
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(programFiles)) return null;
        string fallback = Path.Combine(programFiles, "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    // Every numbered library of libraryfolders.vdf, in file order, without
    // repeats. Current files hold a block per library with a "path" key; files
    // from before 2021 held the path directly as the numbered key's value. Both
    // are read.
    public static IReadOnlyList<string> ReadLibraryPaths(string libraryFoldersVdfText)
    {
        IReadOnlyList<KeyValuesNode> nodes = KeyValuesReader.Parse(libraryFoldersVdfText);
        KeyValuesNode? root = nodes.FirstOrDefault(
            n => string.Equals(n.Key, "libraryfolders", StringComparison.OrdinalIgnoreCase));
        if (root is null) return [];

        var paths = new List<string>();
        foreach (KeyValuesNode child in root.Children)
        {
            if (!IsNumbered(child.Key)) continue;
            string? path = child.Value ?? child.GetValue("path");
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (paths.Exists(p => SamePath(p, path))) continue;
            paths.Add(path);
        }
        return paths;
    }

    // Null when the manifest lacks its appid, name or installdir.
    public static SteamManifest? ReadManifest(string acfText, string libraryRoot)
    {
        IReadOnlyList<KeyValuesNode> nodes = KeyValuesReader.Parse(acfText);
        KeyValuesNode? state = nodes.FirstOrDefault(
            n => string.Equals(n.Key, "AppState", StringComparison.OrdinalIgnoreCase));
        if (state is null) return null;

        string? appId = state.GetValue("appid");
        string? name = state.GetValue("name");
        string? installDir = state.GetValue("installdir");
        if (string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(installDir))
            return null;

        string directory = Path.Combine(libraryRoot, "steamapps", "common", installDir);
        return new SteamManifest(appId, name, installDir, directory);
    }

    // The installed games of every library. A manifest is skipped when it
    // cannot be read, when its game folder is not on disk (an install in
    // progress or a stale manifest), or when it is the shared redistributables
    // package. A library that is not reachable (unplugged drive) is skipped.
    public static IReadOnlyList<SteamManifest> ScanLibraries(IEnumerable<string> libraryRoots)
    {
        ArgumentNullException.ThrowIfNull(libraryRoots);
        var manifests = new List<SteamManifest>();
        foreach (string root in libraryRoots)
        {
            if (!Path.IsPathFullyQualified(root)) continue;
            string steamapps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                // An unplugged drive: the answer to "why is this game missing".
                DeckleOrchestrationSource.Log.ScanFolderMissing(steamapps);
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(steamapps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogUnreadable(steamapps, ex);
                continue;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int kept = 0;
            int skipped = 0;
            foreach (string file in files)
            {
                SteamManifest? manifest;
                try
                {
                    manifest = ReadManifest(File.ReadAllText(file), root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                {
                    LogUnreadable(file, ex);
                    skipped++;
                    continue;
                }

                if (manifest is null
                    || manifest.AppId == RedistributablesAppId
                    || !Directory.Exists(manifest.InstallDirectory))
                {
                    skipped++;
                    continue;
                }
                manifests.Add(manifest);
                kept++;
            }

            // One line per library rather than per manifest: a library holds
            // hundreds of them, and the counts are what a report needs.
            DeckleOrchestrationSource.Log.SteamLibraryScanned(root, kept, skipped);
        }
        return manifests;
    }

    // The libraries to scan: the Steam root itself first (pre-2021 files did
    // not list it), then those of steamapps\libraryfolders.vdf. A missing or
    // unreadable file leaves the root alone.
    internal static IReadOnlyList<string> LibraryRootsOf(string steamRoot)
    {
        var roots = new List<string> { steamRoot };
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return roots;

        try
        {
            foreach (string path in ReadLibraryPaths(File.ReadAllText(vdf)))
                if (!roots.Exists(r => SamePath(r, path))) roots.Add(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            LogUnreadable(vdf, ex);
        }
        return roots;
    }

    private static string? TryNormalizeRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string path = value.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(path)) return null;
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            DeckleOrchestrationSource.Log.SteamPathUnreadable("registry_value", ex.GetType().Name, ex.Message);
            return null;
        }
        return Directory.Exists(path) ? path : null;
    }

    private static bool IsNumbered(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a.Replace('/', '\\')),
            Path.TrimEndingDirectorySeparator(b.Replace('/', '\\')),
            StringComparison.OrdinalIgnoreCase);

    private static void LogUnreadable(string path, Exception ex) =>
        DeckleOrchestrationSource.Log.ScanPathSkipped(path, ex.GetType().Name, ex.Message);
}
