using System.Diagnostics;
using System.IO;

namespace Deckle.Anytype;

// ── BackendProcess ───────────────────────────────────────────────────────────
//
// Acquires the headless serve process: adopt one already running from our
// installed binary, or start a fresh one — windowless.
//
// CreateNoWindow is the load-bearing flag. anytype.exe is a console-subsystem
// binary, so a default launch allocates a visible console whose close button
// kills the serve (STATUS_CONTROL_C_EXIT — the recurring 0xC000013A deaths).
// CreateNoWindow gives the child a console with no window at all — stdout stays
// valid for the Go runtime, but there is nothing a user can close. This is why
// the scheduled-task hosting was retired (JOURNAL 2026-07-02): the task ran the
// serve with a default, closable console.
//
// The child outlives Deckle by construction: a Windows child process is not
// tied to its parent's lifetime, and no job object binds them. Survival across
// app rebuilds — the property the scheduled task existed for — comes free.
public static class BackendProcess
{
    // Finds a live serve launched from this exact binary and returns a
    // handle-backed Process for it, or null. Matching is by main-module path,
    // not name alone: "anytype.exe" is also the Desktop app's binary name
    // (JOURNAL 2026-06-19), so a name-only match could adopt the wrong process.
    // Candidates whose main module cannot be read (exited mid-scan, access
    // denied) are skipped — a miss means a fresh spawn, never a crash.
    public static Process? TryFindRunning(string executablePath)
    {
        string name = Path.GetFileNameWithoutExtension(executablePath);
        Process? found = null;
        foreach (var candidate in Process.GetProcessesByName(name))
        {
            try
            {
                if (found is null &&
                    string.Equals(candidate.MainModule?.FileName, executablePath,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    found = candidate;
                    continue;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited between enumeration and inspection, or not ours to read.
            }
            candidate.Dispose();
        }
        return found;
    }

    // Starts the serve with no console window. Returns null on failure, having
    // logged it — the supervisor treats a null as a rejected start and retries
    // on its backoff, so this never throws.
    public static Process? Spawn(BackendProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName         = spec.ExecutablePath,
                Arguments        = spec.Arguments,
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = Path.GetDirectoryName(spec.ExecutablePath) ?? string.Empty,
            });
            if (process is null)
            {
                DeckleAnytypeSource.Log.BackendSpawnFailed();
                DeckleAnytypeSource.Log.BackendSpawnFailedDetail("Process.Start returned null");
            }
            return process;
        }
        catch (Exception ex)
        {
            DeckleAnytypeSource.Log.BackendSpawnFailed();
            DeckleAnytypeSource.Log.BackendSpawnFailedDetail($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
