using System.Text.Json;

namespace Deckle.Core;

// ── JsonSettingsStore<T> ──────────────────────────────────────────────────
//
// Generic JSON persistence primitive: load on demand, save with debounce,
// flush synchronously, atomic write via temp + Move, and a per-store
// process mutex so two concurrent app instances don't clobber each
// other's writes.
//
// The store also follows its file. A write made by another process, or by
// hand in an editor, is picked up by a FileSystemWatcher, reloaded once it
// settles, and announced through Changed, so every surface bound to the
// store shows the new values without a restart. The store's own writes are
// recognised by content and ignored, and a file caught half-written stays
// ignored until the next notification brings a valid one.
//
// Logging is delegate-injected (logInfo / logVerbose / logWarning /
// logError), so this class stays in Deckle.Core (the lowest-level lib,
// no diagnostics-module dependency). Each caller passes lambdas that
// route to its own EventSource provider or local sink.
//
// Two optional migration hooks let callers patch the on-disk format
// before strict deserialization (preDeserializeMigration: e.g. legacy
// JSON key renames) and rewrite the in-memory POCO after load
// (postLoadMigration: e.g. fill missing ids on first launch). Either
// hook reporting a change flags the load as migrated, and the
// constructor, Reload and the watcher then flush the cleanup back to disk.
//
// Thread-safety: Current is read under a lock; mutations to the POCO
// graph itself are the caller's responsibility (the typical pattern is
// to mutate then call Save — the debounce + serialize-under-lock is
// what keeps the on-disk file consistent). Changed is raised on a timer
// or watcher thread, never on the caller's; UI consumers marshal.
public sealed class JsonSettingsStore<T> : IDisposable where T : class, new()
{
    /// <summary>
    /// How long the watcher waits after the last file notification before
    /// reading. An editor saves in several steps (truncate, write, close),
    /// and each one notifies; waiting lets them land as one reload.
    /// </summary>
    public static readonly TimeSpan ExternalWriteSettle = TimeSpan.FromMilliseconds(250);

    private readonly string _path;
    private readonly string _mutexName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Action<string>? _logInfo;
    private readonly Action<string>? _logVerbose;
    private readonly Action<string>? _logWarning;
    private readonly Action<string>? _logError;
    private readonly Func<string, (string json, bool migrated)>? _preDeserializeMigration;
    private readonly Func<T, bool>? _postLoadMigration;

    private readonly object _lock = new();
    private readonly System.Threading.Timer _debounceTimer;
    private readonly System.Threading.Timer _followTimer;
    private readonly FileSystemWatcher? _watcher;
    private T _current;

    // The file's text as the store last wrote or read it. A notification
    // whose content matches is the store's own write (or one already seen)
    // and is not a reload. Content, not timestamps: the atomic temp + Move
    // write gives the file a time the store never saw.
    private string? _lastText;

    /// <summary>The live in-memory POCO, not a copy.</summary>
    public T Current
    {
        get { lock (_lock) return _current; }
    }

    public string Path => _path;

    /// <summary>
    /// Raised after a successful disk write (Save → debounce → Flush, or
    /// Reload) and after a reload triggered by a write from outside the
    /// store. UI consumers subscribe to refresh bound state.
    /// </summary>
    public event Action? Changed;

