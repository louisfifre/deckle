using System.Text.Json;
using Deckle.Core;

namespace Deckle.Projects;

// What AddProject answers: the project at that path, and whether it was
// already there. The caller opens a known project's page and adds nothing
// (spec, Behaviours).
public sealed record AddResult(ProjectEntry Project, bool AlreadyKnown);

// The projects the person registered and the categories that group them, kept
// in one JSON file under the module's own data folder
// (<data root>\modules\projects\projects.json).
//
// Why not JsonSettingsStore: the same reason as AddedFolderStore. That
// primitive debounces, watches the file and holds a cross-process mutex, which
// settings edited by hand need. This list changes only on an explicit gesture,
// is written at once and is read by one window, so a plain atomic write (temp
// file, then move) is the whole need.
//
// A file that cannot be read is copied aside as "<name>.bad" and the store
// starts empty, so a broken file never blocks the window and nothing is lost.
//
// Order is the file's order: categories in creation order, projects as one
// list. A move appends to the end of the list, which makes the project last in
// its new category. The store knows nothing of the runner, so "remove is
// refused while an action runs" is the workspace's rule, not this one's.
//
// Thread-safe: the window reads on its thread while a listing finishing on a
// pool thread refreshes the pins. Changed is raised outside the lock.
public sealed class ProjectStore
{
    public const string FileName = "projects.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly List<ProjectCategory> _categories = [];
    private readonly List<ProjectEntry> _projects = [];

    public ProjectStore(string moduleDirectory, TimeProvider? clock = null)
    {
        _path = System.IO.Path.Combine(moduleDirectory, FileName);
        _clock = clock ?? TimeProvider.System;
        Load();
    }

    public string FilePath => _path;

    // Snapshots: a caller enumerating while another thread changes the store
    // sees the state at the time of the call.
    public IReadOnlyList<ProjectCategory> Categories
    {
        get { lock (_gate) return [.. _categories]; }
    }

    public IReadOnlyList<ProjectEntry> Projects
    {
        get { lock (_gate) return [.. _projects]; }
    }

    // Raised after every change that was saved (or kept in memory when the
    // save failed), on the thread that made it.
    public event Action? Changed;

    public ProjectEntry? Find(string id)
    {
        lock (_gate) return _projects.FirstOrDefault(p => p.Id == id);
    }

