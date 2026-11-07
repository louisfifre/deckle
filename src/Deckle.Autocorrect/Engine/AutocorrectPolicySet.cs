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
        => CreateCore(
            french, english, accentIndex, context, personal, personalVariants,
            candidateSearchObserver: null);

    internal static AutocorrectPolicySet CreateWithCandidateSearchObserver(
        IFrequencyLexicon french,
        IFrequencyLexicon? english,
        AccentIndex accentIndex,
        IPairDisambiguator? context,
        IPersonalLexicon? personal,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants,
        CandidateSearchObserver candidateSearchObserver)
        => CreateCore(
            french, english, accentIndex, context, personal, personalVariants,
            candidateSearchObserver);

    private static AutocorrectPolicySet CreateCore(
        IFrequencyLexicon french,
        IFrequencyLexicon? english,
        AccentIndex accentIndex,
        IPairDisambiguator? context,
        IPersonalLexicon? personal,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants,
        CandidateSearchObserver? candidateSearchObserver)
    {
        var diacritics = new DiacriticsRestorer(
            french, english, accentIndex, context: context, personal: personal,
            personalVariants: personalVariants);
        var typo = candidateSearchObserver is null
            ? new ConservativeTypoCorrector(
                french, english, personal, accentIndex: accentIndex)
            : new ConservativeTypoCorrector(
                french, english, personal, options: null,
                accentIndex: accentIndex,
                candidateSearchObserver: candidateSearchObserver);

        // Spell-fix first, then accents: a confident keyboard slip may beat a
        // rare accent-only false friend, and each stage leaves the other's
        // words alone.
        return new AutocorrectPolicySet(new CompositeCorrectionPolicy(typo, diacritics));
    }
}