namespace Deckle.Orchestration;

// How many candidates each source produced and how many were new.
public sealed record ScanResult(int SteamFound, int FolderFound, int Added);

// The "Scan Steam and folders" command. Collects candidates from the Steam
// libraries and the chosen folders, then adds those the registry does not
// have yet. Additive by design: an existing entry is never overwritten (the
// user may have renamed it or changed its exe) and nothing is removed (an
// unplugged drive must not cost the user their rules). Silent: the page shows
// the outcome, no notification. Local files only, no network.
//
// Blocking file I/O: callers run it off the UI thread.
public static class RegistryScan
{
    public static ScanResult Run(OrchestrationSettingsService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        string? steamRoot = SteamLibrary.LocateSteamRoot();
        if (steamRoot is null) DeckleOrchestrationSource.Log.SteamNotFound();

        IReadOnlyList<AppEntry> steam = CollectSteam(steamRoot);
        IReadOnlyList<AppEntry> folders = CollectFolders(service.Current.ScanFolders);
        int added = service.AddEntriesIfNew([.. steam, .. folders]);

        DeckleOrchestrationSource.Log.ScanCompleted();
        DeckleOrchestrationSource.Log.ScanCompletedDetail(steam.Count, folders.Count, added);
        return new ScanResult(steam.Count, folders.Count, added);
    }

    // One Game entry per installed Steam game. The exe is a guess and may be
    // null; the install folder is what the entry matches on.
    internal static IReadOnlyList<AppEntry> CollectSteam(string? steamRoot)
    {
        if (steamRoot is null) return [];

        var entries = new List<AppEntry>();
        foreach (SteamManifest manifest in SteamLibrary.ScanLibraries(SteamLibrary.LibraryRootsOf(steamRoot)))
        {
            string? exe = ExecutableLocator.Locate(manifest.InstallDirectory, [manifest.Name, manifest.InstallDir]);
            entries.Add(new AppEntry
            {
                DisplayName = manifest.Name,
                Kind = AppKind.Game,
                Source = AppSource.Steam,
                SteamAppId = manifest.AppId,
                InstallDirectory = manifest.InstallDirectory,
                ExecutablePath = exe,
                ProcessName = exe is null ? string.Empty : AppMatcher.NormalizeProcessName(exe),
            });
        }
        return entries;
    }

    internal static IReadOnlyList<AppEntry> CollectFolders(IEnumerable<string> folders)
    {
        var entries = new List<AppEntry>();
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                DeckleOrchestrationSource.Log.ScanFolderMissing(folder);
                continue;
            }
            entries.AddRange(FolderScanner.Scan(folder));
        }
        return entries;
    }
}
