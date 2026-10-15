using System.Text.Json;
using Deckle.Core;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsService ────────────────────────────────────────────────────
//
// Module-local persistence for LlmSettings. The legacy combined settings.json
// is migrated into this module's file by
// SettingsBootstrap.MigrateLegacyToPerModule().
public sealed class LlmSettingsService
{
    private static readonly Lazy<LlmSettingsService> _instance = new(() => new LlmSettingsService());
    public static LlmSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<LlmSettings> _store;

    public LlmSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private LlmSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "llm", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<LlmSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Llm-Save",
            jsonOptions: _jsonOptions,
            logWarning:  msg => DeckleLlmSource.Log.SettingsLoadWarning(msg),
            logError:    msg => DeckleLlmSource.Log.SettingsLoadError(msg),
            postLoadMigration: LlmSettingsMigrations.Apply);
    }

    public void Save()                  => _store.Save();
    public void Flush()                 => _store.Flush();
    public void Reload()                => _store.Reload();
    public void Replace(LlmSettings next) => _store.Replace(next);
}
