namespace Deckle.Autocorrect;

// The production correction chain and its sentence-stage probe are one unit.
// Keeping their construction here prevents tests, probes and the app from
// silently running different policy orders or optional knowledge sources.
public sealed record AutocorrectPolicySet(
    ICorrectionPolicy Policy,
    IAmbiguityProbe AmbiguityProbe)
{
    public static AutocorrectPolicySet Create(
        IFrequencyLexicon french,
        IFrequencyLexicon? english,
        AccentIndex accentIndex,
        IPairDisambiguator? context = null,
        IPersonalLexicon? personal = null,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants = null,
        VerbMorphology? verbs = null)
    {
        var diacritics = new DiacriticsRestorer(
            french, english, accentIndex, context: context, personal: personal,
            personalVariants: personalVariants);
        var typo = new ConservativeTypoCorrector(
            french, english, personal, accentIndex: accentIndex, verbs: verbs);

        var policies = new List<ICorrectionPolicy>
        {
            // Apostrophe repair precedes spell-fix so cest cannot collapse to est.
            new ElisionCorrector(french, english, personal),
            // One lexical pass arbitrates morphology and physical slips. A
            // confident slip may beat a rare accent-only false friend; morphology
            // gets priority when subject agreement or a closed accent proof exists.
            typo,
            diacritics,
        };
        if (verbs is not null)
            policies.Add(new GrammarCorrector(verbs, personal));

        return new AutocorrectPolicySet(
            new CompositeCorrectionPolicy(policies.ToArray()),
            new CompositeAmbiguityProbe(diacritics, typo));
    }
}
