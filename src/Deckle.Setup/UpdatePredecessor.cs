using System.Diagnostics;

namespace Deckle.Setup;

internal interface IUpdatePredecessorProcess : IDisposable
{
    string ExecutablePath { get; }
    Task WaitForExitAsync();
}

internal interface IUpdatePredecessorProcessSource
{
    IUpdatePredecessorProcess? Open(int processId);
}

// A PID is only a lookup key. The executable image proves that the opened
// process is still the installed Deckle instance that initiated this update.
internal static class UpdatePredecessor
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    internal static Task WaitForExitAsync(int? processId, string expectedExecutable) =>
        WaitForExitAsync(
            processId, expectedExecutable, new UpdatePredecessorProcessSource(), DefaultWait);

    internal static async Task WaitForExitAsync(
        int? processId,
        string expectedExecutable,
        IUpdatePredecessorProcessSource processes,
        TimeSpan timeout)
    {
        if (processId is not int id || id == Environment.ProcessId) return;

        using IUpdatePredecessorProcess? predecessor = processes.Open(id);
        if (predecessor is null || !PathsEqual(predecessor.ExecutablePath, expectedExecutable)) return;

        try
        {
            await predecessor.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The ordinary running-process gate remains authoritative after the
            // bounded handoff wait; timeout never authorizes payload replacement.
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class UpdatePredecessorProcessSource : IUpdatePredecessorProcessSource
{
    public IUpdatePredecessorProcess? Open(int processId)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            string? executable = process.MainModule?.FileName;
            if (executable is null)
            {
                process.Dispose();
                return null;
            }

            return new UpdatePredecessorProcess(process, executable);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception)
        {
            process?.Dispose();
            return null;
        }
    }
}

internal sealed class UpdatePredecessorProcess(Process process, string executablePath)
    : IUpdatePredecessorProcess
{
    public string ExecutablePath { get; } = Path.GetFullPath(executablePath);
    public Task WaitForExitAsync() => process.WaitForExitAsync();
    public void Dispose() => process.Dispose();
}
