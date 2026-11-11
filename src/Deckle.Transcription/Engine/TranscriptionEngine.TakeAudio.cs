using Deckle.Audio;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── TakeAudio partial — the audio of a dictation take, on disk as it is recorded ───
    //
    // The text recovery (TranscriptionEngine.Recovery.cs) only knows what was
    // already transcribed. This copy holds what was SAID: every frame the
    // capture emits goes to a sealed file (TakeAudioRecoveryStore), so a take
    // whose transcription failed, was abandoned or died with the process can be
    // transcribed again at the next launch (TranscriptionEngine.AudioRecovery.cs).
    //
    // Lifecycle, in the order it happens:
    //   1. WorkerRun calls BeginTakeAudio once the take has started. A fresh
    //      file replaces any leftover and the Frame event feeds the recorder. It
    //      is subscribed here, not in a pipeline, so both strategies are covered
    //      and every frame is seen, silence included. It is not hooked inside
    //      the segmented session: file transcription pushes there too.
    //   2. EndTake calls SettleTakeAudio, the one choke point every ending
    //      crosses (FinishTake, the worker's teardown, the crash handler). It
    //      flushes the last block, then decides: delete when the text was
    //      delivered or there was nothing to deliver, keep otherwise.
    //   3. A process crash never reaches step 2, so the file stays: keeping is
    //      the default, deleting is the exception that has to be earned.
    //
    // Scope: live dictation only. A file transcription has its source on disk,
    // and the recovery run is itself fed from this file. A null store switches
    // the whole mechanism off, which is what every engine built without one
    // gets. The audio is personal data: it is never written unsealed and no
    // log line carries it.
    //
    // Threads: the fields below are touched by the worker thread only (BeginTake
    // and EndTake both run there); the recorder handles its own capture-thread
    // access.

    private readonly TakeAudioRecoveryStore? _takeAudio;

    private TakeAudioRecorder? _takeAudioRecorder;
    private Action<CaptureFrame>? _takeAudioFeed;

    private void BeginTakeAudio()
    {
        TakeAudioRecoveryStore? store = _takeAudio;
        if (store is null) return;

        // Null when the disk refused (already warned): the take runs without it.
        TakeAudioRecorder? recorder = TakeAudioRecorder.Start(store);
        if (recorder is null) return;

        Action<CaptureFrame> feed = frame => recorder.Push(frame.Samples);
        _takeAudioRecorder = recorder;
        _takeAudioFeed = feed;
        _capture.Frame += feed;
    }

    // Called by EndTake, after the take's end is logged and before any
    // subscriber hears about it. No-op for a take without a recorder (a file
    // take, a take whose file could not be created, a second call).
    private void SettleTakeAudio(string outcome, string reason)
    {
        TakeAudioRecorder? recorder = _takeAudioRecorder;
        if (recorder is null) return;
        _takeAudioRecorder = null;

        Action<CaptureFrame>? feed = _takeAudioFeed;
        _takeAudioFeed = null;
        if (feed is not null) _capture.Frame -= feed;

        double audioSec = recorder.Complete();
        TakeAudioRecoveryStore store = _takeAudio!;
        if (DiscardsTakeAudio(outcome, reason))
        {
            store.Delete();
            return;
        }

        DeckleWhispSource.Log.TakeAudioKept(outcome, reason, audioSec, store.Length);
    }

    // The audio goes only when nothing is left to recover: the text reached the
    // person, or there was no speech in it. Every other ending keeps it, and a
    // failure reason added later is covered without touching this rule, since
    // it ends a take as "failed" (see IsFailureReason) and a failed take is
    // never discarded.
    internal static bool DiscardsTakeAudio(string outcome, string reason) =>
        outcome == TakeOutcomes.ClipboardOnly
        || (outcome == TakeOutcomes.None
            && reason is TakeEndReasons.NoText or TakeEndReasons.EmptyAudio);
}
