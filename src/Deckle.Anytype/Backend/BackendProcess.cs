using System.Diagnostics;
using System.IO;

namespace Deckle.Anytype;

internal interface IBackendProcess : IDisposable
{
    int Id { get; }
    string ExecutablePath { get; }
    DateTimeOffset StartedAt { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken ct);
}

internal interface IBackendProcessHost
{
    IReadOnlyList<IBackendProcess> FindRunning(IReadOnlyCollection<string> executablePaths);
    IBackendProcess? Open(int processId);
    IBackendProcess? Spawn(BackendProcessSpec spec);
}

// The process boundary is deliberately separate from reconciliation. The host
// knows how to open, enumerate and spawn Windows processes; the reconciler owns
// every adoption decision and never infers listener ownership from a name.
internal sealed class BackendProcessHost : IBackendProcessHost
{
    public IReadOnlyList<IBackendProcess> FindRunning(IReadOnlyCollection<string> executablePaths)
    {
        var expected = executablePaths
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = expected
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var found = new List<IBackendProcess>();
        var seen = new HashSet<int>();

        foreach (string name in names)
        {
            foreach (Process candidate in Process.GetProcessesByName(name))
            {
                if (!seen.Add(candidate.Id))
                {
                    candidate.Dispose();
                    continue;
                }

                try
                {
                    string? path = candidate.MainModule?.FileName;
                    if (path is not null && expected.Contains(Path.GetFullPath(path)))
                    {
                        found.Add(new BackendProcess(candidate, path));
                        continue;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The candidate exited during inspection or is unreadable.
                }

                candidate.Dispose();
            }
        }

        return found;
    }

    public IBackendProcess? Open(int processId)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            string? path = process.MainModule?.FileName;
            if (path is null)
            {
                process.Dispose();
                return null;
            }
            return new BackendProcess(process, path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public IBackendProcess? Spawn(BackendProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        try
        {
            Process? process = Process.Start(new ProcessStartInfo
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
                return null;
            }
            return new BackendProcess(process, spec.ExecutablePath);
        }
        catch (Exception ex)
        {
            DeckleAnytypeSource.Log.BackendSpawnFailed();
            DeckleAnytypeSource.Log.BackendSpawnFailedDetail($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}

internal sealed class BackendProcess(Process process, string executablePath) : IBackendProcess
{
    private readonly Process _process = process;

    public int Id => _process.Id;
    public string ExecutablePath { get; } = Path.GetFullPath(executablePath);

    public DateTimeOffset StartedAt
    {
        get
        {
            try { return _process.StartTime; }
            catch { return DateTimeOffset.UtcNow; }
        }
    }

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch { return true; }
        }
    }

    public int ExitCode
    {
        get
        {
            try { return _process.ExitCode; }
            catch { return -1; }
        }
    }

    public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);
    public void Dispose() => _process.Dispose();
}
