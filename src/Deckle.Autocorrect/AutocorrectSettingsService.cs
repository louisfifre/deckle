using System.Text.Json;
using Deckle.Core;

namespace Deckle.Autocorrect;

public sealed class AutocorrectSettingsService
{
    private static readonly Lazy<AutocorrectSettingsService> _instance =
        new(() => new AutocorrectSettingsService());
    public static AutocorrectSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<AutocorrectSettings> _store;

    // Serializes the read-modify-write of the decision map and the master
    // switch. Two writers race otherwise — the enrollment toast (fire-and-
    // forget off the engine thread) and the settings page (UI thread) — and a
    // plain read-copy-write would lose an update. The engine never takes this
    // lock: it only reads Current.Apps, and reads the reference atomically.
    private readonly object _writeLock = new();

    public AutocorrectSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private AutocorrectSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "autocorrect", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<AutocorrectSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Autocorrect-Save",
            jsonOptions: _jsonOptions,
            logWarning:  DeckleAutocorrectSource.Log.SettingsLoadWarning,
            logError:    DeckleAutocorrectSource.Log.SettingsLoadError);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(AutocorrectSettings next) => _store.Replace(next);

    // ── Decision writes ──────────────────────────────────────────────────

    public void SetEnabled(bool enabled)
    {
        lock (_writeLock)
        {
            Current.Enabled = enabled;
            Save();
        }
    }

    public void SetDecision(string process, bool enabled)
    {
        if (string.IsNullOrEmpty(process)) return;
        lock (_writeLock)
        {
            var s = Current;
            s.Apps = AutocorrectSettings.WithDecision(s.Apps, process, enabled);
            Save();
        }
    }

    /// <summary>
    /// The effective lexicon is merged at engine build, so the App rebuilds its
    /// runtime when the resulting EffectiveLexiconKey changes.
    /// </summary>
    public void SetDomainPackActive(string packId, bool active)
    {
        if (string.IsNullOrEmpty(packId)) return;
        lock (_writeLock)
        {
            var s = Current;
            s.DomainPacks = AutocorrectSettings.WithDomainPack(s.DomainPacks, packId, active);
            Save();
        }
    }

    /// <summary>
    /// Returns the normalized form actually registered, or null when the text
    /// cannot name a single lexicon form.
    /// </summary>
    public string? ExcludeWord(string? word)
    {
        string? normalized = AutocorrectSettings.NormalizeExcludedWord(word);
        if (normalized is null) return null;

        lock (_writeLock)
        {
            var s = Current;
            s.ExcludedWords = AutocorrectSettings.WithExclusion(s.ExcludedWords, normalized);
            Save();
        }
        return normalized;
    }

    public void IncludeWord(string word)
    {
        string? normalized = AutocorrectSettings.NormalizeExcludedWord(word);
        if (normalized is null) return;

        lock (_writeLock)
        {
            var s = Current;
            if (!s.ExcludedWords.Contains(normalized, StringComparer.Ordinal)) return;
            s.ExcludedWords = AutocorrectSettings.WithoutExclusion(s.ExcludedWords, normalized);
            Save();
        }
    }

    /// <summary>
    /// The app returns to "never met" and can be offered enrollment again.
    /// </summary>
    public void RemoveDecision(string process)
    {
        if (string.IsNullOrEmpty(process)) return;
        lock (_writeLock)
        {
            var s = Current;
            if (!s.Apps.ContainsKey(process)) return;
            s.Apps = AutocorrectSettings.WithoutDecision(s.Apps, process);
            Save();
        }
    }
}
