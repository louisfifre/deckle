using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

/// <param name="Recovered">True when the text of the interrupted take is on the clipboard.</param>
/// <param name="Chars">Length of that text; 0 unless <paramref name="Recovered"/>.</param>
/// <param name="Failed">True when audio was waiting and could not be recovered (not run, not readable,
/// run failed). False when nothing was waiting, the audio held no speech, or it was recovered.</param>
public readonly record struct TakeAudioRecoveryResult(bool Recovered, int Chars = 0, bool Failed = false)
{
    internal static TakeAudioRecoveryResult Failure { get; } = new(false, 0, true);
}

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
//                                  look at it. Logged as a warning. After 24 h
//                                  the marker expires: it is deleted (warning)
//                                  so a newer take's audio is not blocked for good.
//   take-audio.dat present         set aside as .attempted, read, transcribed.
//     delivered                    the .attempted file and the text recovery
//                                  file go: the audio transcription is the more
//                                  complete copy.
//     failed cleanly, not started, the file goes back to .dat, so the next
//     or unreadable                launch tries again.
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
    /// <summary>How long an ".attempted" marker blocks the recovery before it expires.</summary>
    internal static readonly TimeSpan AttemptedMarkerLifetime = TimeSpan.FromHours(24);

    /// <summary>True when a file from an earlier run waits at launch.</summary>
    public bool HasWork => audio.Exists || audio.AttemptedExists;

    public async Task<TakeAudioRecoveryResult> TryRecoverAsync()
    {
        if (audio.AttemptedExists)
        {
            // A marker a day old no longer protects anything worth a block: the
            // take it guarded is stale, and a newer take may wait behind it.
            // Without this, one crash would silence the net for good.
            if (audio.AttemptedAge is { } age && age >= AttemptedMarkerLifetime)
            {
                bool dropped = audio.DeleteAttempted();
                DeckleWhispSource.Log.TakeAudioAttemptExpired(age.TotalHours, dropped);
            }

            if (audio.AttemptedExists)
            {
                DeckleWhispSource.Log.TakeAudioRecoverySkipped("previous_attempt_crashed", kept: true);
                return TakeAudioRecoveryResult.Failure;
            }
        }

        if (!audio.Exists) return default;

        if (!audio.MarkAttempted())
        {
            // Without the marker a crash could not be told from a failure, and
            // the run would repeat at every launch: it does not start.
            DeckleWhispSource.Log.TakeAudioRecoverySkipped("attempt_marker_failed", kept: audio.Exists);
            return TakeAudioRecoveryResult.Failure;
        }

        TakeAudioRead read = audio.TryReadAttempted();
        if (read.Status != TakeAudioReadStatus.Restored)
        {
            // An IOException reads the same as a damaged file, and a transient
            // one must not cost the only copy: the file goes back to .dat. A
            // truly damaged file repeats this warning until the next dictation
            // take replaces it.
            bool kept = audio.RestoreAttempted();
            DeckleWhispSource.Log.TakeAudioRecoverySkipped("unreadable", kept);
            return TakeAudioRecoveryResult.Failure;
        }

        if (read.Samples.Length == 0)
        {
            // No record in it: nothing to get from it.
            bool deleted = audio.DeleteAttempted();
            DeckleWhispSource.Log.TakeAudioRecoverySkipped("empty", kept: !deleted);
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
            return TakeAudioRecoveryResult.Failure;
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
                return TakeAudioRecoveryResult.Failure;
        }
    }
}
