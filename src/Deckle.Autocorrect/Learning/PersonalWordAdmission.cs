namespace Deckle.Autocorrect;

// The cleanliness gate in front of personal-vocabulary learning. Recurrence is
// not enough evidence when an ASCII token is also the folded form of a French
// word: repeatedly typing "prepare" must not teach the dictionary to shield it
// from "prépare". Exact French literals remain admissible ("sur" beside
// "sûr" is a real word), as do explicit protected technical literals and
// unknown forms whose only accented collision is below the lexical evidence
// floor.
public sealed class PersonalWordAdmission
{
    // Below this floor an accented neighbour is corpus noise, not enough reason
    // to permanently forbid a recurring user literal. It mirrors the live
    // restorer's minimum evidence for a dominant correction.
    private const double FrenchCollisionFloorPerMillion = 1.0;

    private readonly IFrequencyLexicon _french;
    private readonly AccentIndex _accentIndex;
    private readonly IFrequencyLexicon? _protectedLiterals;

    public PersonalWordAdmission(
        IFrequencyLexicon french,
        AccentIndex accentIndex,
        IFrequencyLexicon? protectedLiterals = null)
    {
        _french = french;
        _accentIndex = accentIndex;
        _protectedLiterals = protectedLiterals;
    }

    public bool Allows(string word)
    {
        string lower = (word ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormC);
        if (lower.Length == 0)
            return false;

        if (_french.Contains(lower))
            return true;

        // An explicit global-English grant outranks a French spelling collision:
        // "model" and "telemetry" are legitimate code-switching literals even
        // beside "modèle" and "télémétrie".
        if (_protectedLiterals?.Contains(lower) == true)
            return true;

        string folded = AccentFolding.Fold(lower);
        foreach (AccentVariant variant in _accentIndex.VariantsOf(folded))
            if (variant.FrequencyPerMillion >= FrenchCollisionFloorPerMillion)
                return false;
        return true;
    }
}
