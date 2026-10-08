namespace Deckle.Orchestration;

// Turns a folder the user chose into registry candidates. The convention is
// the one people use for a "Games" folder: each immediate subfolder is one
// game, and an exe lying loose at the root is one app. Nothing deeper is
// walked, so pointing at a large drive stays quick.
public static class FolderScanner
{
    public static IReadOnlyList<AppEntry> Scan(string folder)
    {
        var entries = new List<AppEntry>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return entries;

        foreach (string subfolder in Enumerate(() => Directory.GetDirectories(folder), folder))
        {
            string name = Path.GetFileName(subfolder);
            string? exe = ExecutableLocator.Locate(subfolder, [name]);
            if (exe is null) continue;

            entries.Add(new AppEntry
            {
                DisplayName = name,
                Kind = AppKind.Game,
                Source = AppSource.Folder,
                InstallDirectory = subfolder,
                ExecutablePath = exe,
                ProcessName = AppMatcher.NormalizeProcessName(exe),
            });
        }

        // Loose exes get no InstallDirectory: they share the folder, and the
        // folder would otherwise make every one of them a duplicate of the first.
        foreach (string exe in Enumerate(() => Directory.GetFiles(folder, "*.exe"), folder))
        {
            string stem = AppMatcher.NormalizeProcessName(exe);
            if (ExecutableLocator.IsExcluded(stem)) continue;

            entries.Add(new AppEntry
            {
                DisplayName = AppDisplayName.Humanize(stem),
                Kind = AppKind.App,
                Source = AppSource.Folder,
                ExecutablePath = exe,
                ProcessName = stem,
            });
        }

        return entries;
    }

    private static string[] Enumerate(Func<string[]> list, string folder)
    {
        try
        {
            string[] items = list();
            Array.Sort(items, StringComparer.OrdinalIgnoreCase);
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleOrchestrationSource.Log.ScanFileUnreadable(folder, ex.GetType().Name, ex.Message);
            return [];
        }
    }
}
