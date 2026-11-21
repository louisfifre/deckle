using System.Text.Json;
using Deckle.Core;

namespace Deckle.Folders;

// The descriptions of one folder's photos, kept in the module's own store and
// never next to the photos, as the marks are (PhotoMarkStore's header gives
// the reasons): <module>\photo-descriptions\<key>.json, one small file per
// folder, holding the folder's path so a reader can tell which it is for.
//
// For each photo: the text, the model that wrote it and the date. Keyed by
// file name inside the folder, case ignored. A photo renamed or deleted
// outside the app leaves an entry that no photo matches: it is never shown,
// and nothing else breaks (CountDescribed reads the folder's current photos).
// A rename made in the app carries the description over (Settle).
//
// Written from the description queue's thread and read from the interface's,
// so every access takes the lock. Changed is raised on the thread that wrote;
// a view hops to its own thread.
public sealed class PhotoDescriptionStore
{
    public const string DirectoryName = "photo-descriptions";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, PhotoDescription> _descriptions = new(StringComparer.OrdinalIgnoreCase);

    public PhotoDescriptionStore(string moduleDirectory, string folder)
    {
        Folder = FolderPaths.Normalize(folder);
        _path = Path.Combine(moduleDirectory, DirectoryName, FolderDataKey.Of(Folder) + ".json");
        Load();
    }

    public string Folder { get; }

    public string FilePath => _path;

    // The name of the photo whose description changed.
    public event Action<string>? Changed;

    public PhotoDescription? DescriptionOf(string fileName)
    {
        lock (_lock) return _descriptions.GetValueOrDefault(fileName);
    }

    public bool IsDescribed(string fileName)
    {
        lock (_lock) return _descriptions.ContainsKey(fileName);
    }

    public int CountDescribed(IEnumerable<PhotoFile> photos)
    {
        lock (_lock) return photos.Count(p => _descriptions.ContainsKey(p.Name));
    }

    // "Describe again" replaces the text: one description per photo.
    public void Set(string fileName, PhotoDescription description)
    {
        lock (_lock)
        {
            _descriptions[fileName] = description;
            Save();
        }
        Changed?.Invoke(fileName);
    }

    // After the marks are applied: a recycled photo's description goes with
    // it, a renamed photo's follows its new name, in one write.
    internal void Settle(IEnumerable<string> removed, IEnumerable<(string From, string To)> renamed)
    {
        var touched = new List<string>();
        lock (_lock)
        {
            foreach (string name in removed)
            {
                if (_descriptions.Remove(name)) touched.Add(name);
            }
            foreach ((string from, string to) in renamed)
            {
                if (!_descriptions.Remove(from, out PhotoDescription? description)) continue;
                _descriptions[to] = description;
                touched.Add(to);
            }
            if (touched.Count == 0) return;
            Save();
        }
        foreach (string name in touched) Changed?.Invoke(name);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_path), Json);
            foreach ((string name, StoredDescription d) in file?.Photos ?? [])
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(d.Text)) continue;
                _descriptions[name] = new PhotoDescription(d.Text, d.Model ?? "", d.WrittenAt);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Kept aside, so the next save does not overwrite what could be
            // recovered by hand.
            DeckleFoldersSource.Log.PhotoDescriptionsLoadFailed(ex.GetType().Name);
            _descriptions.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new StoredFile { Folder = Folder };
            foreach ((string name, PhotoDescription d) in _descriptions)
                file.Photos[name] = new StoredDescription { Text = d.Text, Model = d.Model, WrittenAt = d.WrittenAt };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleFoldersSource.Log.PhotoDescriptionsSaveFailed(ex.GetType().Name);
        }
    }

    private sealed class StoredFile
    {
        public int Version { get; set; } = 1;
        public string Folder { get; set; } = "";
        public SortedDictionary<string, StoredDescription> Photos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StoredDescription
    {
        public string Text { get; set; } = "";
        public string? Model { get; set; }
        public DateTimeOffset WrittenAt { get; set; }
    }
}
