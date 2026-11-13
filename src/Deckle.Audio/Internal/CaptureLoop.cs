using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Audio.Internal;

// Why the loop ended.
//   StopRequested  the cancellation token fired (a user-driven Stop in the
//                  orchestrator).
//   CapHit         the host's MaxRecordingDurationSeconds was reached.
//   DeviceLost     the device stopped delivering for good (unplugged, disabled,
//                  its format changed): the audio captured so far is kept.
internal enum LoopExit
{
    StopRequested,
    CapHit,
    DeviceLost,
}

// Result of Pump, separate from CaptureResult because MicrophoneCapture still
// builds the telemetry payload from it. The drain duration is measured here
// (the loop owns the post-Stop drain). ReleaseStage names the first call the
// device refused at the end ("stop"), null when all went through; LostCode is
// the error that ended a DeviceLost loop.
internal readonly record struct PumpResult(
    float[]     Pcm,
    long        TotalSamples,
    LoopExit    Exit,
    int         ReadsReceived,
    TimeSpan    DrainDuration,
    string?     ReleaseStage,
    int         ReleaseCode,
    int         LostCode);

// The read-and-drain loop of a take. Owns the timing, the anomaly episodes and
// the post-Stop drain; the device is behind ICaptureEndpoint and the signal
// path behind CapturePipeline, so MicrophoneCapture only opens and closes.
//
// The loop polls: it asks the device how many frames are ready, reads exactly
// those, and sleeps a few milliseconds when none are. It never waits inside the
// device, because a blocking read on a device that stops delivering would never
// return, and then neither the stop request, the duration cap nor the no-data
// watchdog could act. The poll interval is short against the device period
// (10 ms on a shared WASAPI stream), so a packet is read within a few
// milliseconds of its arrival and the level meter follows the voice.
internal static class CaptureLoop
{
    // How long the loop sleeps when no frame is ready. Thread.Sleep rounds up
    // to the system timer, so the real wake can be later; the backlog check
    // below measures what that costs instead of assuming it.
    internal const int PollIntervalMs = 4;

    // Audio found waiting at one wake that counts as the loop having fallen
    // behind. A healthy wake finds one or two device packets (10 to 20 ms);
    // past this the loop was held away (a GC pause, a busy subscriber) long
    // enough that the device buffer was filling. PortAudio does not report
    // data the device discarded, so the backlog is the only sign of it.
    internal const double LagThresholdMs = 60.0;

    // How far past the duration cap the clock may run while the device
    // delivers nothing, before the take ends anyway.
    internal const int CapClockGraceSeconds = 2;

    // Frames read in one call at most, so the conversion buffer stays small
    // and a long backlog is consumed in several passes.
    private const int MaxFramesPerRead = 4800;

