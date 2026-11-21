using System.Text.Json;
using Deckle.Core;

namespace Deckle.Folders;

// The folders the person added, kept in one JSON file under the module's own
// data folder (<data root>\modules\folders\added-folders.json).
//
// Why not JsonSettingsStore: that primitive debounces, watches the file and
// holds a cross-process mutex, which settings edited by hand need. This list
// changes only on an explicit gesture (add, remove a folder, change its icon), is
// written at once, and
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

        // The plain folder in the accent colour, with the symbol the rule reads
        // from its name and its top level.
        var icon = IconChoice.Default with { Symbol = FolderIconRule.Pick(normalized) };
        var folder = new AddedFolder(normalized, _clock.GetUtcNow(), icon);
        _folders.Add(folder);
        Save();
        DeckleFoldersSource.Log.FolderAdded();
        DeckleFoldersSource.Log.FolderAddedDetail(_folders.Count, icon.Symbol.ToString());
        Changed?.Invoke();
        return folder;
    }

    // The added folder at exactly this path, or null.
    public AddedFolder? Find(string path)
    {
        string normalized = FolderPaths.Normalize(path);
        return _folders.FirstOrDefault(f => FolderPaths.Comparer.Equals(f.Path, normalized));
    }

    // Gives the added folder at this path another icon and saves it at once:
    // the picker has no confirm button, a choice applies when it is made
    // (12.3b). Returns the folder as it now is, or null when no added folder
    // sits at that path.
    public AddedFolder? ChangeIcon(string path, IconChoice icon)
    {
        string normalized = FolderPaths.Normalize(path);
        int index = _folders.FindIndex(f => FolderPaths.Comparer.Equals(f.Path, normalized));
        if (index < 0) return null;
        if (_folders[index].Icon == icon) return _folders[index];

        AddedFolder changed = _folders[index] with { Icon = icon };
        _folders[index] = changed;
        Save();
        DeckleFoldersSource.Log.FolderIconChanged();
        DeckleFoldersSource.Log.FolderIconChangedDetail(icon.Symbol.ToString(), icon.Tint.ToString(), icon.HasBackground);
        Changed?.Invoke();
        return changed;
    }

    // Forgets the added folder at this path and saves at once. Only the entry
    // goes: the folder and everything in it stay on the disk as they are, so
    // adding it again undoes the removal (it then gets its icon from the rule
    // again, as any folder just added). Returns false when no added folder
    // sits at that path.
    public bool Remove(string path)
    {
        string normalized = FolderPaths.Normalize(path);
        int index = _folders.FindIndex(f => FolderPaths.Comparer.Equals(f.Path, normalized));
        if (index < 0) return false;

        _folders.RemoveAt(index);
        Save();
        DeckleFoldersSource.Log.FolderRemoved();
        DeckleFoldersSource.Log.FolderRemovedDetail(_folders.Count);
        Changed?.Invoke();
        return true;
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
                _folders.Add(new AddedFolder(normalized, f.AddedAt, f.ToIcon()));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeckleFoldersSource.Log.AddedFoldersLoadFailed(ex.GetType().Name);
            _folders.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var file = new StoredFile { Folders = [.. _folders.Select(StoredFolder.From)] };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleFoldersSource.Log.AddedFoldersSaveFailed(ex.GetType().Name);
        }
    }

    // Version 2 adds the icon. A version 1 file has none: its folders take
    // IconChoice.Default, the plain folder in the accent colour.
    private sealed class StoredFile
    {
        public int Version { get; set; } = 2;
        public List<StoredFolder> Folders { get; set; } = [];
    }

    // The symbol and the tint are written by name, so the file reads without
    // the enums' order. A name this build does not know (a file written by a
    // later one) falls back to the default for that part alone.
    private sealed class StoredFolder
    {
        public string Path { get; set; } = "";
        public DateTimeOffset AddedAt { get; set; }
        public string? Icon { get; set; }
        public string? Tint { get; set; }
        public bool IconBackground { get; set; }

        public static StoredFolder From(AddedFolder folder) => new()
        {
            Path = folder.Path,
            AddedAt = folder.AddedAt,
            Icon = folder.Icon.Symbol.ToString(),
            Tint = folder.Icon.Tint.ToString(),
            IconBackground = folder.Icon.HasBackground,
        };

        public IconChoice ToIcon() => new(
            Enum.TryParse(Icon, ignoreCase: false, out IconSymbol symbol) && Enum.IsDefined(symbol) ? symbol : IconChoice.Default.Symbol,
            Enum.TryParse(Tint, ignoreCase: false, out IconTint tint) && Enum.IsDefined(tint) ? tint : IconChoice.Default.Tint,
            IconBackground);
    }
}