    public JsonSettingsStore(
        string path,
        string mutexName,
        JsonSerializerOptions jsonOptions,
        Action<string>? logInfo = null,
        Action<string>? logVerbose = null,
        Action<string>? logWarning = null,
        Action<string>? logError = null,
        Func<string, (string json, bool migrated)>? preDeserializeMigration = null,
        Func<T, bool>? postLoadMigration = null)
    {
        _path = path;
        _mutexName = mutexName;
        _jsonOptions = jsonOptions;
        _logInfo = logInfo;
        _logVerbose = logVerbose;
        _logWarning = logWarning;
        _logError = logError;
        _preDeserializeMigration = preDeserializeMigration;
        _postLoadMigration = postLoadMigration;

        _current = Load(out bool migrated);
        _debounceTimer = new System.Threading.Timer(
            _ => Flush(), null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
        _followTimer = new System.Threading.Timer(
            _ => FollowFile(), null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);

        // Rewrite a migrated file now, so the obsolete shape is gone without
        // waiting for the user to change a setting.
        if (migrated) Flush();

        _watcher = StartWatching();
    }

    /// <summary>
    /// Load from disk. Missing file or parse failure falls back to defaults
    /// (also written to disk for the missing case so the next launch sees a
    /// well-formed file). `migrated` is true when the on-disk JSON was
    /// rewritten in memory by either of the migration hooks.
    /// </summary>
    public T Load(out bool migrated)
    {
        migrated = false;
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = new T();
                _postLoadMigration?.Invoke(defaults);
                string text = JsonSerializer.Serialize(defaults, _jsonOptions);
                File.WriteAllText(_path, text);
                lock (_lock) _lastText = text;
                _logInfo?.Invoke("Settings initialized (defaults)");
                _logVerbose?.Invoke($"load complete | source=defaults | path={_path} | reason=file_missing");
                return defaults;
            }

            string raw = File.ReadAllText(_path);
            var parsed = Parse(raw, out migrated);
            lock (_lock) _lastText = raw;

            _logInfo?.Invoke(migrated ? "Settings loaded (migrated)" : "Settings loaded");
            _logVerbose?.Invoke($"load complete | source=disk | path={_path} | bytes={raw.Length} | migrated={migrated}");
            return parsed;
        }
        catch (Exception ex)
        {
            // The app still runs on defaults, so this is a Warning rather
            // than an Error.
            _logWarning?.Invoke(
                $"parse failed, fallback to defaults | path={_path} | error={ex.GetType().Name}: {ex.Message}");
            // Same treatment as the missing-file defaults above: a post-load hook
            // that stamps fresh state (e.g. a schema version) must see every fresh
            // instance, or a later save would persist pre-migration-looking data
            // and the next launch would wrongly re-migrate it.
            var fallback = new T();
            _postLoadMigration?.Invoke(fallback);
            return fallback;
        }
    }

    // Run the file's text through the migration hooks and the serializer.
    // Throws on invalid JSON; the callers decide what a failure means (Load
    // falls back to defaults, the watcher waits for the next write).
    private T Parse(string raw, out bool migrated)
    {
        migrated = false;
        string json = raw;

        if (_preDeserializeMigration is not null)
        {
            (json, bool preMigrated) = _preDeserializeMigration(json);
            if (preMigrated) migrated = true;
        }

        var parsed = JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? new T();

        if (_postLoadMigration is not null && _postLoadMigration(parsed))
            migrated = true;

        return parsed;
    }

    /// <summary>
    /// Schedule a disk write 300 ms from now. Subsequent calls within the
    /// window reset the timer, so a slider drag persists only its final value.
    /// </summary>
    public void Save()
    {
        _debounceTimer.Change(300, System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// Replace the in-memory POCO and schedule a debounced save. The
    /// Changed event fires once the disk write lands, not here.
    /// </summary>
    public void Replace(T newValue)
    {
        if (newValue is null) throw new ArgumentNullException(nameof(newValue));
        lock (_lock)
        {
            _current = newValue;
        }
        Save();
    }

    /// <summary>
    /// Synchronous write. Used directly when a debounce window is
    /// inappropriate (process exit, restart, restore from backup).
    /// </summary>
    public void Flush()
    {
        using var processMutex = new System.Threading.Mutex(initiallyOwned: false, _mutexName);
        bool acquired = false;
        try
        {
            try
            {
                // Short timeout: if another instance lingers we log and skip
                // rather than block the UI. The next Save (debounce) retries.
                acquired = processMutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (System.Threading.AbandonedMutexException)
            {
                // The other instance crashed while holding the mutex; we
                // inherited it (WaitOne succeeded despite the exception).
                // Recoverable, but log it.
                acquired = true;
                _logWarning?.Invoke("settings mutex was abandoned (other instance crashed?) — recovering");
            }

            if (!acquired)
            {
                _logWarning?.Invoke("save skipped: another instance holds the settings mutex");
                return;
            }

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_current, _jsonOptions);
                // Recorded before the write lands, so the watcher's read of
                // the finished file matches whatever order the two threads run in.
                _lastText = json;
            }

            // Temp file then Move: a process killed mid-write leaves no
            // truncated JSON on disk.
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);

            _logVerbose?.Invoke("saved to disk");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"save failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (acquired) processMutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Re-read from disk and replace the in-memory snapshot. Bypasses the
    /// debounce timer — explicit user actions (Restore from backup) want
    /// the new state visible immediately, not 300 ms later. Any in-flight
    /// Save loses to this reload by design.
    /// </summary>
    public void Reload()
    {
        var fresh = Load(out bool migrated);
        lock (_lock)
        {
            _current = fresh;
        }
        if (migrated) Flush();
        _logInfo?.Invoke("reloaded from disk");
        Changed?.Invoke();
    }

    // ── Following the file ────────────────────────────────────────────────

    // One watcher per store, hence one per file: the watcher looks at the
    // file's folder and filters on its name. Created and Renamed matter as
    // much as Changed, because editors and this store alike replace the
    // file through a temp name rather than writing it in place.
    private FileSystemWatcher? StartWatching()
    {
        string? folder = System.IO.Path.GetDirectoryName(_path);
        string name = System.IO.Path.GetFileName(_path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _logWarning?.Invoke($"file not followed, folder missing | path={_path}");
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnFileNotified;
            watcher.Created += OnFileNotified;
            watcher.Renamed += OnFileNotified;
            watcher.Error += (_, e) =>
                _logWarning?.Invoke($"file watcher error | path={_path} | error={e.GetException().GetType().Name}");
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            // The store still works without it: writes from outside are then
            // seen at the next launch, as before.
            _logWarning?.Invoke($"file not followed | path={_path} | error={ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // Every notification restarts the settle timer; the read happens once
    // the burst is over.
    private void OnFileNotified(object sender, FileSystemEventArgs e)
    {
        _followTimer.Change(ExternalWriteSettle, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private void FollowFile()
    {
        string raw;
        try
        {
            // A rename away from the name (an editor's temp step) or a
            // deletion leaves nothing to read; the next notification will.
            if (!File.Exists(_path)) return;
            raw = File.ReadAllText(_path);
        }
        catch (IOException ex)
        {
            // The writer still holds the file; the close that follows notifies again.
            _logVerbose?.Invoke($"follow deferred, file busy | path={_path} | error={ex.GetType().Name}");
            return;
        }

        lock (_lock)
        {
            if (raw == _lastText) return;
        }

        T parsed;
        bool migrated;
        try
        {
            parsed = Parse(raw, out migrated);
        }
        catch (Exception ex)
        {
            // Half-written or hand-edited into invalid JSON: keep the current
            // values and wait. The warning tells the person editing by hand
            // why nothing moved.
            _logWarning?.Invoke(
                $"file changed on disk but could not be read, keeping current values | path={_path} | error={ex.GetType().Name}: {ex.Message}");
            return;
        }

        lock (_lock)
        {
            _current = parsed;
            _lastText = raw;
        }

        _logInfo?.Invoke("Settings reloaded (file changed on disk)");
        _logVerbose?.Invoke($"follow complete | source=watcher | path={_path} | bytes={raw.Length} | migrated={migrated}");
        if (migrated) Flush();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        _followTimer.Dispose();
        _debounceTimer.Dispose();
    }
}
