using System.Text.Json;
using Deckle.Core;

namespace Deckle.Orchestration;

// Persistence of the apps and games registry at
// <UserDataRoot>/modules/orchestration/settings.json, and the only writer of it.
//
// Every mutation builds a NEW list and assigns it to Current.Entries under one
// write lock. The runtime (a later step) reads Entries from thread-pool
// threads without the lock; swapping the reference means it only ever sees a
// complete old or new list, never one being edited.
public sealed class OrchestrationSettingsService
{
    private const string ModuleId = "orchestration";

    private static readonly Lazy<OrchestrationSettingsService> _instance =
        new(() => new OrchestrationSettingsService());

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<OrchestrationSettings> _store;

    // Serializes read-copy-write of the lists. The page writes from the UI
    // thread; the scan (a later step) writes from a worker. Without the lock
    // two writers would each copy the old list and one addition would be lost.
    private readonly object _writeLock = new();

    private event Action? _changed;

    public static OrchestrationSettingsService Instance => _instance.Value;

    public OrchestrationSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    private OrchestrationSettingsService()
    {
        string directory = AppPaths.GetModuleDirectory(ModuleId);
        Directory.CreateDirectory(directory);
        _store = new JsonSettingsStore<OrchestrationSettings>(
            path: System.IO.Path.Combine(directory, "settings.json"),
            mutexName: $"{AppPaths.AppFolderName}-Settings-Orchestration-Save",
            jsonOptions: _jsonOptions,
            logWarning: DeckleOrchestrationSource.Log.SettingsLoadWarning,
            logError: DeckleOrchestrationSource.Log.SettingsLoadError);
    }

    // Consumers react to the in-memory value at once; only the disk write is
    // debounced by the store.
    public void Save()
    {
        _changed?.Invoke();
        _store.Save();
    }

    public void Flush() => _store.Flush();

    /// <summary>Add one entry. False when it repeats an existing one (same exe or install folder).</summary>
    public bool AddEntry(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_writeLock)
        {
            var settings = Current;
            if (AppMatcher.IsDuplicate(settings.Entries, entry)) return false;
            settings.Entries = [.. settings.Entries, entry];
            LogAdded(entry);
            Save();
            return true;
        }
    }

    /// <summary>Add every entry that repeats neither an existing one nor an earlier one in the batch.</summary>
    public int AddEntriesIfNew(IEnumerable<AppEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_writeLock)
        {
            var settings = Current;
            var next = new List<AppEntry>(settings.Entries);
            int added = 0;
            foreach (AppEntry entry in entries)
            {
                if (AppMatcher.IsDuplicate(next, entry)) continue;
                next.Add(entry);
                LogAdded(entry);
                added++;
            }
            if (added == 0) return 0;
            settings.Entries = next;
            Save();
            return added;
        }
    }

    public void RemoveEntry(string id)
    {
        lock (_writeLock)
        {
            var settings = Current;
            if (!settings.Entries.Exists(e => e.Id == id)) return;
            settings.Entries = settings.Entries.Where(e => e.Id != id).ToList();
            DeckleOrchestrationSource.Log.EntryRemoved();
            DeckleOrchestrationSource.Log.EntryRemovedDetail(id);
            Save();
        }
    }

    /// <summary>Change one entry: the mutation runs on a copy, then the list is swapped.</summary>
    public void UpdateEntry(string id, Action<AppEntry> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_writeLock)
        {
            var settings = Current;
            int index = settings.Entries.FindIndex(e => e.Id == id);
            if (index < 0) return;

            AppEntry copy = settings.Entries[index].Clone();
            mutate(copy);
            copy.Id = id;

            var next = new List<AppEntry>(settings.Entries) { [index] = copy };
            settings.Entries = next;
            Save();
        }
    }

    public void SetScanFolders(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        lock (_writeLock)
        {
            Current.ScanFolders = [.. folders];
            Save();
        }
    }

    // Internal so the observability test checks the exact pair the service emits.
    internal static void LogAdded(AppEntry entry)
    {
        DeckleOrchestrationSource.Log.EntryAdded();
        DeckleOrchestrationSource.Log.EntryAddedDetail(
            entry.Id, entry.DisplayName, entry.Source.ToString(), entry.ProcessName);
    }
}
