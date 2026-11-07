using System.Collections.Concurrent;

namespace Deckle.Orchestration;

/// <summary>
/// Reads each executable's icon once, off the calling thread, and keeps the
/// answer by path. The page rebuilds its rows on every visit, so without this
/// every visit would call the shell again for every entry.
/// </summary>
public sealed class ExecutableIconCache
{
    public static ExecutableIconCache Shared { get; } = new();

    private readonly Func<string, ExecutableIcon?> _read;
    private readonly ConcurrentDictionary<string, Task<ExecutableIcon?>> _icons =
        new(StringComparer.OrdinalIgnoreCase);

    public ExecutableIconCache() : this(path => ExecutableIconReader.TryRead(path)) { }

    internal ExecutableIconCache(Func<string, ExecutableIcon?> read) => _read = read;

    /// <summary>
    /// The icon, or null when there is none. Cancelling stops the wait, not
    /// the read: that one is shared with whoever asks for the same path next.
    /// </summary>
    public async Task<ExecutableIcon?> GetAsync(string path, CancellationToken cancellationToken)
    {
        Task<ExecutableIcon?> read = _icons.GetOrAdd(path, key => Task.Run(() => Read(key)));
        ExecutableIcon? icon = await read.WaitAsync(cancellationToken).ConfigureAwait(true);

        // A missing icon is forgotten, so a drive plugged in later is tried
        // again. A failure that threw stays remembered (see Read): asking the
        // shell again would only fail again.
        if (icon is null && read.IsCompletedSuccessfully && !_failed.ContainsKey(path))
            _icons.TryRemove(new KeyValuePair<string, Task<ExecutableIcon?>>(path, read));
        return icon;
    }

    private readonly ConcurrentDictionary<string, byte> _failed = new(StringComparer.OrdinalIgnoreCase);

    private ExecutableIcon? Read(string path)
    {
        try
        {
            return _read(path);
        }
        catch (Exception ex)
        {
            _failed[path] = 0;
            DeckleOrchestrationSource.Log.IconReadFailed(
                Path.GetFileName(path), ex.GetType().Name, ex.HResult);
            return null;
        }
    }
}
