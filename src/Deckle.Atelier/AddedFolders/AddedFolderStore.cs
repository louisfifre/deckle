using System.Text.Json;

namespace Deckle.Atelier;

// The folders the person added, kept in one JSON file under the module's own
// data folder (<data root>\modules\atelier\added-folders.json).
//
// Why not JsonSettingsStore: that primitive debounces, watches the file and
// holds a cross-process mutex, which settings edited by hand need. This list
// changes only on an explicit gesture (add a folder), is written at once, and
// is read by one window, so a plain atomic write (temp file, then move) is the
// whole need.
//
// A file that cannot be read is copied aside as "<name>.bad" and the store
// starts empty, so a broken file never blocks the window and nothing is lost.
public sealed class AddedFolderStore
{
    public const string FileName = "added-folders.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly List<AddedFolder> _folders = [];

    public AddedFolderStore(string moduleDirectory, TimeProvider? clock = null)
    {
        _path = System.IO.Path.Combine(moduleDirectory, FileName);
        _clock = clock ?? TimeProvider.System;
        Load();
    }

    public string FilePath => _path;

    // In the order they were added: a folder just added appears last (12.20).
    public IReadOnlyList<AddedFolder> Folders => _folders;

    public event Action? Changed;

    // Adds a folder, or returns the one already added at that path. The
    // caller gets the folder either way, so "add" on a known folder opens it.
    public AddedFolder Add(string path)
    {
        string normalized = FolderPaths.Normalize(path);
        AddedFolder? known = _folders.FirstOrDefault(f => FolderPaths.Comparer.Equals(f.Path, normalized));
        if (known is not null) return known;

        var folder = new AddedFolder(normalized, _clock.GetUtcNow());
        _folders.Add(folder);
        Save();
        DeckleAtelierSource.Log.FolderAdded();
        DeckleAtelierSource.Log.FolderAddedDetail(_folders.Count);
        Changed?.Invoke();
        return folder;
    }

    // The added folder that holds this path, the deepest one when added
    // folders nest; null when the path is outside every one.
    public AddedFolder? Owning(string path)
    {
        AddedFolder? best = null;
        foreach (AddedFolder f in _folders)
        {
            if (FolderPaths.IsInside(f.Path, path) && (best is null || f.Path.Length > best.Path.Length))
                best = f;
        }
        return best;
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_path), Json);
            foreach (StoredFolder f in file?.Folders ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Path)) continue;
                string normalized = FolderPaths.Normalize(f.Path);
                if (_folders.Any(k => FolderPaths.Comparer.Equals(k.Path, normalized))) continue;
                _folders.Add(new AddedFolder(normalized, f.AddedAt));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeckleAtelierSource.Log.AddedFoldersLoadFailed(ex.GetType().Name);
            _folders.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var file = new StoredFile { Folders = [.. _folders.Select(f => new StoredFolder { Path = f.Path, AddedAt = f.AddedAt })] };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleAtelierSource.Log.AddedFoldersSaveFailed(ex.GetType().Name);
        }
    }

    private sealed class StoredFile
    {
        public int Version { get; set; } = 1;
        public List<StoredFolder> Folders { get; set; } = [];
    }

    private sealed class StoredFolder
    {
        public string Path { get; set; } = "";
        public DateTimeOffset AddedAt { get; set; }
    }
}
