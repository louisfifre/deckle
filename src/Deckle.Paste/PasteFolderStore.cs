using System.Text.Json;
using Deckle.Diagnostics;

namespace Deckle.Paste;

// The folder pastes go to, kept in one JSON file inside the data directory
// the app gives (paste-folder.json).
//
// Why not JsonSettingsStore: that primitive debounces, watches the file and
// holds a cross-process mutex, which settings edited by hand need. This one
// value changes only when the person chooses a folder, is written at once,
// and is read by one window, so a plain atomic write (temp file, then move)
// is the whole need. Same reasoning as Deckle.Atelier's AddedFolderStore.
//
// A file that cannot be read is copied aside as "<name>.bad" and the store
// starts with no folder, so a broken file never blocks the window and nothing
// is lost. The folder is not checked against the disk here: a folder gone
// since is the saver's failure to report, at the paste.
public sealed class PasteFolderStore
{
    public const string FileName = "paste-folder.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public PasteFolderStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
        Load();
    }

    public string FilePath => _path;

    public string? Folder { get; private set; }

    // Keeps the folder and writes it at once. A write that fails keeps the
    // choice for this run, says so in the log and returns false, so the
    // window can tell the person the choice will not survive a restart (the
    // exception stops here: a caller's catch would never see it).
    public bool SetFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        bool persisted = Save();
        DecklePasteSource.Log.FolderChosen();
        return persisted;
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_path), Json);
            Folder = string.IsNullOrWhiteSpace(file?.Folder) ? null : file.Folder;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DecklePasteSource.Log.FolderLoadFailed(ExceptionTypeName.Of(ex));
            Folder = null;
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new StoredFile { Folder = Folder }, Json));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DecklePasteSource.Log.FolderSaveFailed(ExceptionTypeName.Of(ex));
            return false;
        }
    }

    private sealed class StoredFile
    {
        public int Version { get; set; } = 1;
        public string? Folder { get; set; }
    }
}