    // The capture-lag instrumentation block (iter / wait_ms / prev_iter_ms /
    // gc deltas) exists to attribute a cause when the consumer falls behind
    // the producer, see the CaptureLagDetected emission below for how each
    // field decodes. Keep it: the lag is intermittent, so the probe earns its
    // place until the cause is pinned and fixed.
    public static PumpResult Pump(
        ICaptureEndpoint endpoint,
        IAudioRecordingHost host,
        List<float> rmsLog,                          // owned by MicrophoneCapture, cleared at start
        Action<float>? audioLevelCallback,
        Action<CaptureFrame>? frameCallback,         // null = no subscriber, no frame array allocated
        Action<CaptureLevel>? levelCallback,
        Action? onLowAudioDetected,                  // fires once when the first 5 s lacked sustained voice
        string workId,                               // pairs the anomaly summary with the recording
        CancellationToken ct,
        Action<int>? sleep = null)                   // the poll sleep; a test replaces it to run without waiting
    {
        sleep ??= Thread.Sleep;
        CaptureEndpointInfo info = endpoint.Info;
        int channels = info.Format.Channels;
        double msPerFrame = 1000.0 / info.Format.SampleRate;

        // The low-audio warning is a dictation diagnostic: a take that keeps no
        // audio (a live meter) has no tracker and never raises it.
        LowAudioTracker? lowAudio = host.RetainsAudio ? new LowAudioTracker(host.VoiceLevelDbfs) : null;
        var pipeline = new CapturePipeline(
            info.Format, host.RetainsAudio, rmsLog,
            audioLevelCallback, frameCallback, levelCallback,
            lowAudio,
            onLowAudio: () =>
            {
                DeckleAudioSource.Log.LowAudioDetected();
                DeckleAudioSource.Log.LowAudioDetectedDetail(
                    lowAudio!.RecordingMs, LowAudioTracker.NormalVoiceSustainedMs, lowAudio.VoiceLevelDbfs);
                onLowAudioDetected?.Invoke();
            });

        var buffer = new float[MaxFramesPerRead * channels];

        // The no-data watchdog reads its own clock, independent of the detail
        // gate, because its warning must reach a default log.
        var dataClock = Stopwatch.StartNew();
        CaptureDataWatchdog dataWatchdog = default;

        // Snapshot the cap at recording start so a mid-recording Settings
        // change does not shorten or extend a session already in progress.
        int maxDurationSec = host.MaxRecordingDurationSeconds;
        bool capHit = false;
        int readsReceived = 0;

        CaptureAnomalyEpisode captureLagEpisode = default;
        int lagMaxBehindMs = 0;
        long recordedFrames = 0;

        // diagLastIterMs / diagLastIterGcN hold the PREVIOUS iteration's body
        // cost (duration + per-generation GC count), measured around the reads
        // only, the sleep is excluded (it surfaces in wait_ms instead). The lag
        // check reads them, so a fired line describes the iteration that
        // actually let the frames pile up.
        long diagIterationCount = 0;
        long diagLastIterMs     = 0;
        int  diagLastIterGc0    = 0;
        int  diagLastIterGc1    = 0;
        int  diagLastIterGc2    = 0;
        long diagWaitMs         = 0;
        Stopwatch? diagWaitWatch = null;
        Stopwatch? diagIterWatch = null;

        int lostCode = 0;
        bool deviceLost = false;

        // Reads every frame ready now, in passes of at most MaxFramesPerRead.
        // Returns the frames read, or a negative error code when the device is
        // gone.
        int DrainReady()
        {
            int total = 0;
            while (true)
            {
                int available = endpoint.Available();
                if (available < 0) return available;
                if (available == 0) return total;

                int frames = Math.Min(available, MaxFramesPerRead);
                int read = endpoint.Read(buffer.AsSpan(0, frames * channels), frames);
                if (read != 0) return read < 0 ? read : -read;

                readsReceived++;
                recordedFrames += frames;
                total += frames;
                pipeline.Accept(buffer.AsSpan(0, frames * channels), frames);
            }
        }

        while (!ct.IsCancellationRequested)
        {
            bool captureDetailEnabled = OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleAudioSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Capture);

            int ready = endpoint.Available();
            if (ready < 0)
            {
                deviceLost = true;
                lostCode = ready;
                break;
            }

            if (ready == 0)
            {
                // Nothing yet: the watchdog still runs, so a device that never
                // delivers is noticed, then the loop sleeps.
                long idleMs = dataClock.ElapsedMilliseconds;
                if (dataWatchdog.ObserveIdle(idleMs) == CaptureAnomalyTransition.Opened)
                {
                    DeckleAudioSource.Log.NoAudioDataDetected(
                        "no_data", dataWatchdog.MsSinceData(idleMs),
                        info.Device.EndpointId, info.Device.Name);
                }

                // The cap counts audio, and a device that delivers none would
                // never reach it: a Settings check that relies on its cap to
                // end, with no token to stop it, would then never return. So
                // the cap is also held against the clock, with a grace past it
                // for a device that delivers late.
                if (maxDurationSec > 0 && idleMs >= (maxDurationSec + CapClockGraceSeconds) * 1000L)
                {
                    capHit = true;
                    DeckleAudioSource.Log.DurationCapReached();
                    DeckleAudioSource.Log.DurationCapReachedDetail(pipeline.Seconds, maxDurationSec);
                    break;
                }

                if (captureDetailEnabled) (diagWaitWatch ??= new Stopwatch()).Restart();
                sleep(PollIntervalMs);
                if (captureDetailEnabled) diagWaitMs = diagWaitWatch!.ElapsedMilliseconds;
                continue;
            }

            if (captureDetailEnabled)
            {
                (diagIterWatch ??= new Stopwatch()).Restart();
                diagIterationCount++;
            }

            int diagIterGc0 = captureDetailEnabled ? GC.CollectionCount(0) : 0;
            int diagIterGc1 = captureDetailEnabled ? GC.CollectionCount(1) : 0;
            int diagIterGc2 = captureDetailEnabled ? GC.CollectionCount(2) : 0;

            // Capture lag, one incident per recording when the loop really fell
            // behind: more audio waited at this wake than a healthy poll leaves.
            // Repetition contributes only to the terminal summary.
            //
            // The detail fields decode the cause:
            //   iter         iteration index when the lag fires (low = cold start)
            //   wait_ms      the last sleep (high = the thread was not scheduled back in time)
            //   prev_iter_ms time the previous read pass took (high = heavy inline work let frames pile up)
            //   gcN          collections during that lagging iteration (non-zero, esp. gen2 = a STW pause caused it; all zero = inline work did)
            double behindMs = ready * msPerFrame;
            if (behindMs >= LagThresholdMs)
            {
                if ((int)behindMs > lagMaxBehindMs) lagMaxBehindMs = (int)behindMs;
                if (captureLagEpisode.ObserveFailure() == CaptureAnomalyTransition.Opened)
                {
                    DeckleAudioSource.Log.CaptureLagDetected((int)behindMs);
                    if (captureDetailEnabled)
                    {
                        DeckleAudioSource.Log.CaptureLagDetectedDetail(
                            (int)behindMs, diagIterationCount, diagWaitMs, diagLastIterMs,
                            diagLastIterGc0, diagLastIterGc1, diagLastIterGc2);
                    }
                }
            }
            else if (captureLagEpisode.ObserveSuccess() == CaptureAnomalyTransition.Recovered)
            {
                DeckleAudioSource.Log.CaptureLagRecovered();
            }

            int drained = DrainReady();
            if (drained < 0)
            {
                deviceLost = true;
                lostCode = drained;
                break;
            }

            // Data arrived: the no-data watchdog closes its episode, if one was
            // open, with the length of the gap.
            long dataElapsedMs = dataClock.ElapsedMilliseconds;
            long gapMs = dataWatchdog.MsSinceData(dataElapsedMs);
            if (dataWatchdog.ObserveData(dataElapsedMs) == CaptureAnomalyTransition.Recovered)
                DeckleAudioSource.Log.AudioDataRecovered(gapMs);

            // Duration cap, forces a stop as if the user had pressed the
            // hotkey. Audio captured so far still flows through the full
            // pipeline.
            //
            // The orchestrator's user-driven Stop path arrives via the
            // CancellationToken; the cap-hit branch escapes the loop by
            // setting capHit and breaking. The orchestrator observes
            // Exit == LoopExit.CapHit on return and runs its
            // Recording -> Stopping CAS itself.
            double curSec = pipeline.Seconds;
            if (maxDurationSec > 0 && curSec >= maxDurationSec)
            {
                capHit = true;
                DeckleAudioSource.Log.DurationCapReached();
                DeckleAudioSource.Log.DurationCapReachedDetail(curSec, maxDurationSec);
                break;
            }

            if (captureDetailEnabled)
            {
                diagLastIterMs  = diagIterWatch!.ElapsedMilliseconds;
                diagLastIterGc0 = GC.CollectionCount(0) - diagIterGc0;
                diagLastIterGc1 = GC.CollectionCount(1) - diagIterGc1;
                diagLastIterGc2 = GC.CollectionCount(2) - diagIterGc2;
            }
            else
            {
                diagLastIterMs = 0;
                diagLastIterGc0 = 0;
                diagLastIterGc1 = 0;
                diagLastIterGc2 = 0;
            }
        }

