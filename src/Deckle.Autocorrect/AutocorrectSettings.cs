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

    // Active domain packs, by pack id ("fr-it"). Absent or false = inactive:
    // a pack extends the lexicon with vocabulary most users never type, and
    // stacking packs dilutes correction coverage, so activation is always the
    // user's deliberate act — never a shipped default.
    public Dictionary<string, bool> DomainPacks { get; set; } = new(StringComparer.Ordinal);

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
    }

    public bool IsDomainPackActive(string packId) =>
        DomainPacks.TryGetValue(packId, out bool active) && active;

    // Identifies the effective lexicon a built engine is reading — the active
    // packs, in a stable order. The App holds the key of the runtime it built
    // and compares it on every settings change: an equal key means the loaded
    // table is still the right one, a different key means the merge changed and
    // the runtime must be rebuilt. Sorting is what makes it an identity rather
    // than a history: two settings files that activate the same packs in a
    // different order describe the same lexicon and must produce the same key.
    public static string EffectiveLexiconKey(AutocorrectSettings settings)
    {
        var packs = settings.DomainPacks
            .Where(entry => entry.Value)
            .Select(entry => entry.Key)
            .OrderBy(id => id, StringComparer.Ordinal);
        return $"packs:{string.Join(',', packs)}";
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

    public static Dictionary<string, bool> WithoutDecision(
        IReadOnlyDictionary<string, bool> apps, string process)
    {
        var next = new Dictionary<string, bool>(apps, StringComparer.OrdinalIgnoreCase);
        next.Remove(process);
        return next;
    }
}
