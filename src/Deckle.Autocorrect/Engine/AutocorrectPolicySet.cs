namespace Deckle.Autocorrect;

// The production correction chain. Keeping its construction here prevents tests
// and the app from silently running different policy orders or optional
// knowledge sources.
public sealed record AutocorrectPolicySet(ICorrectionPolicy Policy)
{
    public static AutocorrectPolicySet Create(
        IFrequencyLexicon french,
        IFrequencyLexicon? english,
        AccentIndex accentIndex,
        IPairDisambiguator? context = null,
        IPersonalLexicon? personal = null,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants = null)
    {
        var diacritics = new DiacriticsRestorer(
            french, english, accentIndex, context: context, personal: personal,
            personalVariants: personalVariants);
        var typo = new ConservativeTypoCorrector(
            french, english, personal, accentIndex: accentIndex);

        // Spell-fix first, then accents: a confident keyboard slip may beat a
        // rare accent-only false friend, and each stage leaves the other's
        // words alone.
        return new AutocorrectPolicySet(new CompositeCorrectionPolicy(typo, diacritics));
    }
}