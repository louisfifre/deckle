namespace Deckle.Autocorrect;

// The permanent restricted-English protection tier. The generated FranceTerme
// seed supplies broad technical terminology; this small reviewed overlay covers
// the everyday developer literals repeatedly observed in Deckle's local
// correction telemetry. Several deliberately collide with plausible French
// neighbours (docs/dos, repo/repos, model/modèle): that is exactly why they need
// an explicit literal grant instead of being inferred from spelling distance.
public sealed class GlobalEnglishLexicon : IFrequencyLexicon
{
    private const double OverlayFrequency = 1.0;

    private static readonly HashSet<string> TechnicalLiterals = new(StringComparer.Ordinal)
    {
        "anytype", "api", "cli", "codex", "cpu", "def", "docs", "git", "github",
        "gpu", "json", "jsonl", "llm", "logs", "mcp", "model", "onnx", "push",
        "repo", "size", "stp", "telemetry", "ui", "ux", "winui", "xaml",
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
