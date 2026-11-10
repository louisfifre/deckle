using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Atelier;

// The marks the boards draw (12.4): "À garder" (G) and "À supprimer" (X), and
// a photo with neither is "sans marque". Two marks only: spec, Open question 3,
// drawn A.
public enum PhotoMark
{
    None,
    Keep,
    Delete,
}

// The three counters above the gallery (12.4): "215 sans marque · 160 à
// garder · 37 à supprimer".
public readonly record struct MarkCounts(int Unmarked, int Keep, int Delete);

// The marks of one folder, kept in the app's own store and never next to the
// photos: "Marking a photo changes nothing on the disk" (spec, Behaviours).
//
// Why not in XMP sidecars, as darktable reads them: once darktable has
// imported a photo, its database wins over the sidecar (darktable manual,
// "sidecar files"), so a mark written there after the hand-over would be
// ignored, and writing next to the photos is what the spec rules out while
// sorting. Writing the marks to XMP stays, at most, a later export.
//
// One small JSON file per folder, <module>\photo-marks\<key>.json, where the
// key is a hash of the folder's path: a mark changes on every key press while
// sorting, and rewriting one folder's few hundred entries stays cheap where a
// single file for every folder would grow without end. The file also holds
// the folder's path, so a reader can tell which folder it is for.
//
// Marks are keyed by file name inside the folder, case ignored as NTFS does.
// A photo renamed or removed outside the app loses its mark; a mark whose file
// is gone counts for nothing (Count reads the folder's current photos).
public sealed class PhotoMarkStore
{
    public const string DirectoryName = "photo-marks";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly Dictionary<string, PhotoMark> _marks = new(StringComparer.OrdinalIgnoreCase);

    public PhotoMarkStore(string moduleDirectory, string folder)
    {
        Folder = FolderPaths.Normalize(folder);
        _path = Path.Combine(moduleDirectory, DirectoryName, KeyOf(Folder) + ".json");
        Load();
    }

    public string Folder { get; }

    public string FilePath => _path;

    public event Action? Changed;

    public PhotoMark MarkOf(string fileName) => _marks.GetValueOrDefault(fileName, PhotoMark.None);

    // One photo or a selection of several, saved at once: one write per
    // gesture, however many photos it marks.
    public void Mark(string fileName, PhotoMark mark) => Mark([fileName], mark);

    public void Mark(IEnumerable<string> fileNames, PhotoMark mark)
    {
        bool changed = false;
        foreach (string name in fileNames) changed |= SetOne(name, mark);
        if (!changed) return;
        Save();
        Changed?.Invoke();
    }

    public MarkCounts Count(IEnumerable<PhotoFile> photos)
    {
        int unmarked = 0, keep = 0, delete = 0;
        foreach (PhotoFile photo in photos)
        {
            switch (MarkOf(photo.Name))
            {
                case PhotoMark.Keep: keep++; break;
                case PhotoMark.Delete: delete++; break;
                default: unmarked++; break;
            }
        }
        return new MarkCounts(unmarked, keep, delete);
    }

    // After the application: a recycled photo's mark goes with it, a renamed
    // photo's mark follows its new name, both in one write.
    internal void Settle(IEnumerable<string> removed, IEnumerable<(string From, string To)> renamed)
    {
        bool changed = false;
        foreach (string name in removed) changed |= _marks.Remove(name);
        foreach ((string from, string to) in renamed)
        {
            if (!_marks.Remove(from, out PhotoMark mark)) continue;
            _marks[to] = mark;
            changed = true;
        }
        if (!changed) return;
        Save();
        Changed?.Invoke();
    }

    private bool SetOne(string fileName, PhotoMark mark)
    {
        if (mark == PhotoMark.None) return _marks.Remove(fileName);
        if (_marks.TryGetValue(fileName, out PhotoMark known) && known == mark) return false;
        _marks[fileName] = mark;
        return true;
    }

    // First 16 hex digits of SHA-256 over the lower-cased path: stable across
    // runs, unlike string.GetHashCode, and short enough for a file name.
    private static string KeyOf(string folder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..16].ToLowerInvariant();

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_path), Json);
            foreach ((string name, PhotoMark mark) in file?.Marks ?? [])
            {
                if (mark != PhotoMark.None && !string.IsNullOrWhiteSpace(name)) _marks[name] = mark;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeckleAtelierSource.Log.PhotoMarksLoadFailed(ex.GetType().Name);
            _marks.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new StoredFile
            {
                Folder = Folder,
                Marks = new SortedDictionary<string, PhotoMark>(_marks, StringComparer.OrdinalIgnoreCase),
            };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleAtelierSource.Log.PhotoMarksSaveFailed(ex.GetType().Name);
        }
    }

    private sealed class StoredFile
    {
        public int Version { get; set; } = 1;
        public string Folder { get; set; } = "";
        public SortedDictionary<string, PhotoMark> Marks { get; set; } = [];
    }
}
