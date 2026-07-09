using System.Text.Json;
using Deckle.Core;

namespace Deckle.Transcription;

// ── TranscriptionSettingsService ─────────────────────────────────────────────
//
// Module-local persistence for TranscriptionSettings. Each module that owns
// settings has its own service backed by JsonSettingsStore<T>; the JSON
// file lives at <UserDataRoot>/modules/transcription/settings.json so the
// filesystem layout reflects the module boundary one-to-one.
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

    /// <summary>Raised after a successful disk write.</summary>
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
            logInfo:     msg => DeckleWhispSource.Log.WhispSettingsPrefixed($"[transcription] {msg}"),
            logVerbose:  msg => DeckleWhispSource.Log.SettingsLoadComplete($"[transcription] {msg}"),
            logWarning:  msg => DeckleWhispSource.Log.SettingsLoadWarning($"[transcription] {msg}"),
            logError:    msg => DeckleWhispSource.Log.SettingsLoadError($"[transcription] {msg}"),
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

    /// <summary>Re-read from disk and replace the in-memory snapshot.</summary>
    public void Reload() => _store.Reload();

    /// <summary>Replace the in-memory POCO entirely (Reset to defaults).</summary>
    public void Replace(TranscriptionSettings next) => _store.Replace(next);

    // Resolves the directory containing speech model .bin files (Whisper +
    // VAD Silero). User override wins; otherwise fall back to
    // AppPaths.ModelsDirectory. Layered this way so the user override
    // stays reachable from the Settings UI without leaking the resolution
    // policy into AppPaths.
    public string ResolveModelsDirectory()
    {
        string user = Current.ModelsDirectory;
        if (!string.IsNullOrWhiteSpace(user))
            return user;

        return AppPaths.ModelsDirectory;
    }

    // Resolves the destination folder for file-transcription output. The
    // configured value wins; empty/whitespace is the sentinel for the user's
    // Desktop, resolved here at use time. Static and pure — the counterpart to
    // the instance ResolveModelsDirectory above, shaped this way so the engine
    // can resolve a host-provided value without reaching for the singleton.
    public static string ResolveFileTranscriptionOutputDirectory(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}
