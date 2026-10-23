using System.Text.Json;
using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

public sealed class TaskbarCoverSettingsService
{
    private static readonly Lazy<TaskbarCoverSettingsService> _instance =
        new(() => new TaskbarCoverSettingsService());
    public static TaskbarCoverSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<TaskbarCoverSettings> _store;

    public TaskbarCoverSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private TaskbarCoverSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "taskbar-cover", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<TaskbarCoverSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-TaskbarCover-Save",
            jsonOptions: _jsonOptions,
            logWarning:  DeckleShellTaskbarCoverSource.Log.SettingsLoadWarning,
            logError:    DeckleShellTaskbarCoverSource.Log.SettingsLoadError);
    }

    public void Save()                              => _store.Save();
    public void Flush()                             => _store.Flush();
    public void Reload()                            => _store.Reload();
    public void Replace(TaskbarCoverSettings next)  => _store.Replace(next);
}
