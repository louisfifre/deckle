namespace Deckle.Autocorrect.Probe;

internal sealed record CorrectionBenchmarkSummary(
    double Threshold,
    int Total,
    int Correctable,
    int Changes,
    int Fixes,
    int WrongChanges,
    int CorrectKeeps,
    int SafeAbstentions,
    int AbstainedCorrections,
    int MissedKeeps,
    int ScoringErrors)
{
    public int Misses => AbstainedCorrections + MissedKeeps;
    public double ChangePrecision => Changes == 0 ? 1.0 : (double)Fixes / Changes;
    public double CorrectionRecall => Correctable == 0 ? 1.0 : (double)Fixes / Correctable;
    public double UnsafeRate => Total == 0 ? 0.0 : (double)WrongChanges / Total;

    public static CorrectionBenchmarkSummary Create(
        IReadOnlyList<CorrectionBenchmarkResult> results,
        double threshold)
    {
        int correctable = 0;
        int changes = 0;
        int fixes = 0;
        int wrongChanges = 0;
        int correctKeeps = 0;
        int safeAbstentions = 0;
        int abstainedCorrections = 0;
        int missedKeeps = 0;
        int scoringErrors = 0;

        foreach (CorrectionBenchmarkResult result in results)
        {
            if (result.Case.RequiresCorrection)
                correctable++;

            switch (result.Verdict(threshold))
            {
                case CorrectionBenchmarkVerdict.CorrectFix:
                    changes++;
                    fixes++;
                    break;
                case CorrectionBenchmarkVerdict.CorrectKeep:
                    correctKeeps++;
                    break;
                case CorrectionBenchmarkVerdict.SafeAbstention:
                    safeAbstentions++;
                    break;
                case CorrectionBenchmarkVerdict.AbstainedCorrection:
                    abstainedCorrections++;
                    break;
                case CorrectionBenchmarkVerdict.MissedKeep:
                    missedKeeps++;
                    break;
                case CorrectionBenchmarkVerdict.WrongChange:
                    changes++;
                    wrongChanges++;
                    break;
                case CorrectionBenchmarkVerdict.ScoringError:
                    scoringErrors++;
                    break;
            }
        }

        return new CorrectionBenchmarkSummary(
            threshold,
            results.Count,
            correctable,
            changes,
            fixes,
            wrongChanges,
            correctKeeps,
            safeAbstentions,
            abstainedCorrections,
            missedKeeps,
            scoringErrors);
    }
}
