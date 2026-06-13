namespace Deckle.Input.Autocorrect.Lexicon;

// ── AccentIndex ─────────────────────────────────────────────────────────────
//
// The reverse map of the gate: folded key → the accented surface forms that
// fold to it, ranked by frequency. A typed bare form is folded, looked up
// here, and the candidates become correction proposals.
//
// Only forms that actually carry diacritics enter the index (Fold(form) !=
// form). A form equal to its own fold ("marche") is a literal the gate already
// protects — it has nothing to restore and never belongs to a candidate list.
public sealed class AccentIndex
{
    private readonly Dictionary<string, AccentVariant[]> _buckets;

    private AccentIndex(Dictionary<string, AccentVariant[]> buckets) => _buckets = buckets;

    // Distinct folded keys that hold at least one accented variant.
    public int Count => _buckets.Count;

    // The accented variants of a folded key, most frequent first. Empty when
    // the key carries no diacritic form — the caller then leaves the literal.
    public IReadOnlyList<AccentVariant> VariantsOf(string foldedLowerKey) =>
        _buckets.TryGetValue(foldedLowerKey, out var variants)
            ? variants
            : Array.Empty<AccentVariant>();

    // Buckets every accented entry under its folded key, each bucket sorted by
    // frequency descending so the dominant variant is index 0.
    public static AccentIndex Build(FrequencyLexicon lexicon)
    {
        var grouped = new Dictionary<string, List<AccentVariant>>(StringComparer.Ordinal);

        foreach (var (form, freq) in lexicon.Entries)
        {
            string folded = AccentFolding.Fold(form);
            if (folded == form)
                continue; // no diacritic to restore — the gate owns this literal.

            if (!grouped.TryGetValue(folded, out var list))
                grouped[folded] = list = new List<AccentVariant>();
            list.Add(new AccentVariant(form, freq));
        }

        var buckets = new Dictionary<string, AccentVariant[]>(grouped.Count, StringComparer.Ordinal);
        foreach (var (key, list) in grouped)
        {
            list.Sort(static (a, b) => b.FrequencyPerMillion.CompareTo(a.FrequencyPerMillion));
            buckets[key] = list.ToArray();
        }

        return new AccentIndex(buckets);
    }
}
