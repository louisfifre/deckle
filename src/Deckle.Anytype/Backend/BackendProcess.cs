using System.Diagnostics;
using System.IO;

namespace Deckle.Anytype;

internal interface IBackendProcess : IDisposable
{
    int Id { get; }
    string ExecutablePath { get; }
    // Null when Windows refuses the query (an adopted handle without query
    // rights): a made-up value would read as a real start time or exit code.
    DateTimeOffset? StartedAt { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
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
                    DeckleAnytypeSource.Log.BackendProcessQueryFailed(
                        candidate.Id, "find_running", ex.GetType().Name, ex.Message);
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
            // The caller reports the conflict; this keeps the OS reason.
            DeckleAnytypeSource.Log.BackendProcessQueryFailed(
                processId, "open", ex.GetType().Name, ex.Message);
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
                // No console window at all: a windowed console receives
                // CTRL_CLOSE_EVENT and the serve dies with 0xC000013A.
                CreateNoWindow   = true,
                WorkingDirectory = Path.GetDirectoryName(spec.ExecutablePath) ?? string.Empty,
            });
            if (process is null)
            {
                DeckleAnytypeSource.Log.BackendSpawnFailed("start_returned_null", "", "");
                DeckleAnytypeSource.Log.BackendSpawnFailedDetail(spec.ExecutablePath);
                return null;
            }
            return new BackendProcess(process, spec.ExecutablePath);
        }
        catch (Exception ex)
        {
            DeckleAnytypeSource.Log.BackendSpawnFailed("exception", ex.GetType().Name, ex.Message);
            DeckleAnytypeSource.Log.BackendSpawnFailedDetail(spec.ExecutablePath);
            return null;
        }
    }
}

internal static class BackendProcessReport
{
    // One reading of the exited process for the reconciler and the supervisor
    // alike. Returns the uptime in seconds, -1 when the start time is unreadable.
    internal static double Stopped(IBackendProcess process)
    {
        DateTimeOffset? started = process.StartedAt;
        double uptimeSeconds = started is { } at
            ? Math.Max(0, (DateTimeOffset.UtcNow - at).TotalSeconds)
            : -1;
        int? exitCode = process.ExitCode;
        DeckleAnytypeSource.Log.BackendStopped(
            process.Id, exitCode ?? 0, exitCode.HasValue, uptimeSeconds);
        return uptimeSeconds;
    }
}

internal sealed class BackendProcess(Process process, string executablePath) : IBackendProcess
{
    private readonly Process _process = process;

    // Cached so the failure logging below never queries the handle it reports on.
    public int Id { get; } = process.Id;
    public string ExecutablePath { get; } = Path.GetFullPath(executablePath);

    public DateTimeOffset? StartedAt
    {
        get
        {
            try { return _process.StartTime; }
            catch (Exception ex)
            {
                DeckleAnytypeSource.Log.BackendProcessQueryFailed(
                    Id, "start_time", ex.GetType().Name, ex.Message);
                return null;
            }
        }
    }

    public bool HasExited
    {
        get
        {
            // An unreadable handle reads as exited so the reconciler never
            // waits on a process it cannot observe.
            try { return _process.HasExited; }
            catch (Exception ex)
            {
                DeckleAnytypeSource.Log.BackendProcessQueryFailed(
                    Id, "has_exited", ex.GetType().Name, ex.Message);
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try { return _process.ExitCode; }
            catch (Exception ex)
            {
                DeckleAnytypeSource.Log.BackendProcessQueryFailed(
                    Id, "exit_code", ex.GetType().Name, ex.Message);
                return null;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);
    public void Dispose() => _process.Dispose();
}
