using System;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Settings;

// ── SettingsService ───────────────────────────────────────────────────────────
//
// Singleton owner of the shell AppSettings POCO, persisted to
// <UserDataRoot>/settings.json via JsonSettingsStore<AppSettings>. Each module
// persists its own settings through its own service.
//
// Thread-safety: see JsonSettingsStore<T>.
public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<AppSettings> _store;

    public AppSettings Current => _store.Current;

    internal string ConfigPath => _store.Path;

    /// <summary>
    /// Raised after a successful disk write or a Reload, never on an
    /// in-memory edit alone.
    /// </summary>
    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private SettingsService()
    {
        // No CreateDirectory: the AppPaths static ctor creates UserDataRoot.
        _store = new JsonSettingsStore<AppSettings>(
            path:        AppPaths.SettingsFilePath,
            mutexName:   AppPaths.SettingsMutexName,
            jsonOptions: _jsonOptions,
            logInfo:     msg => DeckleSettingsSource.Log.SettingsLoaded(msg),
            logVerbose:  msg => DeckleSettingsSource.Log.SettingsLoadComplete(msg),
            logWarning:  msg => DeckleSettingsSource.Log.SettingsLoadWarning(msg),
            logError:    msg => DeckleSettingsSource.Log.SettingsLoadError(msg));
    }

    /// <summary>Schedule a debounced disk write (300 ms).</summary>
    public void Save()
        => _store.Save();

    /// <summary>Synchronous flush. Use before process exit / restart.</summary>
    public void Flush() => _store.Flush();

    public void Reload() => _store.Reload();

    // The returned path may not exist yet; CreateBackup creates it.
    public string ResolveBackupDirectory()
    {
        string user = Current.Paths.BackupDirectory;
        if (!string.IsNullOrWhiteSpace(user))
            return user;

        return AppPaths.SettingsBackupDirectory;
    }
}
