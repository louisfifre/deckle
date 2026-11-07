using System.Diagnostics;

namespace Deckle.Transcription;

// How the engine's work in flight stood when the host asked it to finish.
//   NothingInFlight — the engine was idle: the host can tear down at once.
//   Finished        — a take or a file run was in flight and ended on its own
//                     path, the text delivered the way it always is.
//   TimedOut        — the bound passed first. The host disposes the engine,
//                     which cancels the run; the recovery stores keep what exists.
public enum DrainResult { NothingInFlight, Finished, TimedOut }

public sealed partial class TranscriptionEngine
{
    // ── Shutdown partial — finish the work in flight, then let the host quit ──
    //
    // Dispose is the brutal end: it cancels the capture and the drain at once.
    // A host that quits while a take is recording or transcribing calls this
    // first. It does what a Stop press does, then waits, bounded, for the
    // worker to settle on its own path: transcription of the queued utterances,
    // the rewrite, the clipboard and the paste, exactly as on a normal take.
    //
    // Why the bound is the host's: only the host knows how long a person will
    // accept a quit that has not happened.
    //
    // Past the bound nothing is lost that Dispose does not already keep: the
    // text recovery file is only deleted once the raw text is on the clipboard
    // (TranscriptionEngine.Recovery.cs), and the take's audio file is kept for
    // every ending that did not deliver (TranscriptionEngine.TakeAudio.cs).
    //
    // Threads: blocks the caller. The host calls it from a background thread so
    // that its UI keeps painting the HUD while the engine works.

    // Set once by FinishInFlightWork, under the lifecycle lock, and never
    // cleared: the engine is on its way out. A new take or a queued file batch
    // is refused from then on, so the idle moment the wait looks for cannot be
    // taken by a hotkey press or by the next batch of the file queue.
    private volatile bool _finishing;

    // How often the wait looks at the state again. Stop is only valid from
    // Recording, and a take still in Starting reaches Recording a moment later
    // (the microphone probe): the loop stops it at that moment.
    private const int FINISH_POLL_MS = 25;

    public DrainResult FinishInFlightWork(string workId, TimeSpan bound)
    {
        PipelineState first;
        lock (_lifecycleLock)
        {
            if (_disposed) return DrainResult.NothingInFlight;
            _finishing = true;
            first = (PipelineState)Volatile.Read(ref _state);
        }
        if (first == PipelineState.Idle) return DrainResult.NothingInFlight;

        DeckleWhispSource.Log.ShutdownDrainStarted(workId, first.ToString(), (int)bound.TotalMilliseconds);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            // The same CAS as a hotkey release; losing it means the take is
            // already stopping, which is what we want.
            if ((PipelineState)Volatile.Read(ref _state) == PipelineState.Recording)
                RequestToggle(shouldPaste: false);

            if (_idleEvent.Wait(FINISH_POLL_MS)
                || (PipelineState)Volatile.Read(ref _state) == PipelineState.Disposed)
            {
                DeckleWhispSource.Log.ShutdownDrainFinished(workId, stopwatch.ElapsedMilliseconds);
                return DrainResult.Finished;
            }

            if (stopwatch.Elapsed >= bound)
            {
                DeckleWhispSource.Log.ShutdownDrainTimedOut(
                    workId,
                    ((PipelineState)Volatile.Read(ref _state)).ToString(),
                    stopwatch.ElapsedMilliseconds);
                return DrainResult.TimedOut;
            }
        }
    }
}
