using System.Text.Json;
using Deckle.Core;

namespace Deckle.Transcription;

// ── TranscriptionSettingsService ─────────────────────────────────────────────
//
// Module-local persistence for TranscriptionSettings. Each module that owns
// settings has its own service backed by JsonSettingsStore<T>; the JSON
// file lives at <UserDataRoot>/modules/transcription/settings.json so the
// filesystem layout reflects the module boundary one-to-one.
//
// Disk migration. The previous file lived at modules/whisp/settings.json
// (back when the parent module was still called Deckle.Whisp). On first
// load after the rename, if the legacy file exists and the new one does
// not, we move the legacy file in place. This is a one-shot, idempotent
// migration — once the new file exists the legacy path is ignored.
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
        string newPath = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "transcription", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newPath)!);

        // One-shot disk migration from the legacy modules/whisp/ location.
        // Idempotent: once the new file exists the move is skipped, and the
        // legacy directory remains untouched (a future cleanup pass can
        // remove the empty folder).
        string legacyPath = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "whisp", "settings.json");
        if (!File.Exists(newPath) && File.Exists(legacyPath))
        {
            try { File.Move(legacyPath, newPath); }
            catch { /* best-effort — JsonSettingsStore falls back to defaults */ }
        }

        _store = new JsonSettingsStore<TranscriptionSettings>(
            path:        newPath,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Transcription-Save",
            jsonOptions: _jsonOptions,
            logInfo:     msg => DeckleWhispSource.Log.WhispSettingsPrefixed($"[transcription] {msg}"),
            logVerbose:  msg => DeckleWhispSource.Log.SettingsLoadComplete($"[transcription] {msg}"),
            logWarning:  msg => DeckleWhispSource.Log.SettingsLoadWarning($"[transcription] {msg}"),
            logError:    msg => DeckleWhispSource.Log.SettingsLoadError($"[transcription] {msg}"));
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
}
