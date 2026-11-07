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
// device refused at the end ("stop"), null when all went through; LostHresult
// is the code that ended a DeviceLost loop.
internal readonly record struct PumpResult(
    float[]     Pcm,
    long        TotalSamples,
    LoopExit    Exit,
    int         PacketsReceived,
    TimeSpan    DrainDuration,
    string?     ReleaseStage,
    int         ReleaseHresult,
    int         LostHresult);

// The wait-and-drain loop of a take. Owns the timing, the anomaly episodes and
// the post-Stop drain; the device is behind ICaptureEndpoint and the signal
// path behind CapturePipeline, so MicrophoneCapture only opens and closes.
//
// Event-driven: the loop sleeps on the device's own signal and wakes within
// one device period of the sound, instead of polling a 50 ms buffer. A device
// that stops signalling shows as a timeout, and the no-data watchdog below
// turns that into a warning.
internal static class CaptureLoop
{
    // How long a wait lasts before the loop looks at the clock anyway: the cap,
    // the cancellation and the no-data watchdog all need it. Ten device periods.
    private const int WaitTimeoutMs = 100;

    // Audio found waiting at one wake that counts as the loop having fallen
    // behind. A healthy wake finds one packet (10 ms); the engine's own buffer
    // holds roughly two to three periods, so past this the device has begun to
    // overwrite what was not read, and the Discontinuity flag says it did.
    private const double LagThresholdMs = 30.0;

