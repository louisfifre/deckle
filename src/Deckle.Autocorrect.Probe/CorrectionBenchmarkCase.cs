namespace Deckle.Autocorrect.Probe;

internal sealed record CorrectionBenchmarkCase(
    string Id,
    string Category,
    int LiteralIndex,
    int GoldIndex,
    string[] Candidates)
{
    public string Literal => Candidates[LiteralIndex];
    public string Gold => Candidates[GoldIndex];
    public bool RequiresCorrection => LiteralIndex != GoldIndex;
}
