using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Deckle.Core;

// ── CorpusPaths ─────────────────────────────────────────────────────────────
//
// Storage layout helper — resolves the base directory for telemetry JSONL
// and audio WAV files, and normalizes profile names into filesystem-safe
// slugs. Shared by the Settings consent dialogs, the corpus writer
// (`Deckle.Transcription.Corpus.WavCorpusWriter`), and the structured-telemetry
// listeners (`Deckle.Diagnostics.Telemetry`) so there's a single source
// of truth for the storage layout.
//
// Resolution order:
//   1. Host-configured storage directory override (via
//      `ConfigureStorageDirectoryOverride`). Read on every call so a
//      host that flips the override at runtime gets picked up immediately.
//   2. `AppPaths.TelemetryDirectory` (= `<UserDataRoot>\telemetry\`),
//      always present and writable.
//
// Carry-over de la vague 6 : ce helper vivait jadis dans `Deckle.Logging`
// et lisait `TelemetryGates.Current.StorageDirectoryOverride` directement.
// Le couplage au hub legacy est remplacé par un délégué injectable —
// l'App câble le getter au boot (cf. `App.OnLaunched`) sur le service de
// settings retenu (legacy `TelemetrySettingsService` pour la sous-vague
// 6a, puis `Deckle.Diagnostics.Telemetry.TelemetrySettingsService` quand
// ce dernier sera créé en sous-vague 6d).
public static class CorpusPaths
{
    private static Func<string?> _storageDirectoryOverride = static () => null;

    // Host hook — called once at startup. The getter is invoked on every
    // `GetDirectoryPath()` call, so the source of truth is live (a
    // settings change in the UI takes effect on the next read).
    public static void ConfigureStorageDirectoryOverride(Func<string?> getter)
    {
        _storageDirectoryOverride = getter ?? throw new ArgumentNullException(nameof(getter));
    }

    public static string GetDirectoryPath()
    {
        string? custom = _storageDirectoryOverride();
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;

        return AppPaths.TelemetryDirectory;
    }

    public static string GetDefaultDirectoryPath() => AppPaths.TelemetryDirectory;

    // Lowercase ASCII, hyphen-separated slug. Accented characters are
    // transliterated via Unicode normalization (NFD + non-spacing-mark
    // strip) so "réécriture" becomes "reecriture" instead of collapsing
    // the accented bytes to hyphens. Empty input returns "unnamed".
    // Stable across the text corpus JSONL path and the audio WAV subfolder
    // name — callers rely on it to join the two sides of the corpus.
    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";

        string lowered = name.ToLowerInvariant();
        string decomposed = lowered.Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        string stripped = sb.ToString().Normalize(NormalizationForm.FormC);
        string replaced = Regex.Replace(stripped, @"[^a-z0-9]+", "-");
        string trimmed = replaced.Trim('-');
        return string.IsNullOrEmpty(trimmed) ? "unnamed" : trimmed;
    }

    public static string Sanitize(string s)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            s = s.Replace(invalid, '-');
        return s;
    }
}
