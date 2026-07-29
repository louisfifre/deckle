namespace Deckle.Autocorrect;

// ── DomainActivation ────────────────────────────────────────────────────────
//
// Whether a domain pack is part of the effective lexicon, and the identity of
// the lexicon that results. One rule, applied in one place, because the answer
// has two sources: what the user decided, and — where they decided nothing —
// what Windows says they write in.
//
//   stored true / false → the user's choice, honoured as written.
//   absent              → on when the pack's language is a system language.
//
// The choice is never written back over the detection. Storing the detected
// value would freeze it: a language added in Windows later would no longer
// reach the packs the user never touched, and an absent entry would stop
// meaning "still following the system". The toggle IS the override, and the
// only thing the settings file records.
//
// The language set is a parameter rather than a read of SystemLanguages, so
// this stays pure — AutocorrectSettings is a serializable POCO with no OS
// dependency, and the rule is testable without a Windows profile. Callers on
// the app path pass SystemLanguages.Current.
public static class DomainActivation
{
    public static bool IsActive(
        AutocorrectSettings settings, DomainPack pack, IReadOnlySet<string> systemLanguages)
        => settings.DomainPacks.TryGetValue(pack.Id, out bool stored)
            ? stored
            : systemLanguages.Contains(pack.Language);

    // The active packs, in Shipped order — so the effective lexicon's
    // composition order is fixed even though the merge makes it irrelevant.
    public static IReadOnlyList<DomainPack> ActiveIn(
        AutocorrectSettings settings, IReadOnlySet<string> systemLanguages)
    {
        List<DomainPack>? active = null;
        foreach (DomainPack pack in DomainPack.Shipped)
            if (IsActive(settings, pack, systemLanguages))
                (active ??= new List<DomainPack>()).Add(pack);
        return active ?? (IReadOnlyList<DomainPack>)Array.Empty<DomainPack>();
    }

    // Identifies the effective lexicon a built engine is reading — the active
    // packs and the exclusion register, in a stable order. The App holds the key
    // of the runtime it built and compares it on every settings change: an equal
    // key means the loaded table is still the right one, a different key means
    // the merge changed and the runtime must be rebuilt.
    //
    // Sorting is what makes it an identity rather than a history: two settings
    // files that describe the same table must produce the same key. So is
    // resolving through Shipped — a pack turned off explicitly and a pack left
    // absent outside the system languages both contribute nothing, and an id no
    // build ships names no forms at all.
    public static string EffectiveLexiconKey(
        AutocorrectSettings settings, IReadOnlySet<string> systemLanguages)
    {
        var packs = ActiveIn(settings, systemLanguages)
            .Select(pack => pack.Id)
            .OrderBy(id => id, StringComparer.Ordinal);
        var exclusions = settings.ExcludedWords.OrderBy(word => word, StringComparer.Ordinal);
        return $"packs:{string.Join(',', packs)}|excluded:{string.Join(',', exclusions)}";
    }
}