        dataWatchdog.Close(dataClock.ElapsedMilliseconds);
        int recordingMs = (int)(recordedFrames * msPerFrame);

        // The count of what went wrong in this recording, written whatever the
        // verbosity: the warnings above are at most one each, so without this
        // line a recording that ended mid-anomaly shows no end to it.
        if (dataWatchdog.Stalls > 0 || captureLagEpisode.Occurrences > 0)
        {
            DeckleAudioSource.Log.RecordingAnomaliesSummary(
                dataWatchdog.Stalls, dataWatchdog.LongestGapMs, dataWatchdog.Recovered,
                captureLagEpisode.Occurrences, captureLagEpisode.Recovered,
                lagMaxBehindMs, recordingMs, workId);
        }

        // Drain phase starts here, measured separately from the in-loop
        // recording time so the pipeline timings can show how much of the
        // stop-to-pipeline time is spent closing the microphone.
        var drainSw = Stopwatch.StartNew();

        // What the device captured up to now and the loop has not read yet: the
        // loop stopped on the token, not on the data. Read before the stream
        // stops, because a stopped stream refuses reads. A take that hit its cap
        // keeps exactly what it had.
        long framesBeforeTail = recordedFrames;
        int readsBeforeTail = readsReceived;
        if (!deviceLost && !capHit) DrainReady();

        // A refused stop is the first sign of a device this process will not
        // let go of. A device that is already gone refuses it by nature, so it
        // is not asked.
        string? releaseStage = null;
        int releaseCode = 0;
        if (!deviceLost)
        {
            int stopCode = endpoint.Stop();
            if (stopCode != 0)
            {
                releaseStage = "stop";
                releaseCode = stopCode;
            }
        }

        // Flushes the resampler's look-ahead and the last full window.
        pipeline.Finish();

        drainSw.Stop();
        DeckleAudioSource.Log.CaptureDrainDetail(
            drainSw.ElapsedMilliseconds,
            (int)((recordedFrames - framesBeforeTail) * msPerFrame),
            readsReceived - readsBeforeTail);

        return new PumpResult(
            Pcm:           pipeline.ToArray(),
            TotalSamples:  pipeline.SampleCount,
            Exit:          deviceLost ? LoopExit.DeviceLost : capHit ? LoopExit.CapHit : LoopExit.StopRequested,
            ReadsReceived: readsReceived,
            DrainDuration: drainSw.Elapsed,
            ReleaseStage:  releaseStage,
            ReleaseCode:   releaseCode,
            LostCode:      lostCode);
    }
}
