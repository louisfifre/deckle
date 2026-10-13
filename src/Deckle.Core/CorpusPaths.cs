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
// slugs, so the corpus writers and the Settings consent dialogs share one
// source of truth for the storage layout.
//
// The storage-directory override is injected by the host to keep this core
// helper independent from telemetry settings and listener modules.
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
    // transliterated (NFD, then non-spacing marks dropped) so "réécriture"
    // becomes "reecriture" instead of collapsing the accented bytes to
    // hyphens.
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
