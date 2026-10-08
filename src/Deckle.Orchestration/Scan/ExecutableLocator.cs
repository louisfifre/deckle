namespace Deckle.Orchestration;

// Guesses which executable of an install folder is the game. Steam manifests
// and plain folders name no exe, so this is a heuristic by construction; the
// entry still matches by its install folder when the guess is wrong, and the
// user can change the exe from the row.
//
// Rule: drop installers, uninstallers, redistributables and crash reporters;
// prefer an exe whose name is a preferred stem (the game's name or folder),
// then one that starts with it, else the largest file.
public static class ExecutableLocator
{
    private static readonly string[] ExcludedPatterns =
    [
        "unins*", "*setup*", "*install*", "*redist*", "vcredist*", "dxsetup*",
        "*crashhandler*", "*crashreport*",
    ];

    // Exes in the folder and one level down: most games keep theirs at the
    // root or in one "bin"/"Binaries"-style subfolder. Null when the folder is
    // missing or holds no candidate.
    public static string? Locate(string installDirectory, IEnumerable<string> preferredStems)
    {
        ArgumentNullException.ThrowIfNull(preferredStems);
        if (!Directory.Exists(installDirectory)) return null;

        var candidates = new List<(string Path, long Size)>();
        AddExecutables(installDirectory, candidates);
        foreach (string subfolder in SafeSubdirectories(installDirectory))
            AddExecutables(subfolder, candidates);

        return Pick(candidates, preferredStems);
    }

    // Pure: the decision above, over a list of (path, size in bytes).
    public static string? Pick(
        IReadOnlyList<(string Path, long Size)> candidates, IEnumerable<string> preferredStems)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(preferredStems);

        var kept = candidates
            .Where(c => !IsExcluded(Path.GetFileNameWithoutExtension(c.Path)))
            .ToList();
        if (kept.Count == 0) return null;

        string[] preferred = preferredStems
            .Select(Reduce)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var exact = kept.Where(c => preferred.Contains(ReducedStem(c.Path))).ToList();
        if (exact.Count > 0) return Largest(exact);

        var prefix = kept.Where(c => preferred.Any(p => ReducedStem(c.Path).StartsWith(p, StringComparison.Ordinal))).ToList();
        if (prefix.Count > 0) return Largest(prefix);

        return Largest(kept);
    }

    // True for an installer, uninstaller, redistributable or crash reporter.
    public static bool IsExcluded(string stem) =>
        ExcludedPatterns.Any(pattern => WildcardMatch(stem, pattern));

    // "Hollow Knight" → "hollowknight"; "Spacewar_x64" → "spacewarx64".
    internal static string Reduce(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string ReducedStem(string path) => Reduce(Path.GetFileNameWithoutExtension(path));

    // Ties go to the earlier candidate, so the result is stable.
    private static string Largest(List<(string Path, long Size)> candidates)
    {
        var best = candidates[0];
        foreach (var candidate in candidates)
            if (candidate.Size > best.Size) best = candidate;
        return best.Path;
    }

    // "*" stands for any run of characters, case ignored. The patterns are
    // fixed and short, so a recursive match is plenty.
    private static bool WildcardMatch(string text, string pattern)
    {
        if (pattern.Length == 0) return text.Length == 0;
        if (pattern[0] == '*')
        {
            for (int i = 0; i <= text.Length; i++)
                if (WildcardMatch(text[i..], pattern[1..])) return true;
            return false;
        }
        return text.Length > 0
            && char.ToLowerInvariant(text[0]) == char.ToLowerInvariant(pattern[0])
            && WildcardMatch(text[1..], pattern[1..]);
    }

    private static void AddExecutables(string directory, List<(string Path, long Size)> candidates)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                candidates.Add((file, new FileInfo(file).Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleOrchestrationSource.Log.ScanFileUnreadable(directory, ex.GetType().Name, ex.Message);
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleOrchestrationSource.Log.ScanFileUnreadable(directory, ex.GetType().Name, ex.Message);
            return [];
        }
    }
}
