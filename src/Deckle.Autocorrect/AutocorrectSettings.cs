using System.Text;
using System.Text.Json.Serialization;

namespace Deckle.Autocorrect;

// Module settings. Enablement is per app (CONTEXT.md § Autocorrect — the
// activation gate): an app the user has never met is never corrected, but a
// would-be correction there can offer enrollment; an app set to false is
// declined and left entirely alone; true means corrections run there.
public sealed class AutocorrectSettings : IJsonOnDeserialized
{
    public bool Enabled { get; set; } = true;

    // Process name (no extension, matched case-insensitively) → corrections on.
    //   absent = never encountered — a candidate for the enrollment prompt.
    //   true   = enabled here.   false = declined (never prompt, never correct).
    public Dictionary<string, bool> Apps { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { ["notepad"] = true };

    // The user's decisions about domain packs, by pack id ("fr-it").
    //   absent      = never decided — the pack follows the Windows language
    //                 list, so a language the user already writes in is on.
    //   true/false  = decided here, and never overwritten by that detection.
    // The rule that reads this map lives in DomainActivation, which takes the
    // system languages as an argument — this stays a plain serializable POCO.
    public Dictionary<string, bool> DomainPacks { get; set; } = new(StringComparer.Ordinal);

    // Words the user pulled out of correction's reach, whatever lexicon carried
    // them — precedence exclusions > packs > base. Stored normalized (lowercased
    // and NFC) so an entry matches the lexicon keys it removes.
    public List<string> ExcludedWords { get; set; } = new();

    // Legacy v1 allow-list. Read once and folded into Apps, then never written
    // again — the one-way migration off the flat list.
    [JsonPropertyName("enrolledProcesses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnrolledProcesses { get; set; }

    // System.Text.Json rebuilds Apps with the default ordinal comparer, so
    // restore the case-insensitive one; then fold any legacy allow-list in (a
    // listed app becomes enabled) and drop it so it is never written back.
    public void OnDeserialized()
    {
        Apps = new Dictionary<string, bool>(Apps, StringComparer.OrdinalIgnoreCase);
        if (EnrolledProcesses is { Count: > 0 })
            foreach (string process in EnrolledProcesses)
                if (process.Length > 0)
                    Apps[process] = true;
        EnrolledProcesses = null;

        // The exclusion register removes lexicon keys, so it must be spelled
        // the way the lexicon is. Re-normalize on load — the file is editable
        // by hand — and drop what cannot name a form, deduplicating on the way.
        ExcludedWords = ExcludedWords
            .Select(NormalizeExcludedWord)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // The one spelling of an excluded word, applied on every path in: the
    // settings page, and the settings file itself on load, which a hand edit
    // may have left in any case. Returns null for anything that cannot name a
    // single lexicon form — the lexicon is keyed by one lowercased word, so a
    // phrase or a blank has nothing to remove.
    public static string? NormalizeExcludedWord(string? word)
    {
        string trimmed = word?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
            return null;
        return trimmed.ToLowerInvariant().Normalize(NormalizationForm.FormC);
    }

    // Pure transforms of the per-app decision map, beside the map they act on.
    // AutocorrectSettingsService calls these under its write lock; both return
    // a NEW case-insensitive map so the engine — which reads Apps live on its
    // input thread — only ever observes a complete old-or-new reference, never
    // a half-built one. The OrdinalIgnoreCase comparer is preserved on purpose:
    // process names are matched without regard to case everywhere.
    public static Dictionary<string, bool> WithDecision(
        IReadOnlyDictionary<string, bool> apps, string process, bool enabled) =>
        new(apps, StringComparer.OrdinalIgnoreCase) { [process] = enabled };

    public static Dictionary<string, bool> WithDomainPack(
        IReadOnlyDictionary<string, bool> packs, string packId, bool active) =>
        new(packs, StringComparer.Ordinal) { [packId] = active };

    // Same reference-swap discipline for the exclusion register: the App reads
    // it off the UI thread when it rebuilds the effective lexicon, so it must
    // only ever see a complete old-or-new list. Sorted so the register reads
    // alphabetically wherever it is shown, and deduplicated so excluding a word
    // twice stays one entry.
    public static List<string> WithExclusion(IReadOnlyList<string> excluded, string word) =>
        excluded.Contains(word, StringComparer.Ordinal)
            ? new List<string>(excluded)
            : excluded.Append(word).OrderBy(w => w, StringComparer.Ordinal).ToList();

    public static List<string> WithoutExclusion(IReadOnlyList<string> excluded, string word) =>
        excluded.Where(w => !string.Equals(w, word, StringComparison.Ordinal)).ToList();

    public static Dictionary<string, bool> WithoutDecision(
        IReadOnlyDictionary<string, bool> apps, string process)
    {
        var next = new Dictionary<string, bool>(apps, StringComparer.OrdinalIgnoreCase);
        next.Remove(process);
        return next;
    }
}
