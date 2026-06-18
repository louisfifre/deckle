using System.Text.Json;
using Deckle.Core;

namespace Deckle.Autocorrect;

// Module-local persistence for AutocorrectSettings — the standard
// JsonSettingsStore<T> lazy singleton at
// <UserDataRoot>/modules/autocorrect/settings.json, same shape as
// TrackpadSettingsService. Store logging callbacks stay null: no
// EventSource dependency from the persistence layer.
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
            jsonOptions: _jsonOptions);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(AutocorrectSettings next) => _store.Replace(next);

    // ── Decision writes ──────────────────────────────────────────────────
    //
    // The module owns the mutation of its own state. The enrollment toast and
    // the settings page both route through here, so the per-app map is always
    // swapped by reference (never mutated in place) under one lock.

    /// <summary>Flip the master switch and persist.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_writeLock)
        {
            Current.Enabled = enabled;
            Save();
        }
    }

    /// <summary>
    /// Record a per-app decision (true = correct here, false = declined) and
    /// persist. New or existing process alike.
    /// </summary>
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
    /// Drop a per-app decision entirely — the app returns to "never met" and
    /// can be offered enrollment again. No-op if it was not decided.
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
