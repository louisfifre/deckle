namespace Deckle.Autocorrect.Probe;

// Gold corpus for the pilot IT domain pack: sentences that actually type pack
// vocabulary, where the general corpus never does. Replayed by the domain-pack
// maintenance bench over the effective lexicon (base + pack), never by the
// product quality gate — the runtime does not load packs yet.
//
// The scenarios split along what frequency can and cannot change, so the
// floor-vs-promotion comparison reads directly from the outcome classes:
// protection of a valid pack literal is frequency-independent (keeps_*),
// sole-candidate accent restoration fires on the lexical gate regardless of
// frequency (restore_*), and typo repair toward a pack form must clear the
// instant frequency and dominance bars, where a floor frequency is expected
// to struggle (repair_*).
internal static class AutocorrectItPackCorpus
{
    public static IReadOnlyList<KeyboardScenario> All { get; } =
    [
        // Valid pack literals typed clean stay untouched — the one-way
        // protection the pack grants at any frequency. « push » doubles as
        // the misfire control: without the pack it sits one edit from
        // « pus » and has no protection of its own.
        Keeps("keeps_franglais",
            "On va push la branche avant le rebase."),
        Keeps("keeps_accented_pack_terms",
            "Notre hébergeur reste fiable malgré le flux."),
        // « backend » is in nobody's lexicon (the frwiktionary Lexique
        // categories miss much everyday franglais) — the conservative engine
        // must leave the unknown literal alone.
        Keeps("keeps_unknown_franglais",
            "Le backend tourne sans broncher."),

        Fixes("restore_agregateur",
            "Un agregateur pour tous nos flux.",
            "Un agrégateur pour tous nos flux.",
            ("agregateur", "agrégateur")),
        Fixes("restore_hebergeur",
            "Le nouvel hebergeur est fiable.",
            "Le nouvel hébergeur est fiable.",
            ("hebergeur", "hébergeur")),
        // Contested fold: the typed form's accent variants are the pack's
        // « réinitialise » (floor 0.2) and the base's Morphalou-epsilon
        // « réinitialisé » (0.03) — the rare case where the pack must win
        // a dominance contest, not just exist.
        Fixes("restore_contested_reinitialise",
            "On reinitialise le routeur.",
            "On réinitialise le routeur.",
            ("reinitialise", "réinitialise")),
        // A clean pack literal amid ordinary prose — the pack must not make
        // the engine touch either side of the sentence.
        Keeps("keeps_pack_literal_amid_prose",
            "La brique logicielle est prête."),
        // Typo repair toward a pack form: the transposition's sole lexical
        // candidate is « agrégateur » at the floor frequency — this is where
        // the instant frequency bar decides floor vs promotion.
        Fixes("repair_transposition_toward_pack",
            "Un agrégatuer de flux.",
            "Un agrégateur de flux.",
            ("agrégatuer", "agrégateur")),
        Fixes("base_typos_in_it_prose",
            "Le serveur envoie des donnees fraiches.",
            "Le serveur envoie des données fraîches.",
            ("donnees", "données"), ("fraiches", "fraîches")),
    ];

    private static KeyboardScenario Fixes(
        string name,
        string typed,
        string expected,
        params (string Original, string Replacement)[] corrections) =>
        new(name, typed, expected,
            corrections.Select(pair =>
                new ExpectedCorrection(pair.Original, pair.Replacement)).ToArray());

    private static KeyboardScenario Keeps(string name, string text) =>
        new(name, text, text, []);
}
