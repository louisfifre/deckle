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
public static class EffectiveLexicon
{
    // Fuses the base lexicon with the active packs. With no pack the base
    // lexicon is returned as-is — no copy, no allocation, the pre-pack path
    // unchanged.
    public static FrequencyLexicon Compose(
        FrequencyLexicon baseLexicon, IReadOnlyList<FrequencyLexicon> packs)
    {
        if (packs.Count == 0)
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

        return FrequencyLexicon.FromComposedEntries(merged);
    }
}
