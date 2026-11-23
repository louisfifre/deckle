namespace Deckle.Autocorrect;

// The permanent restricted-English protection tier. The generated FranceTerme
// seed supplies broad technical terminology; this small reviewed overlay covers
// everyday developer literals. A word only the maintainer uses (a product or
// framework name) belongs in the personal dictionary, not here. Several deliberately collide with plausible French
// neighbours (docs/dos, repo/repos, model/modèle): that is exactly why they need
// an explicit literal grant instead of being inferred from spelling distance.
public sealed class GlobalEnglishLexicon : IFrequencyLexicon
{
    private const double OverlayFrequency = 1.0;

    private static readonly HashSet<string> TechnicalLiterals = new(StringComparer.Ordinal)
    {
        "api", "cli", "cpu", "def", "docs", "git", "github", "gpu", "json",
        "logs", "model", "push", "repo", "size", "telemetry", "ui", "ux",
    };

    private readonly IFrequencyLexicon? _generatedSeed;

    public GlobalEnglishLexicon(IFrequencyLexicon? generatedSeed) =>
        _generatedSeed = generatedSeed;

    public bool Contains(string lowerForm) =>
        TechnicalLiterals.Contains(lowerForm) || _generatedSeed?.Contains(lowerForm) == true;

    public double FrequencyOf(string lowerForm)
    {
        double generated = _generatedSeed?.FrequencyOf(lowerForm) ?? 0.0;
        return TechnicalLiterals.Contains(lowerForm)
            ? Math.Max(generated, OverlayFrequency)
            : generated;
    }
}