    // Packets read in one wake at most: a device that refills as fast as it is
    // drained must not hold the loop away from the cap check and the wait.
    private const int MaxPacketsPerWake = 64;

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
        CancellationToken ct)
    {
        CaptureEndpointInfo info = endpoint.Info;
        double packetMsPerFrame = 1000.0 / info.Format.SampleRate;

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

        // The no-data watchdog reads its own clock, independent of the detail
        // gate, because its warning must reach a default log.
        var dataClock = Stopwatch.StartNew();
        CaptureDataWatchdog dataWatchdog = default;
        bool silentSinceData = false;

        // Snapshot the cap at recording start so a mid-recording Settings
        // change does not shorten or extend a session already in progress.
        int maxDurationSec = host.MaxRecordingDurationSeconds;
        bool capHit = false;
        int packetsReceived = 0;

        CaptureAnomalyEpisode silentEpisode = default;
        CaptureAnomalyEpisode captureLagEpisode = default;
        int lagMaxPacketsReady = 0;
        int recordingMs = 0;

        // diagLastIterMs / diagLastIterGcN hold the PREVIOUS iteration's body
        // cost (duration + per-generation GC count), measured around the drain
        // only, the wait is excluded (it surfaces in wait_ms instead). The lag
        // check reads them, so a fired line describes the iteration that
        // actually let the packets pile up.
        long diagIterationCount = 0;
        long diagLastIterMs     = 0;
        int  diagLastIterGc0    = 0;
        int  diagLastIterGc1    = 0;
        int  diagLastIterGc2    = 0;
        Stopwatch? diagWaitWatch = null;
        Stopwatch? diagIterWatch = null;

        // What one wake found, written by the packet handler below.
        int  wakePackets = 0;
        double wakeAudioMs = 0;
        bool wakeData = false;
        bool wakeSilent = false;
        bool wakeDiscontinuity = false;
        bool firstPacket = true;
        long drainedBytes = 0;

        CapturePacketHandler handle = (data, frames, flags) =>
        {
            wakePackets++;
            wakeAudioMs += frames * packetMsPerFrame;
            drainedBytes += data.Length;

            // The first packet of a stream reports a discontinuity by design.
            if ((flags & CapturePacketFlags.Discontinuity) != 0 && !firstPacket)
                wakeDiscontinuity = true;
            firstPacket = false;

            bool silent = (flags & CapturePacketFlags.Silent) != 0;
            if (silent) wakeSilent = true;
            else wakeData = true;

            pipeline.Accept(data, frames, silent);
        };

        int lostHresult = 0;
        bool deviceLost = false;

        while (!ct.IsCancellationRequested)
        {
            bool captureDetailEnabled = OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleAudioSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Capture);

            if (captureDetailEnabled)
            {
                (diagWaitWatch ??= new Stopwatch()).Restart();
            }
            endpoint.WaitForData(WaitTimeoutMs);
            long diagWaitMs = captureDetailEnabled ? diagWaitWatch!.ElapsedMilliseconds : 0;

            if (captureDetailEnabled)
            {
                (diagIterWatch ??= new Stopwatch()).Restart();
                diagIterationCount++;
            }

            int diagIterGc0 = captureDetailEnabled ? GC.CollectionCount(0) : 0;
            int diagIterGc1 = captureDetailEnabled ? GC.CollectionCount(1) : 0;
            int diagIterGc2 = captureDetailEnabled ? GC.CollectionCount(2) : 0;

            wakePackets = 0;
            wakeAudioMs = 0;
            wakeData = false;
            wakeSilent = false;
            wakeDiscontinuity = false;

            PacketReadStatus status = PacketReadStatus.Empty;
            int readHresult = 0;
            while (wakePackets < MaxPacketsPerWake
                   && (status = endpoint.ReadPacket(handle, out readHresult)) == PacketReadStatus.Packet)
            {
            }
            packetsReceived += wakePackets;
            recordingMs += (int)wakeAudioMs;

            if (status is PacketReadStatus.DeviceLost or PacketReadStatus.Failed)
            {
                deviceLost = true;
                lostHresult = readHresult;
                break;
            }

            // Silent packets: Windows hands over silence flagged as such (the
            // device is muted, or the microphone privacy setting blocks it).
            // They are not data for the watchdog below, only an incident of
            // their own.
            if (wakeSilent && !wakeData)
            {
                silentSinceData = true;
                if (silentEpisode.ObserveFailure() == CaptureAnomalyTransition.Opened)
                    DeckleAudioSource.Log.SilentDataReceived();
            }
            else if (wakeData && silentEpisode.ObserveSuccess() == CaptureAnomalyTransition.Recovered)
            {
                DeckleAudioSource.Log.SilentDataRecovered();
            }

            // Capture lag, one incident per recording when the loop really fell
            // behind: audio piled up past the threshold in one wake, or the
            // device reports it dropped some. Repetition contributes only to
            // the terminal summary.
            //
            // The detail fields decode the cause:
            //   iter         iteration index when the lag fires (low = cold start)
            //   wait_ms      time spent waiting on the device (high = GC pause / CPU preemption during sleep)
            //   prev_iter_ms time the previous drain took (high = heavy inline work let packets pile up)
            //   gcN          collections during that lagging iteration (non-zero, esp. gen2 = a STW pause caused it; all zero = inline work did)
            if (wakeAudioMs >= LagThresholdMs || wakeDiscontinuity)
            {
                HotLoops.AudioCaptureLag.Tick();
                if (wakePackets > lagMaxPacketsReady) lagMaxPacketsReady = wakePackets;
                if (captureLagEpisode.ObserveFailure() == CaptureAnomalyTransition.Opened)
                {
                    DeckleAudioSource.Log.CaptureLagDetected(wakePackets, (int)wakeAudioMs);
                    if (captureDetailEnabled)
                    {
                        DeckleAudioSource.Log.CaptureLagDetectedDetail(
                            wakePackets, diagIterationCount, diagWaitMs, diagLastIterMs,
                            diagLastIterGc0, diagLastIterGc1, diagLastIterGc2);
                    }
                }
            }
            else if (captureLagEpisode.ObserveSuccess() == CaptureAnomalyTransition.Recovered)
            {
                DeckleAudioSource.Log.CaptureLagRecovered();
            }

            // No-data watchdog, the failure where every WASAPI call succeeds
            // but the device never signals (no_packet), or only signals
            // silence (silent_packets): the take is then silent with nothing
            // else noticing. A dead device stops signalling the event, so the
            // wait above times out and the loop still lands here.
            long dataElapsedMs = dataClock.ElapsedMilliseconds;
            if (wakeData)
            {
                long gapMs = dataWatchdog.MsSinceData(dataElapsedMs);
                if (dataWatchdog.ObserveData(dataElapsedMs) == CaptureAnomalyTransition.Recovered)
                    DeckleAudioSource.Log.AudioDataRecovered(gapMs);
                silentSinceData = false;
            }
            else if (dataWatchdog.ObserveIdle(dataElapsedMs) == CaptureAnomalyTransition.Opened)
            {
                DeckleAudioSource.Log.NoAudioDataDetected(
                    silentSinceData ? "silent_packets" : "no_packet",
                    dataWatchdog.MsSinceData(dataElapsedMs),
                    info.Device.EndpointId, info.Device.Name);
            }

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
            if (!capHit && maxDurationSec > 0 && curSec >= maxDurationSec)
            {
                capHit = true;
                DeckleAudioSource.Log.DurationCapReached();
                DeckleAudioSource.Log.DurationCapReachedDetail(curSec, maxDurationSec);
                if (captureDetailEnabled)
                {
                    diagLastIterMs = diagIterWatch!.ElapsedMilliseconds;
                }
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

        // The count of what went wrong in this recording, written whatever the
        // verbosity: the warnings above are at most one each, so without this
        // line a recording that ended mid-anomaly shows no end to it.
        if (dataWatchdog.Stalls > 0
            || silentEpisode.Occurrences > 0
            || captureLagEpisode.Occurrences > 0)
        {
            DeckleAudioSource.Log.RecordingAnomaliesSummary(
                dataWatchdog.Stalls, dataWatchdog.LongestGapMs, dataWatchdog.Recovered,
                silentEpisode.Occurrences, silentEpisode.Recovered,
                captureLagEpisode.Occurrences, captureLagEpisode.Recovered,
                lagMaxPacketsReady, recordingMs, workId);
        }

        // Drain phase starts here, measured separately from the in-loop
        // recording time so the pipeline timings can show how much of the
        // stop-to-pipeline time is spent closing the microphone.
        var drainSw = Stopwatch.StartNew();

        // The stop code was ignored on the waveIn path: a refused stop is the
        // first sign of a device this process will not let go of. A device that
        // is already gone refuses it by nature, so it is not asked.
        string? releaseStage = null;
        int releaseHresult = 0;
        if (!deviceLost)
        {
            int stopHresult = endpoint.Stop();
            if (stopHresult != 0)
            {
                releaseStage = "stop";
                releaseHresult = stopHresult;
            }
        }

        // What the device captured up to the stop and had not yet been read.
        // The loop above stopped on the token, not on the data, so the last
        // packet or two can still be waiting.
        long bytesBeforeTail = drainedBytes;
        int tailPackets = 0;
        if (!deviceLost)
        {
            while (tailPackets < MaxPacketsPerWake
                   && endpoint.ReadPacket(handle, out _) == PacketReadStatus.Packet)
            {
                tailPackets++;
            }
        }
        packetsReceived += tailPackets;

        // Flushes the resampler's look-ahead and the last full window.
        pipeline.Finish();

        drainSw.Stop();
        DeckleAudioSource.Log.CaptureDrainDetail(
            drainSw.ElapsedMilliseconds, (int)(drainedBytes - bytesBeforeTail), tailPackets);

        return new PumpResult(
            Pcm:             pipeline.ToArray(),
            TotalSamples:    pipeline.SampleCount,
            Exit:            deviceLost ? LoopExit.DeviceLost : capHit ? LoopExit.CapHit : LoopExit.StopRequested,
            PacketsReceived: packetsReceived,
            DrainDuration:   drainSw.Elapsed,
            ReleaseStage:    releaseStage,
            ReleaseHresult:  releaseHresult,
            LostHresult:     lostHresult);
    }
}
