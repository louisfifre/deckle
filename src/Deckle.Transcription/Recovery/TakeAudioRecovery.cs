using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

/// <param name="Recovered">True when the text of the interrupted take is on the clipboard.</param>
/// <param name="Chars">Length of that text; 0 unless <paramref name="Recovered"/>.</param>
public readonly record struct TakeAudioRecoveryResult(bool Recovered, int Chars = 0);

// ── TakeAudioRecovery ──────────────────────────────────────────────────────
//
// The launch-time decision about the audio a lost take left on disk: run it
// through the engine once, deliver its text, and clean up in the order that
// never loses the only copy. The engine does the transcription and the
// clipboard (RecoverTakeFromAudioAsync, passed as a delegate so this flow can
// be tested without one); the app tells the person once it returns.
//
//   take-audio.attempted present   a previous run set the audio aside and never
//                                  finished, so the app probably died on it.
//                                  Not retried, not deleted: the maintainer can
//                                  look at it. Logged as a warning.
//   take-audio.dat present         set aside as .attempted, read, transcribed.
//     delivered                    the .attempted file and the text recovery
//                                  file go: the audio transcription is the more
//                                  complete copy.
//     failed cleanly, not started  the file goes back to .dat, so the next
//                                  launch tries again.
//     no speech in it              the file goes: nothing more to get from it.
//   anything else                  the caller restores the text recovery file,
//                                  so what the text store saved is not lost.
//
// A crash mid-run leaves .attempted, which is the whole protection against a
// crash loop; the rename happens before anything reads the audio.
//
// Nothing here throws, and nothing logs audio or text, only counts and
// durations.
public sealed class TakeAudioRecovery(
    TakeAudioRecoveryStore audio,
    DictationRecoveryStore text,
    Func<float[], Task<RecoveredTake>> transcribe)
{
    /// <summary>True when a file from an earlier run waits at launch.</summary>
    public bool HasWork => audio.Exists || audio.AttemptedExists;

    public async Task<TakeAudioRecoveryResult> TryRecoverAsync()
    {
        if (audio.AttemptedExists)
        {
            DeckleWhispSource.Log.TakeAudioRecoverySkipped("previous_attempt_crashed", kept: true);
            return default;
        }

        if (!audio.Exists) return default;

        if (!audio.MarkAttempted())
        {
            // Without the marker a crash could not be told from a failure, and
            // the run would repeat at every launch: it does not start.
            DeckleWhispSource.Log.TakeAudioRecoverySkipped("attempt_marker_failed", kept: audio.Exists);
            return default;
        }

        TakeAudioRead read = audio.TryReadAttempted();
        if (read.Status != TakeAudioReadStatus.Restored || read.Samples.Length == 0)
        {
            // Nothing can be done with it, and keeping it would repeat the
            // warning at every launch.
            string reason = read.Status == TakeAudioReadStatus.Restored ? "empty" : "unreadable";
            bool deleted = audio.DeleteAttempted();
            DeckleWhispSource.Log.TakeAudioRecoverySkipped(reason, kept: !deleted);
            return default;
        }

        string workId = WorkId.Next();
        var stopwatch = Stopwatch.StartNew();
        DeckleWhispSource.Log.TakeAudioRecoveryStarted(workId, read.Seconds, read.Records, read.TailLost);

        RecoveredTake result;
        try
        {
            result = await transcribe(read.Samples).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bool kept = audio.RestoreAttempted();
            DeckleWhispSource.Log.TakeAudioRecoveryFailed(
                workId, "exception", ex.GetType().Name, kept, stopwatch.ElapsedMilliseconds);
            return default;
        }

        switch (result.Status)
        {
            case RecoveredTakeStatus.Delivered:
                audio.DeleteAttempted();
                text.Delete();
                DeckleWhispSource.Log.TakeAudioRecovered(
                    workId, result.Chars, read.Seconds, stopwatch.ElapsedMilliseconds);
                return new TakeAudioRecoveryResult(true, result.Chars);

            case RecoveredTakeStatus.NoText:
                bool discarded = audio.DeleteAttempted();
                DeckleWhispSource.Log.TakeAudioRecoveryFailed(
                    workId, "no_text", "", kept: !discarded, stopwatch.ElapsedMilliseconds);
                return default;

            default:
                bool restored = audio.RestoreAttempted();
                DeckleWhispSource.Log.TakeAudioRecoveryFailed(
                    workId,
                    result.Status == RecoveredTakeStatus.NotStarted ? "engine_busy" : "transcription_failed",
                    "",
                    restored,
                    stopwatch.ElapsedMilliseconds);
                return default;
        }
    }
}
