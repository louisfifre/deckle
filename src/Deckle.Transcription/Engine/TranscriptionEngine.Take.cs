using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Take partial — the log's start and end of one take ───────────────
    //
    // A take is one dictation or one file of a batch. The engine runs one at a
    // time (one worker), so a single set of fields is enough. BeginTake writes
    // TakeStarted; EndTake writes the one TakeEnded and is idempotent, so the
    // exit paths that race (an early return, the crash handler, the worker's
    // teardown) cannot write two. SettleWorkerToIdle calls EndTake last, which
    // is what guarantees that a take that began always ends in the file.

    private string _takeWorkId = "";
    private System.Diagnostics.Stopwatch? _takeSw;
    private int _takeOpen;

    // Call once _transcriptionId is set: the corpus join key travels on the
    // start line.
    private void BeginTake(string kind)
    {
        // A previous take that never declared its end is closed before the next
        // one opens, so two starts never sit in the log without an end.
        EndTake(TakeOutcomes.Abandoned, TakeEndReasons.WorkerExit);

        _takeWorkId = WorkId.Next();
        _takeSw = System.Diagnostics.Stopwatch.StartNew();
        Volatile.Write(ref _takeOpen, 1);
        DeckleWhispSource.Log.TakeStarted(_takeWorkId, kind, _transcriptionId);
    }

    private void EndTake(string outcome, string reason)
    {
        if (Interlocked.Exchange(ref _takeOpen, 0) == 0) return;
        DeckleWhispSource.Log.TakeEnded(
            _takeWorkId, outcome, reason, _takeSw?.ElapsedMilliseconds ?? 0);
    }

    // Ends the take and tells the HUD, in that order: the log records the
    // outcome before a subscriber can react to it. A reason is required when
    // nothing was delivered; a delivery needs none.
    private void FinishTake(TranscriptionOutcome outcome, string? reason = null)
    {
        EndTake(TakeOutcomeName(outcome, reason), reason ?? TakeEndReasons.Delivered);
        RaiseFinished(outcome);
    }

    // A take that delivered nothing is "failed" when something went wrong and
    // "none" when there was nothing to deliver; the reason decides.
    private static string TakeOutcomeName(TranscriptionOutcome outcome, string? reason) => outcome switch
    {
        TranscriptionOutcome.Pasted        => TakeOutcomes.Pasted,
        TranscriptionOutcome.ClipboardOnly => TakeOutcomes.ClipboardOnly,
        TranscriptionOutcome.SavedToFile   => TakeOutcomes.SavedToFile,
        _ => IsFailureReason(reason) ? TakeOutcomes.Failed : TakeOutcomes.None,
    };

    private static bool IsFailureReason(string? reason) => reason is
        TakeEndReasons.MicrophoneError
        or TakeEndReasons.ModelNotLoaded
        or TakeEndReasons.ModelNotReady
        or TakeEndReasons.TranscriptionFailed
        or TakeEndReasons.Incomplete
        or TakeEndReasons.CopyFailed
        or TakeEndReasons.WriteFailed
        or TakeEndReasons.PipelineCrashed;
}
