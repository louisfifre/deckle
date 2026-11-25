using System.Text.Json;
using Deckle.Core;

namespace Deckle.Folders;

// The folders the person added, kept in one JSON file under the module's own
// data folder (<data root>\modules\folders\added-folders.json).
//
// Why not JsonSettingsStore: that primitive debounces, watches the file and
// holds a cross-process mutex, which settings edited by hand need. This list
// changes only on an explicit gesture (add, remove a folder, change its icon,
// choose a tool in it, make, rename or remove a group, move a folder into or
// out of a group), is written at once, and is read by one window, so a
// plain atomic write (temp file, then move) is the whole need.
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
    private readonly List<FolderGroup> _groups = [];

    public AddedFolderStore(string moduleDirectory, TimeProvider? clock = null)
    {
        _path = System.IO.Path.Combine(moduleDirectory, FileName);
        _clock = clock ?? TimeProvider.System;
        Load();
    }

    public string FilePath => _path;

    // In the order they were added: a folder just added appears last (12.20).
    public IReadOnlyList<AddedFolder> Folders => _folders;

    // In the order they were made.
    public IReadOnlyList<FolderGroup> Groups => _groups;

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

    // Gives the added folder at this path the tool it is left in (null for the
    // default one) and saves at once. The id is kept as written: this build
    // may not know a tool a later one wrote. Returns the folder as it now is,
    // or null when no added folder sits at that path.
    public AddedFolder? ChangeTool(string path, string? toolId)
    {
        string normalized = FolderPaths.Normalize(path);
        int index = _folders.FindIndex(f => FolderPaths.Comparer.Equals(f.Path, normalized));
        if (index < 0) return null;
        if (_folders[index].Tool == toolId) return _folders[index];

        AddedFolder changed = _folders[index] with { Tool = toolId };
        _folders[index] = changed;
        Save();
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

    // Makes a group with this name (trimmed) and puts it last. Throws when the
    // name is empty or taken: the window checks IsGroupNameTaken as the person
    // types, so a throw here is a caller that skipped the check.
    public FolderGroup AddGroup(string name)
    {
        string trimmed = CheckedGroupName(name, exceptGroupId: null);
        var group = new FolderGroup(Guid.NewGuid().ToString("N"), trimmed);
        _groups.Add(group);
        Save();
        DeckleFoldersSource.Log.FolderGroupAdded();
        DeckleFoldersSource.Log.FolderGroupAddedDetail(_groups.Count);
        Changed?.Invoke();
        return group;
    }

    // Whether another group already has this name, trimmed and without regard
    // to case. The group exceptGroupId is not counted, so a rename to its own
    // name, or to another case of it, is allowed.
    public bool IsGroupNameTaken(string name, string? exceptGroupId = null)
    {
        string trimmed = name.Trim();
        return _groups.Any(g => g.Id != exceptGroupId
            && StringComparer.CurrentCultureIgnoreCase.Equals(g.Name, trimmed));
    }

    // Gives the group another name and saves at once. Returns the group as it
    // now is, or null when no group has this id. The same name as now changes
    // and writes nothing. Throws, as AddGroup does, on an empty or taken name.
    public FolderGroup? RenameGroup(string groupId, string name)
    {
        int index = _groups.FindIndex(g => g.Id == groupId);
        if (index < 0) return null;
        string trimmed = CheckedGroupName(name, groupId);
        if (_groups[index].Name == trimmed) return _groups[index];

        FolderGroup renamed = _groups[index] with { Name = trimmed };
        _groups[index] = renamed;
        Save();
        DeckleFoldersSource.Log.FolderGroupRenamed();
        Changed?.Invoke();
        return renamed;
    }

    // Removes the group and nothing else: its folders stay added, in no group,
    // where they already are in the list. Returns false when no group has this id.
    public bool RemoveGroup(string groupId)
    {
        int index = _groups.FindIndex(g => g.Id == groupId);
        if (index < 0) return false;

        _groups.RemoveAt(index);
        int released = 0;
        for (int i = 0; i < _folders.Count; i++)
        {
            if (_folders[i].GroupId != groupId) continue;
            _folders[i] = _folders[i] with { GroupId = null };
            released++;
        }
        Save();
        DeckleFoldersSource.Log.FolderGroupRemoved();
        DeckleFoldersSource.Log.FolderGroupRemovedDetail(_groups.Count, released);
        Changed?.Invoke();
        return true;
    }

    // Puts the added folder at this path in a group, or in none with a null id,
    // and saves at once. The folder's entry goes to the end of the list, so it
    // comes last in the place it joins. Returns the folder as it now is, or
    // null when no added folder sits at that path or the id names no group.
    // The place it is in already changes and writes nothing.
    public AddedFolder? MoveToGroup(string path, string? groupId)
    {
        string normalized = FolderPaths.Normalize(path);
        int index = _folders.FindIndex(f => FolderPaths.Comparer.Equals(f.Path, normalized));
        if (index < 0) return null;
        if (groupId is not null && !_groups.Any(g => g.Id == groupId)) return null;
        if (_folders[index].GroupId == groupId) return _folders[index];

        AddedFolder moved = _folders[index] with { GroupId = groupId };
        _folders.RemoveAt(index);
        _folders.Add(moved);
        Save();
        DeckleFoldersSource.Log.FolderMovedToGroup();
        DeckleFoldersSource.Log.FolderMovedToGroupDetail(groupId is not null, _folders.Count(f => f.GroupId == groupId));
        Changed?.Invoke();
        return moved;
    }

    private string CheckedGroupName(string name, string? exceptGroupId)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("A group needs a name.", nameof(name));
        if (IsGroupNameTaken(trimmed, exceptGroupId)) throw new ArgumentException("Another group has this name.", nameof(name));
        return trimmed;
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
            // A group with no id or no name, or whose id or name is already read, is skipped.
            foreach (StoredGroup g in file?.Groups ?? [])
            {
                if (string.IsNullOrWhiteSpace(g.Id) || string.IsNullOrWhiteSpace(g.Name)) continue;
                if (_groups.Any(k => k.Id == g.Id) || IsGroupNameTaken(g.Name)) continue;
                _groups.Add(new FolderGroup(g.Id, g.Name.Trim()));
            }
            foreach (StoredFolder f in file?.Folders ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Path)) continue;
                string normalized = FolderPaths.Normalize(f.Path);
                if (_folders.Any(k => FolderPaths.Comparer.Equals(k.Path, normalized))) continue;
                _folders.Add(new AddedFolder(normalized, f.AddedAt, f.ToIcon(), f.Tool,
                    _groups.Any(g => g.Id == f.Group) ? f.Group : null));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeckleFoldersSource.Log.AddedFoldersLoadFailed(ex.GetType().Name);
            _folders.Clear();
            _groups.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var file = new StoredFile
            {
                Groups = [.. _groups.Select(g => new StoredGroup { Id = g.Id, Name = g.Name })],
                Folders = [.. _folders.Select(StoredFolder.From)],
            };
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
    // IconChoice.Default, the plain folder in the accent colour. Version 3 adds
    // the tool and the groups; a version 2 file has neither: its folders open in
    // the default tool and sit in no group.
    private sealed class StoredFile
    {
        public int Version { get; set; } = 3;
        public List<StoredGroup> Groups { get; set; } = [];
        public List<StoredFolder> Folders { get; set; } = [];
    }

    private sealed class StoredGroup
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
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
        public string? Tool { get; set; }
        public string? Group { get; set; }

        public static StoredFolder From(AddedFolder folder) => new()
        {
            Path = folder.Path,
            AddedAt = folder.AddedAt,
            Icon = folder.Icon.Symbol.ToString(),
            Tint = folder.Icon.Tint.ToString(),
            IconBackground = folder.Icon.HasBackground,
            Tool = folder.Tool,
            Group = folder.GroupId,
        };

        public IconChoice ToIcon() => new(
            Enum.TryParse(Icon, ignoreCase: false, out IconSymbol symbol) && Enum.IsDefined(symbol) ? symbol : IconChoice.Default.Symbol,
            Enum.TryParse(Tint, ignoreCase: false, out IconTint tint) && Enum.IsDefined(tint) ? tint : IconChoice.Default.Tint,
            IconBackground);
    }
}
