using System.Diagnostics;

namespace Deckle.Install;

// ── RunningProcesses ──────────────────────────────────────────────────────────
//
// Detects processes executing from a given folder — the gate before overwriting
// or deleting binaries Windows keeps locked while their image runs. Candidates
// come from the folder's own top-level exe names (Deckle.exe today, whatever the
// payload grows tomorrow), matched back to live processes by image path — so a
// dev build running from a worktree never blocks an install into %LOCALAPPDATA%,
// and the current process (the installed stub running its own update) never
// blocks its own run.
public static class RunningProcesses
{
    public static string[] FromFolder(string folder, params string[] excludedFolders)
    {
        if (!Directory.Exists(folder)) return [];

        string[] exclusions = excludedFolders
            .Where(Directory.Exists)
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .ToArray();

        var found = new List<string>();
        // Recursive: the gate must cover everything the pre-extraction clean and
        // the uninstall delete touch, sub-folder exes included.
        foreach (string exe in Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories))
        {
            if (exclusions.Any(excluded => IsUnder(exe, excluded))) continue;
            string name = Path.GetFileNameWithoutExtension(exe);
            Process[] candidates = Process.GetProcessesByName(name);
            try
            {
                foreach (Process process in candidates)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    if (SameFile(TryGetImagePath(process), exe)) { found.Add(name); break; }
                }
            }
            finally
            {
                foreach (Process process in candidates) process.Dispose();
            }
        }
        return found.ToArray();
    }

    // Uninstall owns provider teardown. Only processes whose inspected image is
    // an exact executable under the supplied root are terminated; name matches
    // alone never authorize a kill. Returns actionable residuals, empty on full
    // teardown or when the root does not exist.
    public static string[] StopFromFolder(string folder)
    {
        if (!Directory.Exists(folder)) return [];

        var failures = new List<string>();
        foreach (string exe in Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(exe);
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.Id == Environment.ProcessId || !SameFile(TryGetImagePath(process), exe))
                        continue;

                    process.Kill(entireProcessTree: false);
                    if (!process.WaitForExit(milliseconds: 5_000))
                        failures.Add($"{name} (PID {process.Id}) did not stop");
                }
                catch (InvalidOperationException)
                {
                    // It exited between path inspection and termination.
                }
                catch (Exception ex)
                {
                    failures.Add($"{name} (PID {process.Id}): {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return [.. failures.Distinct(StringComparer.Ordinal)];
    }

    // MainModule can throw for processes we may not inspect (elevated, exiting);
    // an unreadable image path is treated as "not ours" — a per-user install never
    // needs to fight a process it cannot even see into.
    private static string? TryGetImagePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static bool SameFile(string? a, string b) =>
        a is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string folder)
    {
        string full = Path.GetFullPath(path);
        string prefix = folder + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
