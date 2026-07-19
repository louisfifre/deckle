using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private void FeedCorpus(WordCommit commit, string onScreen)
    {
        if (CanCollectText())
            _corpus?.Word(commit.Word, onScreen, commit.Boundary, (long)commit.TimestampMs);
    }

    // Emits one completed corpus sentence on the dedicated dataset, tagged with the
    // current process, its closure (how the run ended) and its per-slot timing. Runs
    // on the input thread (the accumulator is synchronous), so _surface is the live
    // surface that produced the sentence.
    private void EmitText(SentenceCorpus.SentenceRecord rec)
    {
        // Consent is live, not captured when the sentence starts. A reset can
        // close an accumulated run after the user has switched collection off;
        // do not expose that verbatim text to any EventSource listener.
        if (!CanCollectText())
            return;

        DeckleAutocorrectSource.Log.AutocorrectTextRecorded(
            _surface.ProcessName, rec.Typed, rec.Final, rec.History, rec.Closure, rec.Timing);
    }

    // The typing stream records only where correction is live: an editable,
    // non-password (cut upstream), master-on, enrolled surface with text consent.
    // Checked per stroke so a settings flip takes effect immediately; resets
    // (pointer, focus) bypass this gate so a span can never straddle surfaces.
    private bool ShouldFeedStream()
    {
        return _stream is not null && CanCollectText();
    }

    // Emits one closed typing-stream run on the dedicated dataset, tagged with
    // the producing process. Runs on the input thread; consent is re-checked at
    // emission for the same reason as EmitText — a reset can close a span after
    // the user switched collection off.
    private void EmitStreamRun(TypingStream.RunRecord rec)
    {
        if (!CanCollectText())
            return;

        DeckleAutocorrectSource.Log.AutocorrectStreamRecorded(
            _surface.ProcessName, rec.Text, rec.Erased, rec.Closure, rec.Timing);
    }

    // The synchronous decision line of the per-word telemetry: the word, its left
    // context, the outcome, the decisive stage/reason, that stage's candidate pool
    // and safety gauges, and the full per-stage trail. The deferred reranker verdict
    // (when the word becomes an ambiguous slot) joins it later on the same id.
    private static void EmitDecision(long id, string word, IReadOnlyList<string> leftContext, CorrectionTrace trace)
    {
        DeckleAutocorrectSource.Log.AutocorrectDecisionRecorded(
            id,
            word,
            string.Join(' ', leftContext),
            trace.Outcome,
            trace.PrimaryStage,
            trace.PrimaryReason,
            trace.RenderCandidates(),
            trace.RenderGauges(),
            trace.RenderTrail());
    }

    private static bool IsEnabledFor(AutocorrectSettings settings, string processName)
        => processName.Length > 0
        && settings.Apps.TryGetValue(processName, out bool on) && on;

    private bool CanCollectText()
    {
        FocusedSurface surface = _surface;
        if (!surface.IsTextEditable || surface.IsPassword) return false;
        AutocorrectSettings settings = _settings();
        return settings.Enabled
            && IsEnabledFor(settings, surface.ProcessName)
            && _textTelemetry?.Invoke() == true;
    }

    // The user has answered for this app (on or off) — absent means never met.
    private static bool IsDecided(AutocorrectSettings settings, string processName)
        => processName.Length > 0 && settings.Apps.ContainsKey(processName);

    // First would-be correction on a not-yet-decided app raises the enrollment
    // offer; the per-run guard keeps it to a single prompt until the user answers.
    private void MaybeSuggestEnrollment(string processName)
    {
        if (processName.Length == 0 || !_suggested.Add(processName)) return;
        DeckleAutocorrectSource.Log.EnrollmentSuggested(processName);
        EnrollmentSuggested?.Invoke(processName);
    }

    private static bool IsActivityRollupEnabled()
        => DeckleAutocorrectSource.IsActivityDetailEnabled(
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
