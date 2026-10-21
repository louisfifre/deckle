using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Deckle.Audio.Internal;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Owns only the inner polling loop + post-Stop drain — buffer allocation and
// device open/close stay in MicrophoneCapture.
//
// Exit reason (LoopExit):
//   StopRequested  — cancellation token fired (user-driven Stop in the
//                    orchestrator).
//   CapHit         — the host's MaxRecordingDurationSeconds was reached
//                    inside the loop.
internal enum LoopExit
{
    StopRequested,
    CapHit,
}

// Result of Pump — separated from CaptureResult because MicrophoneCapture
// still builds the telemetry payload and the float buffer from it. The drain
// duration is captured here (the loop owns the post-Stop drain phase).
// ReleaseStage names the first drain call the driver refused (stop or
// unprepare_header), null when both went through; MicrophoneCapture adds the
// close and owns the release incident.
internal readonly record struct PumpResult(
    byte[]      Pcm,
    LoopExit    Exit,
    int         BuffersReceived,
    System.TimeSpan DrainDuration,
    string?     ReleaseStage,
    uint        ReleaseErr);

internal static class WaveInLoop
{
    private const int BYTES_PER_50MS = 16000 * 2 * 50 / 1000; // 1600 bytes
    private const uint WHDR_DONE = 0x00000001;

