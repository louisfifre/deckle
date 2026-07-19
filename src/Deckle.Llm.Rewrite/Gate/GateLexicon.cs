namespace Deckle.Llm.Rewrite;

// ─── Gate closed classes ─────────────────────────────────────────────────────
//
// The two word lists the gate's rules lean on. Both are deliberately small:
// severe calibration is the framing decision — a false reject costs one
// offer, a false accept would cost the trust the whole corrector is built
// on. Growing either list is a calibration act fed by the offer/verdict
// dataset, not an editing convenience.
//
// Entries are NORMALIZED forms (see GateTokenizer.Normalize): lowercase, no
// diacritics, no apostrophes — "où" and "ou" are one entry, "l'" is "l".

public static class GateLexicon
{
    // Words the rewrite may INSERT: French function words only — words that
    // structure a sentence without carrying content. Auxiliaries ("est",
    // "a", "ont") are deliberately absent: inserting one can create meaning.
    // Punctuation insertion is always allowed and does not go through this
    // list — creating sentence boundaries is the point of the retaille.
    static readonly HashSet<string> _functionWords = new(StringComparer.Ordinal)
    {
        // Articles.
        "le", "la", "les", "un", "une", "des", "du", "au", "aux",
        // Elided forms ("l'", "d'", "j'", "qu'"… — apostrophe stripped).
        "l", "d", "j", "s", "n", "m", "t", "c", "qu", "jusqu",
        // Prepositions.
        "a", "de", "dans", "par", "pour", "en", "sur", "sous", "avec",
        "sans", "chez", "entre", "vers", "depuis", "pendant", "apres",
        "avant", "des",
        // Conjunctions and relatives.
        "et", "ou", "mais", "donc", "or", "ni", "car", "que", "qui",
        "si", "comme", "quand", "lorsque", "puisque", "parce",
        // Pronouns.
        "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles",
        "se", "ce", "cette", "ces", "cet", "cela", "ca", "y",
        "me", "te", "lui", "leur", "moi", "toi", "soi", "dont",
        // Negation.
        "ne", "pas",
    };

    // Words the rewrite may DELETE on their own: typed crutches that carry no
    // content in any position. Words that are only *sometimes* crutches
    // ("bon", "quoi", "enfin", "voilà") are excluded — the gate cannot see
    // which use it is looking at, and severe means they stay.
    static readonly HashSet<string> _fillers = new(StringComparer.Ordinal)
    {
        "euh", "heu", "hum", "hem", "bah", "ben", "hein", "bref",
    };

    // Multi-word crutches the rewrite may delete as a phrase, normalized
    // token by token. Same severity rule as above: only sequences that are
    // crutches in essentially every typed use.
    static readonly string[][] _fillerPhrases =
    {
        new[] { "du", "coup" },
        new[] { "en", "fait" },
        new[] { "en", "gros" },
        new[] { "tu", "vois" },
        new[] { "je", "veux", "dire" },
        new[] { "on", "va", "dire" },
        new[] { "comment", "dire" },
    };

    // Punctuation the rewrite may introduce: French text punctuation only.
    // Formatting characters (*, _, `, #…) are NOT punctuation to this gate —
    // the 2026-07-19 eval measured models decorating offers with markdown
    // bold that the gate then let through as "punctuation insertion".
    static readonly HashSet<char> _insertablePunctuation = new()
    {
        '.', ',', ';', ':', '!', '?', '…',
        '\'', '’', '"', '«', '»', '(', ')',
        '-', '—', '–',
    };

    /// <summary>True when a punctuation run may be inserted (or introduced
    /// by a re-punctuation). Runs are single-character repeats, so the run's
    /// character decides.</summary>
    public static bool IsInsertablePunctuation(string text)
        => text.Length > 0 && _insertablePunctuation.Contains(text[0]);

    public static bool IsFunctionWord(string normalized) => _functionWords.Contains(normalized);

    public static bool IsFiller(string normalized) => _fillers.Contains(normalized);

    /// <summary>Longest filler-phrase length, the alignment's group-deletion
    /// horizon.</summary>
    public static int MaxFillerPhraseLength { get; } = ComputeMaxPhraseLength();

    public static bool IsFillerPhrase(ReadOnlySpan<GateToken> tokens)
    {
        foreach (var phrase in _fillerPhrases)
        {
            if (phrase.Length != tokens.Length) continue;
            bool all = true;
            for (int k = 0; k < phrase.Length; k++)
            {
                if (!string.Equals(phrase[k], tokens[k].Normalized, StringComparison.Ordinal))
                {
                    all = false;
                    break;
                }
            }
            if (all) return true;
        }
        return false;
    }

    static int ComputeMaxPhraseLength()
    {
        int max = 1;
        foreach (var phrase in _fillerPhrases)
            if (phrase.Length > max) max = phrase.Length;
        return max;
    }
}
