namespace Deckle.Orchestration;

// The one place that decides whether a process is a registry entry, and
// whether a new entry repeats an existing one. Pure: no I/O beyond path
// normalization, so it is tested on plain lists.
public static class AppMatcher
{
    // "C:\Games\Foo\Foo.EXE" → "Foo"; "foo.exe" → "foo"; "foo" → "foo".
    // Only a trailing ".exe" is stripped, so "Foo.Launcher" keeps its dot.
    public static string NormalizeProcessName(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return string.Empty;

        string name = nameOrPath.Trim();
        int slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    // Precedence, strongest first, over enabled entries only:
    //   1. the executable path is the entry's ExecutablePath;
    //   2. the executable path lies under the entry's InstallDirectory;
    //   3. the process name is the entry's ProcessName.
    // Each tier is tried across all entries before the next, so a path match on
    // one entry always beats a name match on another listed earlier.
    public static AppEntry? Match(
        IReadOnlyList<AppEntry> entries, string? processName, string? executablePath)
    {
        string? fullPath = TryFullPath(executablePath);

        if (fullPath is not null)
        {
            foreach (AppEntry entry in entries)
            {
                if (!entry.Enabled) continue;
                string? entryPath = TryFullPath(entry.ExecutablePath);
                if (entryPath is not null
                    && string.Equals(entryPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            foreach (AppEntry entry in entries)
            {
                if (!entry.Enabled) continue;
                if (IsUnder(fullPath, entry.InstallDirectory))
                    return entry;
            }
        }

        string name = NormalizeProcessName(processName ?? executablePath);
        if (name.Length == 0) return null;

        foreach (AppEntry entry in entries)
        {
            if (!entry.Enabled) continue;
            if (string.Equals(entry.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    // A candidate repeats an entry when both name the same executable or the
    // same install directory. Process names alone never make a duplicate: two
    // different games can both ship a "game.exe".
    public static bool IsDuplicate(IReadOnlyList<AppEntry> entries, AppEntry candidate)
    {
        string? path = TryFullPath(candidate.ExecutablePath);
        string? directory = TryFullDirectory(candidate.InstallDirectory);

        foreach (AppEntry entry in entries)
        {
            if (path is not null
                && string.Equals(TryFullPath(entry.ExecutablePath), path, StringComparison.OrdinalIgnoreCase))
                return true;
            if (directory is not null
                && string.Equals(TryFullDirectory(entry.InstallDirectory), directory, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // The candidates that repeat neither an existing entry nor an earlier
    // candidate, in order. The existing list is never changed: merging is
    // additive, so a scan run twice adds nothing the second time.
    public static IReadOnlyList<AppEntry> SelectNew(
        IReadOnlyList<AppEntry> existing, IEnumerable<AppEntry> candidates)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidates);

        var seen = new List<AppEntry>(existing);
        var fresh = new List<AppEntry>();
        foreach (AppEntry candidate in candidates)
        {
            if (IsDuplicate(seen, candidate)) continue;
            seen.Add(candidate);
            fresh.Add(candidate);
        }
        return fresh;
    }

    // True when path sits inside directory. The directory is compared with a
    // trailing separator so "C:\Games\Foo" never claims "C:\Games\FooBar\x.exe".
    private static bool IsUnder(string fullPath, string? directory)
    {
        string? root = TryFullDirectory(directory);
        if (root is null) return false;
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // Full path without a trailing separator, so "C:\Games\Foo\" and
    // "C:\Games\Foo" compare equal. A drive root keeps its "C:\".
    private static string? TryFullDirectory(string? directory)
    {
        string? full = TryFullPath(directory);
        if (full is null) return null;
        string trimmed = Path.TrimEndingDirectorySeparator(full);
        return trimmed.EndsWith(':') ? full : trimmed;
    }
}
