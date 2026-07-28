namespace Deckle.Autocorrect;

// ── EffectiveLexicon ────────────────────────────────────────────────────────
//
// The single merged table the correctors consult (CONTEXT.md § Lexicon
// composition). All active sources — the base lexicon, the active domain packs
// — fuse once at load; on a form both carry, the highest frequency wins. That
// makes the merge commutative and idempotent, so activation order can never
// change what the engine sees, and the engine itself never faces a stack of
// dictionaries: it takes one FrequencyLexicon, exactly as before packs existed.
//
// Conflicts are handled at pack fabrication, not here — a form whose masking
// cost would smother a base correction never reaches the artifact. This merge
// therefore has no arbitration to do beyond max-wins, and the hot path stays
// free of per-pack logic.
//
// Above both sources sit the user's word exclusions: precedence is exclusions >
// packs > base lexicon (CONTEXT.md § Word exclusion), applied last, so an
// excluded form leaves the table whichever source supplied it. The engine then
// behaves as though the word did not exist — it is neither a correction target
// nor a protected literal. That is the whole of the precedence chain taken
// literally, and it is also the only exclusion the engine can honour reliably:
// candidate generation is spread across the stages and several of them
// (the diacritics sole-candidate path, the morphological accent branches, the
// elision split) apply a candidate with no frequency test at all, so demoting a
// form instead of removing it would leave it reachable.
public static class EffectiveLexicon
{
    // Fuses the base lexicon with the active packs, then subtracts the user's
    // exclusions. With no pack and no exclusion the base lexicon is returned
    // as-is — no copy, no allocation, the pre-pack path unchanged.
    public static FrequencyLexicon Compose(
        FrequencyLexicon baseLexicon,
        IReadOnlyList<FrequencyLexicon> packs,
        IReadOnlyCollection<string>? exclusions = null)
    {
        bool hasExclusions = exclusions is { Count: > 0 };
        if (packs.Count == 0 && !hasExclusions)
            return baseLexicon;

        var merged = new Dictionary<string, double>(baseLexicon.Count, StringComparer.Ordinal);
        foreach (var (form, frequency) in baseLexicon.Entries)
            merged[form] = frequency;

        foreach (FrequencyLexicon pack in packs)
        {
            foreach (var (form, frequency) in pack.Entries)
            {
                // Max wins. A pack form the base already carries keeps the
                // higher of the two: the pack never demotes a base word, and a
                // promoted pack frequency never loses to the base's.
                if (!merged.TryGetValue(form, out double prior) || frequency > prior)
                    merged[form] = frequency;
            }
        }

        // Last, and therefore highest precedence: a word the user excluded
        // leaves the table no matter which source put it there. Excluding a
        // word Deckle never knew is a no-op, not an error — the register is the
        // user's, and it may name anything.
        if (hasExclusions)
            foreach (string word in exclusions!)
                merged.Remove(word);

        return FrequencyLexicon.FromComposedEntries(merged);
    }
}
