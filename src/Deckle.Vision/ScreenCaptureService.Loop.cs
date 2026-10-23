using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    // Runs as an unobserved Task.Run: an exception escaping here would fault
    // the task with IsRunning still true and Stopped never raised, so the
    // body is caught whole and every exit passes through the finally.
    private void CaptureLoop(CancellationToken ct)
    {
        var pacer = new CapturePacer(_targetFrameRateHz, Stopwatch.Frequency);
        int invalidCallRecoveryAttempts = 0;
        long loopStartTicks = Stopwatch.GetTimestamp();
        bool failed = false;
        string exitReason = CaptureStopReasons.Requested;

        // The incidents this loop can leave open: generic AcquireNextFrame
        // failures, a duplication that keeps being lost, a recreate that keeps
        // failing. Each opens one Warning, ends on one log line, and the
        // finally below closes whatever the exit finds still open.
        var incidents = new CaptureIncidents(AcquireFailureIncidentThreshold, Stopwatch.Frequency);

        // Heartbeat accumulators, allocated lazily and filled only while the
        // Verbose|Heartbeat gate is open, so a session with no listener
        // allocates nothing. 320 covers a 5 s window at 60 Hz with margin.
        long heartbeatWindowStartTicks = Stopwatch.GetTimestamp();
        int hbAcquired = 0;
        int hbDropped = 0;
        List<long>? hbAcquireDurationsUs = null;
        List<long>? hbSampleDurationsUs = null;

        bool gatesRead = false;
        long gatesReadTicks = 0;
        bool heartbeatGateOpen = false;
        bool resourceDetailOpen = false;
        bool captureDetailOpen = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 0 after a recovery branch released the duplication.
                // TryRecreateDuplication retries with backoff and returns
                // false only on cancellation or an abandoned recreate, which
                // it has already reported.
                if (_duplicationPtr == 0)
                {
                    bool recreated = TryRecreateDuplication(ct, incidents, out string abandonReason);
                    if (ct.IsCancellationRequested) break;
                    if (!recreated)
                    {
                        failed = true;
                        exitReason = abandonReason;
                        break;
                    }
                }

                long iterationTicks = Stopwatch.GetTimestamp();
                if (!gatesRead
                    || (iterationTicks - gatesReadTicks) * 1000L / Stopwatch.Frequency >= AdmissionRefreshMs)
                {
                    heartbeatGateOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleVisionSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)Keywords.Heartbeat);
                    resourceDetailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleResourceSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)Keywords.Resource);
                    captureDetailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleVisionSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)Keywords.Capture);
                    gatesReadTicks = iterationTicks;
                    gatesRead = true;
                }

                // Checked on every iteration, timeouts and recoveries
                // included, so a static screen reports acquired=0 instead of
                // a silence that reads like a stalled loop.
                if (heartbeatGateOpen)
                {
                    EmitHeartbeatIfDue(
                        ref heartbeatWindowStartTicks, ref hbAcquired, ref hbDropped,
                        hbAcquireDurationsUs ??= new List<long>(320),
                        hbSampleDurationsUs ??= new List<long>(320));
                }
                else
                {
                    // Gate closed: drop any partial window so a reopened
                    // gate does not emit a stale fragment.
                    hbAcquired = 0;
                    hbDropped = 0;
                    hbAcquireDurationsUs?.Clear();
                    hbSampleDurationsUs?.Clear();
                    heartbeatWindowStartTicks = iterationTicks;
                }

                long acquireStartTicks = heartbeatGateOpen ? Stopwatch.GetTimestamp() : 0;

                int hr = ScreenCaptureInterop.AcquireNextFrame(
                    _duplicationPtr,
                    AcquireTimeoutMs,
                    out var frameInfo,
                    out nint desktopResourcePtr);

                if (hr == ScreenCaptureInterop.DXGI_ERROR_WAIT_TIMEOUT)
                {
                    // Static screen: normal, not an error, and proof the
                    // duplication answers again.
                    long timeoutTicks = Stopwatch.GetTimestamp();
                    incidents.AcquireReadable(timeoutTicks);
                    incidents.Healthy(timeoutTicks);
                    continue;
                }

                if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_LOST)
                {
                    invalidCallRecoveryAttempts = 0;
                    // Desktop switch, mode change, DWM on/off, fullscreen
                    // exclusive swap.
                    DeckleVisionSource.Log.AccessLostRecovering();
                    incidents.AccessLost(CaptureLossCauses.AccessLost, Stopwatch.GetTimestamp());
                    ReleaseDuplicationForRecreate();
                    continue;
                }

                if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_DENIED ||
                    hr == ScreenCaptureInterop.DXGI_ERROR_SESSION_DISCONNECTED)
                {
                    invalidCallRecoveryAttempts = 0;
                    // Secure desktop (UAC, Win+L, password screensaver) or
                    // session disconnect (RDP, "switch user"): transient, the
                    // recreate succeeds once the interactive desktop is back.
                    DeckleVisionSource.Log.SecureDesktopRecovering(hr);
                    incidents.AccessLost(CaptureLossCauses.SecureDesktop, Stopwatch.GetTimestamp());
                    ReleaseDuplicationForRecreate();
                    continue;
                }

                if (hr == ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL)
                {
                    invalidCallRecoveryAttempts++;
                    // AcquireNextFrame returns INVALID_CALL while the previous
                    // frame is still owned, and left alone it repeats forever
                    // with no frame reaching the sampler. Release the leaked
                    // frame; if DXGI says there was none, the duplication is
                    // internally inconsistent, so recreate it. No backoff on
                    // this branch: backoff_ms 0.
                    DeckleVisionSource.Log.AcquireFrameFailed(hr, 0);
                    if (invalidCallRecoveryAttempts >= MaxInvalidCallRecoveryAttempts)
                    {
                        DeckleVisionSource.Log.FrameOwnershipRecoveryAbandoned(
                            invalidCallRecoveryAttempts, MaxInvalidCallRecoveryAttempts, hr);
                        failed = true;
                        exitReason = CaptureStopReasons.FrameOwnershipAbandoned;
                        break;
                    }

                    int releaseHr = ScreenCaptureInterop.ReleaseFrame(_duplicationPtr);
                    if (releaseHr == 0)
                    {
                        continue;
                    }

                    if (releaseHr != ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL)
                    {
                        DeckleVisionSource.Log.ReleaseFrameNonZero(releaseHr);
                    }

                    ReleaseDuplicationForRecreate();
                    continue;
                }

                if (hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_REMOVED ||
                    hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_HUNG)
                {
                    // Fatal: the GPU is gone or hung. Stop and raise Stopped;
                    // rebuilding the device is the consumer's call.
                    DeckleVisionSource.Log.DeviceLost(hr);
                    failed = true;
                    exitReason = CaptureStopReasons.DeviceLost;
                    break;
                }

                if (hr != 0)
                {
                    invalidCallRecoveryAttempts = 0;
                    // Any other error is treated as transient: back off, retry.
                    DeckleVisionSource.Log.AcquireFrameFailed(hr, ErrorBackoffMs);
                    incidents.AcquireFailed(hr, Stopwatch.GetTimestamp());
                    if (desktopResourcePtr != 0) Marshal.Release(desktopResourcePtr);
                    try { Task.Delay(ErrorBackoffMs, ct).Wait(ct); }
                    catch (OperationCanceledException)
                    {
                        long ageMs = _startTimestamp != 0
                            ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                            : -1;
                        DeckleCancellationSource.Log.OperationCancelled(
                            CancelledOperations.VisionCapture, CancellationReasons.Upstream, (int)ageMs);
                        break;
                    }
                    continue;
                }

                long readableTicks = Stopwatch.GetTimestamp();
                incidents.AcquireReadable(readableTicks);
                incidents.Healthy(readableTicks);

                // The heartbeat counts every acquired frame; delivered marks
                // the ones the consumer processed without throwing.
                invalidCallRecoveryAttempts = 0;
                bool delivered = false;
                long sampleDurationUs = 0;
                try
                {
                    long now = Stopwatch.GetTimestamp();
                    if (!pacer.TryAdmit(now))
                    {
                        // The finally releases the frame uncopied; the
                        // heartbeat counts it as dropped.
                        continue;
                    }

                    // The duplication contract says the resource backs a
                    // texture; a QI failure drops the frame instead of
                    // killing the loop.
                    nint texturePtr = 0;
                    try
                    {
                        texturePtr = ScreenCaptureInterop.QueryD3D11Texture(desktopResourcePtr);
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref _lastFrameFailure, new FrameFailure(
                            "texture_query", ex.HResult, ex.GetType().Name, ex.Message));
                        if (captureDetailOpen)
                        {
                            DeckleVisionSource.Log.TextureQueryFailedDetail(
                                ex.GetType().Name, ex.Message);
                        }
                        NotifyFrameProcessingFailed();
                        continue;
                    }

                    long textureAcquiredTicks = 0;
                    if (resourceDetailOpen)
                    {
                        int bytesPerPixel = _activeDxgiFormat == ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT ? 8 : 4;
                        int textureSizeBytes = _lastSize.Width * _lastSize.Height * bytesPerPixel;
                        textureAcquiredTicks = Stopwatch.GetTimestamp();
                        DeckleResourceSource.Log.ResourceAcquired(
                            "d3d11-texture", (long)texturePtr, textureSizeBytes, "capture-loop");
                    }

                    try
                    {
                        Interlocked.Increment(ref _frameCount);

                        var capturedFrame = new CapturedFrame(
                            texturePtr:     texturePtr,
                            width:          _lastSize.Width,
                            height:         _lastSize.Height,
                            timestampTicks: now);

                        long sampleStartTicks = heartbeatGateOpen ? Stopwatch.GetTimestamp() : 0;
                        try
                        {
                            FrameArrived?.Invoke(capturedFrame);
                            delivered = true;
                        }
                        catch (Exception ex)
                        {
                            Volatile.Write(ref _lastFrameFailure, new FrameFailure(
                                "frame_consumer", ex.HResult, ex.GetType().Name, ex.Message));
                            if (captureDetailOpen)
                            {
                                DeckleVisionSource.Log.FrameConsumerThrewDetail(
                                    ex.GetType().Name, ex.Message);
                            }
                            NotifyFrameProcessingFailed();
                        }
                        if (heartbeatGateOpen)
                        {
                            long sampleEndTicks = Stopwatch.GetTimestamp();
                            sampleDurationUs = (sampleEndTicks - sampleStartTicks) * 1_000_000L / Stopwatch.Frequency;
                        }
                    }
                    finally
                    {
                        if (texturePtr != 0)
                        {
                            long releasedTextureHandle = resourceDetailOpen ? (long)texturePtr : 0;
                            int textureAgeMs = resourceDetailOpen
                                ? (int)((Stopwatch.GetTimestamp() - textureAcquiredTicks)
                                    * 1000L / Stopwatch.Frequency)
                                : 0;
                            Marshal.Release(texturePtr);
                            if (resourceDetailOpen)
                            {
                                DeckleResourceSource.Log.ResourceReleased(
                                    "d3d11-texture", releasedTextureHandle, textureAgeMs, "capture-loop");
                            }
                        }
                    }
                }
                finally
                {
                    if (desktopResourcePtr != 0) Marshal.Release(desktopResourcePtr);
                    int releaseHr = ScreenCaptureInterop.ReleaseFrame(_duplicationPtr);
                    if (releaseHr != 0 && releaseHr != ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL)
                    {
                        DeckleVisionSource.Log.ReleaseFrameNonZero(releaseHr);
                    }

                    if (heartbeatGateOpen)
                    {
                        long acquireEndTicks = Stopwatch.GetTimestamp();
                        long acquireDurationUs = (acquireEndTicks - acquireStartTicks) * 1_000_000L / Stopwatch.Frequency;
                        (hbAcquireDurationsUs ??= new List<long>(320)).Add(acquireDurationUs);
                        if (delivered)
                        {
                            (hbSampleDurationsUs ??= new List<long>(320)).Add(sampleDurationUs);
                        }
                        hbAcquired++;
                        if (!delivered) hbDropped++;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative cancellation surfacing from a wait: a requested
            // stop, reported by the finally.
        }
        catch (Exception ex)
        {
            failed = true;
            exitReason = CaptureStopReasons.LoopCrashed;
            DeckleVisionSource.Log.CaptureLoopCrashed(ex.HResult, ex.GetType().Name, ex.Message);
        }
        finally
        {
            if (!failed) exitReason = CaptureStopReasons.Requested;
            _loopExitReason = exitReason;
            incidents.AbandonOpen(Stopwatch.GetTimestamp());
            DeckleVisionSource.Log.CaptureLoopEnded(
                failed ? "failed" : "cancelled",
                exitReason,
                Interlocked.Read(ref _frameCount),
                (Stopwatch.GetTimestamp() - loopStartTicks) * 1000L / Stopwatch.Frequency);

            // Raise Stopped only when the loop ended on its own; Stop()
            // cancels the token first. It carries the reason logged above,
            // so the consumer's own milestone names the same cause.
            if (!ct.IsCancellationRequested)
            {
                IsRunning = false;
                try
                {
                    Stopped?.Invoke(exitReason);
                }
                catch (Exception ex)
                {
                    DeckleVisionSource.Log.SubscriberThrew(
                        "stopped", ex.GetType().Name, ex.Message);
                }
            }
        }
    }

    private void NotifyFrameProcessingFailed()
    {
        try
        {
            FrameProcessingFailed?.Invoke();
        }
        catch (Exception ex)
        {
            // Per frame: the handler's failure stays Verbose and governed.
            DeckleVisionSource.Log.FrameConsumerThrewDetail(
                ex.GetType().Name, ex.Message);
        }
    }

    // Sorts the duration buffers in place for the percentiles; one sort per
    // window stays negligible.
    private static void EmitHeartbeatIfDue(
        ref long windowStartTicks,
        ref int acquired,
        ref int dropped,
        List<long> acquireDurationsUs,
        List<long> sampleDurationsUs)
    {
        long now = Stopwatch.GetTimestamp();
        long elapsedMs = (now - windowStartTicks) * 1000L / Stopwatch.Frequency;
        if (elapsedMs < HeartbeatIntervalMs) return;

        long p50Acquire = 0, p95Acquire = 0;
        if (acquireDurationsUs.Count > 0)
        {
            acquireDurationsUs.Sort();
            int count = acquireDurationsUs.Count;
            p50Acquire = acquireDurationsUs[count / 2];
            p95Acquire = acquireDurationsUs[(int)(0.95 * count)];
        }

        long p50Sample = 0, p95Sample = 0;
        if (sampleDurationsUs.Count > 0)
        {
            sampleDurationsUs.Sort();
            int count = sampleDurationsUs.Count;
            p50Sample = sampleDurationsUs[count / 2];
            p95Sample = sampleDurationsUs[(int)(0.95 * count)];
        }

        DeckleVisionSource.Log.Heartbeat(
            (int)elapsedMs, acquired, dropped,
            p50Acquire, p95Acquire, p50Sample, p95Sample);

        windowStartTicks = now;
        acquired = 0;
        dropped = 0;
        acquireDurationsUs.Clear();
        sampleDurationsUs.Clear();
    }

    private void ReleaseDuplicationForRecreate()
    {
        if (_duplicationPtr == 0) return;

        bool resourceDetailOpen = _duplicationAcquiredTicks != 0
            && OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient,
                DeckleResourceSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Resource);
        long releasedHandle = resourceDetailOpen ? (long)_duplicationPtr : 0;
        int ageMs = resourceDetailOpen
            ? (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                * 1000L / Stopwatch.Frequency)
            : 0;
        Marshal.Release(_duplicationPtr);
        _duplicationPtr = 0;
        _duplicationAcquiredTicks = 0;
        if (resourceDetailOpen)
        {
            DeckleResourceSource.Log.ResourceReleased(
                "duplication-output", releasedHandle, ageMs, "capture-loop");
        }
    }

}
