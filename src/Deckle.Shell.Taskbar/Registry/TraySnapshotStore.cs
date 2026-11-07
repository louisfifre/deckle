using System.Text.Json;
using Deckle.Core;

namespace Deckle.Shell.Taskbar;

// What each tray icon's IsPromoted held before Deckle first promoted it, kept
// on disk so a Restore can give it back even after Deckle restarted. Subkey
// name -> the prior DWORD, null when the value was absent. It is the only
// thing the tray feature remembers: tray-snapshot.json beside the settings
// file, written whole on each change, synchronously, so a crash between the
// first write to the registry and the next save never loses Windows's own
// values. Quitting Deckle restores nothing; the snapshot waits in the file for
// the next "off".
//
// Every value is under HKCU\Control Panel\NotifyIconSettings, which the
// promoter knows; the file holds only the subkey names. Thread-safe: the tray
// watcher's thread and the UI's both go through the promoter's lock, and this
// type takes its own as well.
internal sealed class TraySnapshotStore
{
    private const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, uint?> _icons;

    /// <summary>modules/taskbar/tray-snapshot.json under the data root.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        AppPaths.UserDataRoot, "modules", TaskbarSettingsModule.ModuleId, "tray-snapshot.json");

    public TraySnapshotStore(string path)
    {
        _path = path;
        _icons = Load(path);
    }

    /// <summary>Where an unreadable snapshot is copied before the next save replaces it.</summary>
    public static string BackupPathOf(string path) => path + ".bak";

    /// <summary>Whether no icon is recorded: nothing was promoted, or it was given back.</summary>
    public bool IsEmpty
    {
        get { lock (_gate) return _icons.Count == 0; }
    }

    /// <summary>The recorded subkey -> prior IsPromoted map (a copy, case-insensitive on the subkey).</summary>
    public IReadOnlyDictionary<string, uint?> Entries()
    {
        lock (_gate) return new Dictionary<string, uint?>(_icons, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Replaces the record with what the icons hold now and writes the file. An empty map records nothing.</summary>
    public void Record(IReadOnlyDictionary<string, uint?> icons)
    {
        lock (_gate)
        {
            _icons = new Dictionary<string, uint?>(icons, StringComparer.OrdinalIgnoreCase);
            if (_icons.Count == 0) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(new SnapshotFile(CurrentSchema, _icons), _jsonOptions);
            // Write to a sibling then move over, so a crash mid-write leaves the
            // previous file whole rather than a truncated one.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>Forgets the record once the icons are given back, and deletes the file.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _icons = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
            // File.Delete throws when the folder is missing, which is the state
            // of a machine that never promoted anything.
            if (File.Exists(_path)) File.Delete(_path);
        }
    }

    // A missing file is a clean machine. An unreadable one is copied aside,
    // logged and treated as empty: the feature keeps working, the log says why
    // an "off" could not give everything back, and the first Record, which
    // replaces the file, no longer destroys the only trace of Windows's prior
    // values (a person can still read them out of the copy).
    private static Dictionary<string, uint?> Load(string path)
    {
        var empty = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return empty;
        try
        {
            SnapshotFile? file = JsonSerializer.Deserialize<SnapshotFile>(File.ReadAllText(path), _jsonOptions);
            return file?.Icons is null ? empty : new Dictionary<string, uint?>(file.Icons, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DeckleShellTaskbarSource.Log.TraySnapshotUnreadable(ex.GetType().Name, KeepUnreadable(path));
            return empty;
        }
    }

    // A copy, not a move: the file stays where it was until a Record actually
    // replaces it. A later unreadable file overwrites an older copy.
    private static string KeepUnreadable(string path)
    {
        try
        {
            File.Copy(path, BackupPathOf(path), overwrite: true);
            return "kept";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.GetType().Name;
        }
    }

    private sealed record SnapshotFile(int Schema, Dictionary<string, uint?> Icons);
}
