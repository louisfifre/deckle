using System.Text.Json;
using Deckle.Core;

namespace Deckle.Transcription;

// ── TranscriptionSettingsService ─────────────────────────────────────────────
//
// Module-local persistence for TranscriptionSettings, backed by
// JsonSettingsStore<T> at <UserDataRoot>/modules/transcription/settings.json so
// the filesystem layout reflects the module boundary one-to-one.
public sealed class TranscriptionSettingsService
{
    private static readonly Lazy<TranscriptionSettingsService> _instance =
        new(() => new TranscriptionSettingsService());
    public static TranscriptionSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<TranscriptionSettings> _store;

    public TranscriptionSettings Current => _store.Current;

    /// <summary>The on-disk JSON file backing this service. Diagnostic only.</summary>
    public string Path => _store.Path;

    /// <summary>Raised after a successful disk write or a Reload.</summary>
    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private TranscriptionSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "transcription", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<TranscriptionSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Transcription-Save",
            jsonOptions: _jsonOptions,
            // Normal initialization/load/save callbacks describe persistence
            // internals, not a user workflow step. Keep only anomalies.
            logWarning:  DeckleWhispSource.Log.SettingsLoadWarning,
            logError:    DeckleWhispSource.Log.SettingsLoadError,
            postLoadMigration: ApplyPostLoadMigrations);
    }

    internal static bool ApplyPostLoadMigrations(TranscriptionSettings settings)
    {
        EnergySegmenterSettings segmenter = settings.Streaming.Segmenter;
        if (!segmenter.HasDefaultHangoverCurve())
            return false;

        bool migrated = false;

        if (segmenter.HangoverMaxMs == 10_000
            && segmenter.HangoverMinMs == 500
            && segmenter.HangoverRampStartMs == 15_000
            && segmenter.HangoverRampEndMs == 120_000)
        {
            segmenter.HangoverMaxMs = 5_000;
            migrated = true;
        }

        if (segmenter.HangoverMaxMs == 5_000
            && segmenter.HangoverMinMs == 500
            && segmenter.HangoverRampStartMs == 60_000
            && segmenter.HangoverRampEndMs == 120_000)
        {
            segmenter.HangoverRampStartMs = 15_000;
            migrated = true;
        }

        return migrated;
    }

    /// <summary>Schedule a debounced disk write (300 ms).</summary>
    public void Save() => _store.Save();

    /// <summary>Synchronous flush. Use before process exit / restart.</summary>
    public void Flush() => _store.Flush();

    public void Reload() => _store.Reload();

    /// <summary>Replace the in-memory POCO and schedule a debounced save.</summary>
    public void Replace(TranscriptionSettings next) => _store.Replace(next);

    // The user override is resolved here, not in AppPaths, so the resolution
    // policy stays with the module that owns the setting.
    public string ResolveModelsDirectory()
    {
        string user = Current.ModelsDirectory;
        if (!string.IsNullOrWhiteSpace(user))
            return user;

        return AppPaths.ModelsDirectory;
    }

}
