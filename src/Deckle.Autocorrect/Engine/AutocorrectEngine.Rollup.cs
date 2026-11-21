using Deckle.Autocorrect;
using Deckle.Diagnostics;
using System.Diagnostics.Tracing;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private static bool IsActivityRollupEnabled()
        => DeckleAutocorrectSource.IsActivitySummaryEnabled(
            EventLevel.Verbose,
            (EventKeywords)Keywords.Heartbeat);

    private void MaybeRollup(double nowMs, bool enabled)
    {
        if (!enabled)
        {
            ResetRollup();
            return;
        }

        if (_rollupStartMs < 0) _rollupStartMs = nowMs;
        if (nowMs - _rollupStartMs < RollupPeriodMs) return;

        if (OperationalLogAdmission.IsEnabled(OperationalLogActivity.Autocorrect))
        {
            DeckleAutocorrectSource.Log.ActivityRollup(
                _rollupCommits, _rollupCorrections, _rollupReEdited, _rollupLearning, _rollupGated);
        }

        ResetRollup(nowMs);
    }

    private void ResetRollup(double startMs = -1)
    {
        _rollupStartMs = startMs;
        _rollupCommits = 0;
        _rollupCorrections = 0;
        _rollupReEdited = 0;
        _rollupLearning = 0;
        _rollupGated = 0;
    }

    private static void LogCorrectionDetail(CorrectionDecision decision, int backspaces)
    {
        if (!OperationalLogAdmission.IsEnabled(OperationalLogActivity.Autocorrect)) return;
        DeckleAutocorrectSource.Log.CorrectionDetail(
            decision.Reason.ToString(),
            decision.Original.Length,
            decision.Replacement.Length,
            backspaces);
    }
}
