namespace Deckle.Autocorrect.Probe;

internal enum CorrectionBenchmarkVerdict
{
    CorrectFix,
    CorrectKeep,
    SafeAbstention,
    AbstainedCorrection,
    MissedKeep,
    WrongChange,
    ScoringError,
}

internal sealed record CorrectionBenchmarkResult(
    CorrectionBenchmarkCase Case,
    int? BestIndex,
    double Margin,
    TimeSpan Duration,
    string? AbstainReason)
{
    public string? BestText => BestIndex is int index ? Case.Candidates[index] : null;

    public static CorrectionBenchmarkResult FromOutcome(
        CorrectionBenchmarkCase benchmarkCase,
        SentenceScoringOutcome outcome,
        TimeSpan duration)
    {
        if (outcome.AbstainReason is not null &&
            outcome.AbstainReason != SentenceScoringOutcome.AbstainReasons.BelowMargin)
            return new CorrectionBenchmarkResult(
                benchmarkCase,
                null,
                outcome.Margin,
                duration,
                outcome.AbstainReason);

        if (outcome.Scores.Count != benchmarkCase.Candidates.Length ||
            outcome.Scores.Any(static score => string.IsNullOrEmpty(score.Text) || !double.IsFinite(score.Score)))
            return new CorrectionBenchmarkResult(
                benchmarkCase,
                null,
                outcome.Margin,
                duration,
                SentenceScoringOutcome.AbstainReasons.Error);

        int best = 0;
        for (int i = 1; i < outcome.Scores.Count; i++)
            if (outcome.Scores[i].Score > outcome.Scores[best].Score)
                best = i;

        return new CorrectionBenchmarkResult(benchmarkCase, best, outcome.Margin, duration, null);
    }

    public CorrectionBenchmarkVerdict Verdict(double threshold)
    {
        if (AbstainReason is not null || BestIndex is null)
            return CorrectionBenchmarkVerdict.ScoringError;

        if (!double.IsFinite(Margin) || Margin < threshold)
            return Case.RequiresCorrection
                ? CorrectionBenchmarkVerdict.AbstainedCorrection
                : CorrectionBenchmarkVerdict.SafeAbstention;

        int chosen = BestIndex.Value;
        if (chosen == Case.GoldIndex)
            return Case.RequiresCorrection
                ? CorrectionBenchmarkVerdict.CorrectFix
                : CorrectionBenchmarkVerdict.CorrectKeep;

        if (chosen == Case.LiteralIndex)
            return CorrectionBenchmarkVerdict.MissedKeep;

        return CorrectionBenchmarkVerdict.WrongChange;
    }
}