    // The capture-lag instrumentation block (iter / wait_ms / prev_iter_ms /
    // gc deltas) exists to attribute a cause when the consumer falls behind
    // the producer — see the CaptureLagDetected emission below for how each
    // field decodes. Keep it: the lag is intermittent, so the probe earns its
    // place until the cause is pinned and fixed.
    public static PumpResult Pump(
        IntPtr            hWaveIn,
        IntPtr            hEvent,
        IntPtr[]          hdrPtrs,
        int               nBuffers,
        uint              hdrSize,
        IAudioRecordingHost    host,
        List<float>       rmsLog,            // owned by MicrophoneCapture, cleared at start
        System.Action<float>? audioLevelCallback,
        System.Action<CaptureFrame>? frameCallback, // null = no subscriber → no float conversion
        System.Action?    onLowAudioDetected, // fires once when first 5 s lacked sustained voice
        WaveDeviceIdentity device,            // named by the no-data warning
        CancellationToken ct)
    {
        var allBytes = new Pcm16Buffer(initialCapacity: 16000 * 2);

        // The no-data watchdog reads its own clock, independent of the detail
        // gate, because its warning must reach a default log.
        var dataClock = Stopwatch.StartNew();
        CaptureDataWatchdog dataWatchdog = default;
        bool emptyDoneSinceData = false;

        // Snapshot the cap at recording start so a mid-recording Settings
        // change doesn't shorten or extend a session already in progress.
        int maxDurationSec = host.MaxRecordingDurationSeconds;
        bool capHit = false;
        int buffersReceived = 0;

        // Live low-audio tracker — "did the user speak at a healthy volume
        // at least once in the first 5 s?" phrasing. The warning fires once
        // per recording if the answer is no.
        //
        // Why not just count sub-threshold duration: a short peak (finger
        // snap, breath hit) spikes above -50 dBFS for 50-100 ms, which would
        // reset a naive consecutive counter and hide a genuinely broken mic.
        // Instead we track the positive case — a stretch of ≥200 ms where
        // dBFS stays ≥-45 is strong evidence of real speech (one full
        // syllable on a typical USB mic), and we lock the warning off for
        // the rest of the recording. Peaks are too short to clear 200 ms
        // consecutively, so they can't fake a pass.
        //
        // -45 dBFS leaves headroom for quieter setups (condenser and USB mics
        // at typing distance) while still catching the broken-mic / unplugged
        // / miles-away scenarios.
        const double NormalVoiceDbfsThreshold = -45.0;
        const int    NormalVoiceSustainedMs   = 200;
        const int    WarnAfterSilenceMs       = 5000;
        int  healthyVoiceConsecutiveMs = 0;
        int  recordingMs               = 0;
        bool userVoiceConfirmed        = false;
        bool lowAudioWarned            = false;
        CaptureAnomalyEpisode emptyBufferEpisode = default;
        CaptureAnomalyEpisode captureLagEpisode = default;

        // diagLastIterMs / diagLastIterGcN hold the PREVIOUS iteration's body
        // cost (duration + per-generation GC count), measured around the scan
        // loop only — the wait is excluded (it surfaces in wait_ms instead).
        // The lag check reads them, so a fired line describes the iteration
        // that actually let the buffers pile up.
        long diagIterationCount = 0;
        long diagLastIterMs     = 0;
        int  diagLastIterGc0    = 0;
        int  diagLastIterGc1    = 0;
        int  diagLastIterGc2    = 0;
        Stopwatch? diagWaitWatch = null;
        Stopwatch? diagIterWatch = null;

        // Buffers come back in the order they were queued, so they are handled
        // in that order too, from the oldest outstanding one, never by index.
        // See WaveInBufferRing for why index order swaps chunks after a wrap.
        var ring = new WaveInBufferRing(nBuffers);
        Func<int, bool> isDone = i =>
            (Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]).dwFlags & WHDR_DONE) != 0;

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
            NativeMethods.WaitForSingleObject(hEvent, 100);
            long diagWaitMs = captureDetailEnabled ? diagWaitWatch!.ElapsedMilliseconds : 0;

            if (captureDetailEnabled)
            {
                (diagIterWatch ??= new Stopwatch()).Restart();
                diagIterationCount++;
            }

            int diagIterGc0 = captureDetailEnabled ? System.GC.CollectionCount(0) : 0;
            int diagIterGc1 = captureDetailEnabled ? System.GC.CollectionCount(1) : 0;
            int diagIterGc2 = captureDetailEnabled ? System.GC.CollectionCount(2) : 0;

            // At most one lap per wake, as the old index scan was: a driver that
            // refills a requeued buffer at once must not hold the loop here, away
            // from the cap check and the wait.
            int bufferDoneCount = 0;
            bool gotData = false;
            while (bufferDoneCount < nBuffers && ring.TakeNext(isDone, out int i))
            {
                WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]);
                bufferDoneCount++;
                if (hdr.dwBytesRecorded == 0)
                {
                    emptyDoneSinceData = true;
                    if (emptyBufferEpisode.ObserveFailure() == CaptureAnomalyTransition.Opened)
                    {
                        DeckleAudioSource.Log.EmptyBufferReceived();
                        if (captureDetailEnabled)
                            DeckleAudioSource.Log.EmptyBufferReceivedDetail(i);
                    }
                }
                else
                {
                    if (emptyBufferEpisode.ObserveSuccess() == CaptureAnomalyTransition.Recovered)
                        DeckleAudioSource.Log.EmptyBufferRecovered();

                    ReadOnlySpan<byte> data = allBytes.Append(hdr.lpData, (int)hdr.dwBytesRecorded);
                    double bufferDbfs = EmitSubWindows(data, rmsLog, audioLevelCallback, frameCallback);
                    buffersReceived++;
                    gotData = true;

                    // 16 kHz mono PCM16 = 32 bytes per ms.
                    int bufferMs = data.Length / 32;
                    recordingMs += bufferMs;

                    if (!userVoiceConfirmed)
                    {
                        if (bufferDbfs >= NormalVoiceDbfsThreshold)
                        {
                            healthyVoiceConsecutiveMs += bufferMs;
                            if (healthyVoiceConsecutiveMs >= NormalVoiceSustainedMs)
                            {
                                userVoiceConfirmed = true;
                            }
                        }
                        else
                        {
                            healthyVoiceConsecutiveMs = 0;
                        }
                    }

                    if (!lowAudioWarned && !userVoiceConfirmed && recordingMs >= WarnAfterSilenceMs)
                    {
                        lowAudioWarned = true;
                        DeckleAudioSource.Log.LowAudioDetected();
                        DeckleAudioSource.Log.LowAudioDetectedDetail(
                            recordingMs, NormalVoiceSustainedMs, NormalVoiceDbfsThreshold);
                        onLowAudioDetected?.Invoke();
                    }
                }

                // Requeued right after being taken, so the queue order stays the
                // ring order the cursor assumes.
                hdr.dwFlags &= ~WHDR_DONE;
                Marshal.StructureToPtr(hdr, hdrPtrs[i], fDeleteOld: false);
                NativeMethods.waveInAddBuffer(hWaveIn, hdrPtrs[i], hdrSize);
            }

            // Capture lag — one incident per recording when the ring buffer is
            // really under pressure. With 4 buffers × 50 ms and a
            // 100 ms wait, finding 1-2 buffers WHDR_DONE per iteration is
            // normal; 3+ means the consumer fell at least 150 ms behind the
            // producer. Repetition contributes only to the terminal detail.
            //
            // The detail fields decode the cause:
            //   iter         — iteration index when the lag fires (low → cold-start)
            //   wait_ms      — time spent in WaitForSingleObject (high → GC pause / CPU preemption during sleep)
            //   prev_iter_ms — time the previous scan loop took (high → heavy inline work let buffers pile up)
            //   gcN          — collections during that lagging iteration (non-zero, esp. gen2 → a STW pause caused it; all zero → inline work did)
            if (bufferDoneCount >= 3)
            {
                if (captureLagEpisode.ObserveFailure() == CaptureAnomalyTransition.Opened)
                {
                    DeckleAudioSource.Log.CaptureLagDetected();
                    if (captureDetailEnabled)
                    {
                        DeckleAudioSource.Log.CaptureLagDetectedDetail(
                            bufferDoneCount, diagIterationCount, diagWaitMs, diagLastIterMs,
                            diagLastIterGc0, diagLastIterGc1, diagLastIterGc2);
                    }
                }
            }
            else if (captureLagEpisode.ObserveSuccess() == CaptureAnomalyTransition.Recovered)
            {
                DeckleAudioSource.Log.CaptureLagRecovered();
            }

            // No-data watchdog — the one failure where every waveIn call
            // succeeds: a driver that never completes a buffer (no_buffer), or
            // completes them empty (empty_buffers), leaves the take silent with
            // nothing else noticing. A dead device stops signalling the event,
            // so the 100 ms wait above times out and the loop still lands here.
            long dataElapsedMs = dataClock.ElapsedMilliseconds;
            if (gotData)
            {
                long gapMs = dataWatchdog.MsSinceData(dataElapsedMs);
                if (dataWatchdog.ObserveData(dataElapsedMs) == CaptureAnomalyTransition.Recovered)
                    DeckleAudioSource.Log.AudioDataRecovered(gapMs);
                emptyDoneSinceData = false;
            }
            else if (dataWatchdog.ObserveIdle(dataElapsedMs) == CaptureAnomalyTransition.Opened)
            {
                DeckleAudioSource.Log.NoAudioDataDetected(
                    emptyDoneSinceData ? "empty_buffers" : "no_buffer",
                    dataWatchdog.MsSinceData(dataElapsedMs),
                    device.DeviceId, device.Name);
            }

            // Duration cap — forces a stop as if the user had pressed the
            // hotkey. Audio captured so far still flows through the full
            // pipeline.
            //
            // The orchestrator's user-driven Stop path arrives via the
            // CancellationToken; the cap-hit branch escapes the loop by
            // setting `capHit` and breaking. The orchestrator observes
            // Exit == LoopExit.CapHit on return and runs its
            // Recording → Stopping CAS itself.
            double curSec = allBytes.Count / 32000.0;
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
                diagLastIterGc0 = System.GC.CollectionCount(0) - diagIterGc0;
                diagLastIterGc1 = System.GC.CollectionCount(1) - diagIterGc1;
                diagLastIterGc2 = System.GC.CollectionCount(2) - diagIterGc2;
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

        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleAudioSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Capture))
        {
            if (dataWatchdog.Stalls > 0)
            {
                DeckleAudioSource.Log.NoAudioDataEpisodeDetail(
                    dataWatchdog.Stalls, dataWatchdog.LongestGapMs, dataWatchdog.Recovered);
            }
            if (emptyBufferEpisode.Occurrences > 0)
            {
                DeckleAudioSource.Log.EmptyBufferEpisodeDetail(
                    emptyBufferEpisode.Occurrences, emptyBufferEpisode.Recovered);
            }
            if (captureLagEpisode.Occurrences > 0)
            {
                DeckleAudioSource.Log.CaptureLagEpisodeDetail(
                    captureLagEpisode.Occurrences, captureLagEpisode.Recovered);
            }
        }

        // Drain phase starts here — measured separately from the in-loop
        // recording time so the pipeline timings can show how much of the
        // stop-to-pipeline time is spent draining the mic alone (the 100 ms
        // guard sleep below is the obvious lower bound).
        var drainSw = System.Diagnostics.Stopwatch.StartNew();

        // The release codes were ignored before: a refused stop or unprepare
        // is the first sign of a device this process will not let go of. Only
        // the first refusal is kept; the later ones usually share its cause.
        string? releaseStage = null;
        uint releaseErr = 0;
        uint stopErr = NativeMethods.waveInStop(hWaveIn);
        if (stopErr != 0)
        {
            releaseStage = "stop";
            releaseErr = stopErr;
        }
        Thread.Sleep(100);

        // Continue from the same cursor, in queue order: the partial buffer
        // waveInStop returns can sit at a lower index than the last completed
        // one, and it is the end of the take. Nothing is requeued here, so one
        // lap at most bounds the walk.
        int tailBytes = 0;
        int taken = 0;
        for (; taken < nBuffers && ring.TakeNext(isDone, out int i); taken++)
        {
            WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]);
            if (hdr.dwBytesRecorded > 0)
            {
                tailBytes += (int)hdr.dwBytesRecorded;
                ReadOnlySpan<byte> data = allBytes.Append(hdr.lpData, (int)hdr.dwBytesRecorded);
                // Push the drained tail through the same sub-window mill so
                // _rmsLog covers the full session (the in-loop path stops as
                // soon as the cancellation token fires). The drain has no
                // low-audio tracker, so the returned buffer dBFS is
                // intentionally discarded here.
                _ = EmitSubWindows(data, rmsLog, audioLevelCallback, frameCallback);
            }
        }

        // The cursor stops at the first buffer the driver still holds, and
        // FIFO means every later one is still held too: what the walk did not
        // take is what the stop left pending.
        int pendingBuffers = nBuffers - taken;

        // Every header is unprepared, done or not: the caller frees the
        // buffers right after.
        for (int i = 0; i < nBuffers; i++)
        {
            uint unprepareErr = NativeMethods.waveInUnprepareHeader(hWaveIn, hdrPtrs[i], hdrSize);
            if (unprepareErr != 0 && releaseStage is null)
            {
                releaseStage = "unprepare_header";
                releaseErr = unprepareErr;
            }
        }

        drainSw.Stop();
        DeckleAudioSource.Log.CaptureDrainDetail(drainSw.ElapsedMilliseconds, tailBytes, pendingBuffers);

        return new PumpResult(
            Pcm:             allBytes.ToArray(),
            Exit:            capHit ? LoopExit.CapHit : LoopExit.StopRequested,
            BuffersReceived: buffersReceived,
            DrainDuration:   drainSw.Elapsed,
            ReleaseStage:    releaseStage,
            ReleaseErr:      releaseErr);
    }

    // waveIn delivers 50ms PCM16 buffers (1600 bytes); the sub-window walker
    // below loops at most once per call but keeps the pattern in case the
    // buffer size changes. RMS is linear [0, 1], clamped so a rare overshoot
    // from quantization never escapes the range.
    //
    // Collect side-effect: every sub-window RMS is appended to rmsLog. The
    // accumulation runs unconditionally (independent of audioLevelCallback
    // subscription) so the Stop-time mic-telemetry summary reflects the
    // entire session even when the HUD isn't listening.
    //
    // Returns the whole-buffer dBFS derived from the per-sub-window sumSq this
    // method already accumulates, so the low-audio tracker needs no second scan
    // of the buffer. Each completed waveIn buffer is exactly one sub-window (a
    // 1600-byte / 50 ms buffer == BytesPerSubWin). A partial buffer (< 1600
    // bytes, only possible at stop) yields zero complete sub-windows and floors
    // at -120; that path has no low-audio tracker, so the coarseness is
    // harmless.
    private static double EmitSubWindows(
        ReadOnlySpan<byte>    pcm16,
        List<float>           rmsLog,
        System.Action<float>? audioLevelCallback,
        System.Action<CaptureFrame>? frameCallback)
    {
        const int SubWindowMs      = 50;
        const int BytesPerSubWin   = 16000 * 2 * SubWindowMs / 1000; // 1600 bytes
        const int SamplesPerSubWin = BytesPerSubWin / 2;             // 800 samples

        double bufferSumSq   = 0;   // Σ of each sub-window's sumSq — re-rooted at the end.
        int    bufferSamples = 0;   // total samples covered (complete sub-windows only).
        int offset = 0;
        while (offset + BytesPerSubWin <= pcm16.Length)
        {
            // Only materialise the float sub-window when a Frame subscriber is
            // present — the HUD-only path (AudioLevel) allocates nothing extra.
            // The buffer is a fresh array per sub-window, never an alias of the
            // unmanaged ring buffer, so it is safe to hand off and keep.
            float[]? frameSamples = frameCallback is null ? null : new float[SamplesPerSubWin];

            double sumSq = 0;
            for (int i = 0; i < SamplesPerSubWin; i++)
            {
                short s = (short)(pcm16[offset + i * 2] | (pcm16[offset + i * 2 + 1] << 8));
                double v = s / 32768.0;
                sumSq += v * v;
                // (float)(s/32768.0) is bit-identical to PcmConversion.PcmToFloat's
                // s/32768.0f (2^15 divisor → exact), so frame samples match the
                // buffer the backend ultimately receives.
                if (frameSamples is not null) frameSamples[i] = (float)v;
            }
            bufferSumSq   += sumSq;           // accumulate BEFORE the per-window clamp
            bufferSamples += SamplesPerSubWin; // so the buffer fold is the true RMS
            double rms = System.Math.Sqrt(sumSq / SamplesPerSubWin);
            if (rms > 1.0) rms = 1.0;
            float rmsF = (float)rms;
            rmsLog.Add(rmsF);
            audioLevelCallback?.Invoke(rmsF);
            if (frameSamples is not null) frameCallback!.Invoke(new CaptureFrame(frameSamples, rmsF));
            offset += BytesPerSubWin;
        }

        if (bufferSamples == 0) return -120.0;
        double bufferRms = System.Math.Sqrt(bufferSumSq / bufferSamples);
        return bufferRms > 0 ? 20.0 * System.Math.Log10(bufferRms) : -120.0;
    }
}