    public ProjectEntry? FindByPath(string path)
    {
        string normalized = FolderPaths.Normalize(path);
        lock (_gate) return _projects.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Path, normalized));
    }

    public ProjectCategory? FindCategory(string id)
    {
        lock (_gate) return _categories.FirstOrDefault(c => c.Id == id);
    }

    // The projects of one category in their order, or, with null, the ones in
    // no category in the order they arrived.
    public IReadOnlyList<ProjectEntry> ProjectsIn(string? categoryId)
    {
        lock (_gate) return [.. _projects.Where(p => p.CategoryId == categoryId)];
    }

    // ── Projects ───────────────────────────────────────────────────────────

    // Adds a folder, in the category when it names one that exists, else in
    // none. A path that is already a project (any case, with or without a
    // trailing separator) adds nothing and comes back as that project.
    public AddResult AddProject(string path, string? categoryId = null)
    {
        string normalized = FolderPaths.Normalize(path);
        ProjectEntry project;
        lock (_gate)
        {
            ProjectEntry? known = _projects.FirstOrDefault(p => FolderPaths.Comparer.Equals(p.Path, normalized));
            if (known is not null)
            {
                DeckleProjectsSource.Log.ProjectAlreadyKnown();
                return new AddResult(known, AlreadyKnown: true);
            }

            string? category = categoryId is not null && _categories.Any(c => c.Id == categoryId) ? categoryId : null;
            project = new ProjectEntry(NewId(), normalized, Name: null, category, [], Main: null, _clock.GetUtcNow());
            _projects.Add(project);
            Save();
            DeckleProjectsSource.Log.ProjectAdded(_projects.Count);
        }
        Changed?.Invoke();
        return new AddResult(project, AlreadyKnown: false);
    }

    // Forgets the project. Only the entry goes: the folder and everything in it
    // stay on the disk, so adding it again undoes the removal (its name, pins
    // and category are not remembered).
    public bool RemoveProject(string id)
    {
        lock (_gate)
        {
            int index = _projects.FindIndex(p => p.Id == id);
            if (index < 0) return false;
            _projects.RemoveAt(index);
            Save();
            DeckleProjectsSource.Log.ProjectRemoved(_projects.Count);
        }
        Changed?.Invoke();
        return true;
    }

    // Gives the project the person's name for it. An empty or blank name stores
    // null: the folder's own name comes back. The path never changes.
    public ProjectEntry? RenameProject(string id, string? name)
    {
        string? stored = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return Replace(id, p => p.Name == stored ? p : p with { Name = stored });
    }

    // Puts the project in a category, or in none with null, at the end of that
    // category. A category that does not exist leaves the project where it was.
    public ProjectEntry? MoveProject(string id, string? categoryId)
    {
        ProjectEntry? moved;
        lock (_gate)
        {
            int index = _projects.FindIndex(p => p.Id == id);
            if (index < 0) return null;
            if (categoryId is not null && !_categories.Any(c => c.Id == categoryId)) return _projects[index];
            if (_projects[index].CategoryId == categoryId) return _projects[index];

            moved = _projects[index] with { CategoryId = categoryId };
            _projects.RemoveAt(index);
            _projects.Add(moved);
            Save();
            DeckleProjectsSource.Log.ProjectMoved();
        }
        Changed?.Invoke();
        return moved;
    }

    // Puts the projects of one category in the given order. An id that is not
    // in that category is ignored; a project of the category that the list
    // forgot stays after the listed ones in its old order, so a stale drag
    // cannot lose a project. The other categories keep their places.
    public void ReorderInCategory(string? categoryId, IReadOnlyList<string> orderedProjectIds)
    {
        lock (_gate)
        {
            List<int> slots = [];
            List<ProjectEntry> members = [];
            for (int i = 0; i < _projects.Count; i++)
            {
                if (_projects[i].CategoryId != categoryId) continue;
                slots.Add(i);
                members.Add(_projects[i]);
            }

            List<ProjectEntry> reordered = [];
            foreach (string id in orderedProjectIds)
            {
                ProjectEntry? member = members.FirstOrDefault(m => m.Id == id);
                if (member is not null && !reordered.Contains(member)) reordered.Add(member);
            }
            reordered.AddRange(members.Where(m => !reordered.Contains(m)));

            if (reordered.SequenceEqual(members)) return;
            for (int i = 0; i < slots.Count; i++) _projects[slots[i]] = reordered[i];
            Save();
        }
        Changed?.Invoke();
    }

    // ── Categories ─────────────────────────────────────────────────────────

    // Adds a category at the end. A blank title becomes the default one: the
    // person who presses Enter on an empty box keeps "New category".
    public ProjectCategory CreateCategory(string? name = null)
    {
        ProjectCategory category;
        lock (_gate)
        {
            category = new ProjectCategory(NewId(), string.IsNullOrWhiteSpace(name) ? ProjectCategory.DefaultName : name.Trim());
            _categories.Add(category);
            Save();
            DeckleProjectsSource.Log.CategoryCreated(_categories.Count);
        }
        Changed?.Invoke();
        return category;
    }

    // A blank title is refused and the category stays as it was; returns the
    // category as it now is, or null when none has this id.
    public ProjectCategory? RenameCategory(string id, string? name)
    {
        ProjectCategory? result;
        lock (_gate)
        {
            int index = _categories.FindIndex(c => c.Id == id);
            if (index < 0) return null;
            if (string.IsNullOrWhiteSpace(name)) return _categories[index];

            string trimmed = name.Trim();
            if (_categories[index].Name == trimmed) return _categories[index];
            result = _categories[index] with { Name = trimmed };
            _categories[index] = result;
            Save();
        }
        Changed?.Invoke();
        return result;
    }

    // Removes the category; its projects go back to no category and none is
    // removed (spec, C1).
    public bool DeleteCategory(string id)
    {
        lock (_gate)
        {
            int index = _categories.FindIndex(c => c.Id == id);
            if (index < 0) return false;
            _categories.RemoveAt(index);

            int released = 0;
            for (int i = 0; i < _projects.Count; i++)
            {
                if (_projects[i].CategoryId != id) continue;
                _projects[i] = _projects[i] with { CategoryId = null };
                released++;
            }
            Save();
            DeckleProjectsSource.Log.CategoryDeleted(released);
        }
        Changed?.Invoke();
        return true;
    }

    // ── Pinned actions ─────────────────────────────────────────────────────

    // Pins an action. Pinning one that is pinned is not a second pin; it only
    // brings its label and script up to date. An entry with a variant and the
    // same entry with another variant are two pins.
    public ProjectEntry? Pin(string id, PinnedAction pin)
    {
        return Replace(id, p =>
        {
            int at = IndexOfPin(p.Pinned, pin.Action);
            if (at < 0) return p with { Pinned = [.. p.Pinned, pin] };
            if (p.Pinned[at] == pin) return p;
            List<PinnedAction> pins = [.. p.Pinned];
            pins[at] = pin;
            return p with { Pinned = pins };
        });
    }

    // Unpins an action; unpinning the main one clears the main.
    public ProjectEntry? Unpin(string id, ActionRef action)
    {
        return Replace(id, p =>
        {
            int at = IndexOfPin(p.Pinned, action);
            if (at < 0) return p;
            return p with
            {
                Pinned = [.. p.Pinned.Where((_, i) => i != at)],
                Main = p.Main == action ? null : p.Main,
            };
        });
    }

    // Marks one pinned action as the project's main one, or none with null.
    // An action that is not pinned is refused and the main stays as it was.
    // Returns false when the project is unknown or the action not pinned.
    public bool SetMain(string id, ActionRef? action)
    {
        bool accepted = false;
        Replace(id, p =>
        {
            if (action is not null && !p.IsPinned(action)) return p;
            accepted = true;
            return p.Main == action ? p : p with { Main = action };
        });
        return accepted;
    }

    // Brings the pins up to date with a listing of the menu just read: the
    // label of every pin the menu still gives is replaced by the current one.
    // A pin the menu no longer gives is left alone and keeps its card with the
    // label it had (spec, Pinned actions). So is a pin whose identifier now
    // names another script: the identifier follows the entry's position in the
    // menu, so a shifted menu must never make the pin launch another script.
    // Saves and raises Changed only when something changed.
    public ProjectEntry? RefreshPinned(string id, IReadOnlyList<ScriptMenuEntry> entries)
    {
        return Replace(id, p =>
        {
            List<PinnedAction>? updated = null;
            for (int i = 0; i < p.Pinned.Count; i++)
            {
                PinnedAction pin = p.Pinned[i];
                ScriptMenuEntry? entry = entries.FirstOrDefault(e => e.GivesPin(pin));
                if (entry is null || entry.Label == pin.Label) continue;
                updated ??= [.. p.Pinned];
                updated[i] = pin with { Label = entry.Label };
            }
            return updated is null ? p : p with { Pinned = updated };
        });
    }

    // ── Internals ──────────────────────────────────────────────────────────

    // Applies a change to one project. The change returns the same instance
    // when nothing changed, in which case nothing is saved and nothing raised.
    private ProjectEntry? Replace(string id, Func<ProjectEntry, ProjectEntry> change)
    {
        ProjectEntry updated;
        lock (_gate)
        {
            int index = _projects.FindIndex(p => p.Id == id);
            if (index < 0) return null;
            updated = change(_projects[index]);
            if (ReferenceEquals(updated, _projects[index])) return updated;
            _projects[index] = updated;
            Save();
        }
        Changed?.Invoke();
        return updated;
    }

    private static int IndexOfPin(IReadOnlyList<PinnedAction> pins, ActionRef action)
    {
        for (int i = 0; i < pins.Count; i++)
            if (pins[i].Action == action) return i;
        return -1;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(_path), Json);
            foreach (StoredCategory c in file?.Categories ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                string id = string.IsNullOrWhiteSpace(c.Id) ? NewId() : c.Id;
                if (_categories.Any(k => k.Id == id)) continue;
                _categories.Add(new ProjectCategory(id, c.Name.Trim()));
            }

            foreach (StoredProject p in file?.Projects ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.Path)) continue;
                string normalized = FolderPaths.Normalize(p.Path);
                if (_projects.Any(k => FolderPaths.Comparer.Equals(k.Path, normalized))) continue;

                string id = string.IsNullOrWhiteSpace(p.Id) ? NewId() : p.Id;
                if (_projects.Any(k => k.Id == id)) id = NewId();

                // A category the file no longer holds puts the project in none
                // rather than dropping it.
                string? category = p.CategoryId is not null && _categories.Any(c => c.Id == p.CategoryId) ? p.CategoryId : null;

                List<PinnedAction> pins = [];
                foreach (StoredPin pin in p.Pinned ?? [])
                {
                    if (string.IsNullOrEmpty(pin.Id)) continue;
                    var action = new ActionRef(pin.Id, pin.Variant);
                    if (pins.Any(k => k.Action == action)) continue;
                    pins.Add(new PinnedAction(action, pin.Label, pin.Script));
                }

                // The main action is one of the pins, or none.
                ActionRef? main = p.Main is { Id.Length: > 0 } m && pins.Any(k => k.Action == new ActionRef(m.Id, m.Variant))
                    ? new ActionRef(m.Id, m.Variant)
                    : null;

                string? name = string.IsNullOrWhiteSpace(p.Name) ? null : p.Name.Trim();
                _projects.Add(new ProjectEntry(id, normalized, name, category, pins, main, p.AddedAt));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeckleProjectsSource.Log.StoreLoadFailed(ex.GetType().Name);
            _categories.Clear();
            _projects.Clear();
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Called with the lock held. A failure keeps the change in memory and says
    // so once: the window goes on and the next change tries again.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var file = new StoredFile
            {
                Categories = [.. _categories.Select(c => new StoredCategory { Id = c.Id, Name = c.Name })],
                Projects = [.. _projects.Select(ToStored)],
            };
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleProjectsSource.Log.StoreSaveFailed(ex.GetType().Name);
        }
    }

    private static StoredProject ToStored(ProjectEntry p) => new()
    {
        Id = p.Id,
        Path = p.Path,
        Name = p.Name,
        CategoryId = p.CategoryId,
        AddedAt = p.AddedAt,
        Pinned = [.. p.Pinned.Select(pin => new StoredPin
        {
            Id = pin.Action.Id,
            Variant = pin.Action.Variant,
            Label = pin.Label,
            Script = pin.Script,
        })],
        Main = p.Main is null ? null : new StoredActionRef { Id = p.Main.Id, Variant = p.Main.Variant },
    };
}
