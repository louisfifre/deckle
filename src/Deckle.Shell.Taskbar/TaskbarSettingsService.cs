using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Core;

namespace Deckle.Shell.Taskbar;

public sealed class TaskbarSettingsService
{
    private static readonly Lazy<TaskbarSettingsService> _instance =
        new(() => new TaskbarSettingsService());
    public static TaskbarSettingsService Instance => _instance.Value;

    // Internal so the tests read a settings file through the same options
    // the store does.
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The background reads as its name ("transparent"), not as a number.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly JsonSettingsStore<TaskbarSettings> _store;

    public TaskbarSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private TaskbarSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", TaskbarSettingsModule.ModuleId, "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<TaskbarSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Taskbar-Save",
            jsonOptions: JsonOptions,
            logWarning:  DeckleShellTaskbarSource.Log.SettingsLoadWarning,
            logError:    DeckleShellTaskbarSource.Log.SettingsLoadError);
    }

    public void Save()                          => _store.Save();
    public void Flush()                         => _store.Flush();
    public void Reload()                        => _store.Reload();
    public void Replace(TaskbarSettings next)   => _store.Replace(next);
}
