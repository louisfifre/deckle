using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    private void CaptureLoop(CancellationToken ct)
    {
        var pacer = new CapturePacer(_targetFrameRateHz, Stopwatch.Frequency);
        int invalidCallRecoveryAttempts = 0;

        // Heartbeat accumulators, allocated lazily and filled only while the
        // Verbose|Heartbeat gate is open, so a session with no listener
        // allocates nothing. 320 covers a 5 s window at 60 Hz with margin.
        long heartbeatWindowStartTicks = Stopwatch.GetTimestamp();
        int hbAcquired = 0;
        int hbDropped = 0;
        List<long>? hbAcquireDurationsUs = null;
        List<long>? hbSampleDurationsUs = null;

        while (!ct.IsCancellationRequested)
        {
            // 0 after a recovery branch released the duplication.
            // TryRecreateDuplication retries with backoff and returns false
            // only on cancellation or an abandoned recreate.
            if (_duplicationPtr == 0)
            {
                bool recreated = TryRecreateDuplication(ct);
                if (ct.IsCancellationRequested) break;
                if (!recreated) break;
            }

            bool heartbeatGateOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient,
                DeckleVisionSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Heartbeat);

            long acquireStartTicks = heartbeatGateOpen ? Stopwatch.GetTimestamp() : 0;

            int hr = ScreenCaptureInterop.AcquireNextFrame(
                _duplicationPtr,
                AcquireTimeoutMs,
                out var frameInfo,
                out nint desktopResourcePtr);

            if (hr == ScreenCaptureInterop.DXGI_ERROR_WAIT_TIMEOUT)
            {
                // Static screen: normal, not an error.
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_LOST)
            {
                invalidCallRecoveryAttempts = 0;
                // Desktop switch, mode change, DWM on/off, fullscreen
                // exclusive swap.
                DeckleVisionSource.Log.AccessLostRecovering();
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
                ReleaseDuplicationForRecreate();
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL)
            {
                invalidCallRecoveryAttempts++;
                // AcquireNextFrame returns INVALID_CALL while the previous
                // frame is still owned, and left alone it repeats forever with
                // no frame reaching the sampler. Release the leaked frame; if
                // DXGI says there was none, the duplication is internally
                // inconsistent, so recreate it.
                DeckleVisionSource.Log.AcquireFrameFailed(hr, ErrorBackoffMs);
                if (invalidCallRecoveryAttempts >= MaxInvalidCallRecoveryAttempts)
                {
                    DeckleVisionSource.Log.FrameOwnershipRecoveryAbandonedDetail(
                        invalidCallRecoveryAttempts, MaxInvalidCallRecoveryAttempts);
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
                DeckleVisionSource.Log.DeviceLostDetail(hr);
                break;
            }

            if (hr != 0)
            {
                invalidCallRecoveryAttempts = 0;
                // Any other error is treated as transient: back off, retry.
                DeckleVisionSource.Log.AcquireFrameFailed(hr, ErrorBackoffMs);
                if (desktopResourcePtr != 0) Marshal.Release(desktopResourcePtr);
                try { Task.Delay(ErrorBackoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    long ageMs = _startTimestamp != 0
                        ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                        : -1;
                    DeckleCancellationSource.Log.OperationCancelled(
                        "vision-capture", "upstream", (int)ageMs);
                    break;
                }
                continue;
            }

            // The heartbeat counts every acquired frame; delivered marks the
            // ones the consumer processed without throwing.
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
                // texture; a QI failure drops the frame instead of killing
                // the loop.
                nint texturePtr = 0;
                try
                {
                    texturePtr = ScreenCaptureInterop.QueryD3D11Texture(desktopResourcePtr);
                }
                catch (Exception ex)
                {
                    if (OperationalLogAdmission.IsScopedDetailEnabled(
                            OperationalLogActivity.Ambient,
                            DeckleVisionSource.Log,
                            EventLevel.Verbose,
                            (EventKeywords)Keywords.Capture))
                    {
                        DeckleVisionSource.Log.TextureQueryFailedDetail(
                            ex.GetType().Name, ex.Message);
                    }
                    NotifyFrameProcessingFailed();
                    continue;
                }

                bool resourceDetailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleResourceSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Resource);
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
                        if (OperationalLogAdmission.IsScopedDetailEnabled(
                                OperationalLogActivity.Ambient,
                                DeckleVisionSource.Log,
                                EventLevel.Verbose,
                                (EventKeywords)Keywords.Capture))
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

                    EmitHeartbeatIfDue(
                        ref heartbeatWindowStartTicks, ref hbAcquired, ref hbDropped,
                        hbAcquireDurationsUs, hbSampleDurationsUs ??= new List<long>(320));
                }
                else if (hbAcquired > 0 || hbDropped > 0
                      || hbAcquireDurationsUs is { Count: > 0 }
                      || hbSampleDurationsUs is { Count: > 0 })
                {
                    // Gate flipped off mid-window — discard the partial
                    // accumulation so we don't emit a stale fragment on
                    // the next time it flips back on.
                    hbAcquired = 0;
                    hbDropped = 0;
                    hbAcquireDurationsUs?.Clear();
                    hbSampleDurationsUs?.Clear();
                    heartbeatWindowStartTicks = Stopwatch.GetTimestamp();
                }
            }
        }

        // Raise Stopped only when the loop ended on its own; Stop()
        // cancels the token first.
        if (!ct.IsCancellationRequested)
        {
            IsRunning = false;
            Stopped?.Invoke();
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
            if (OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleVisionSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Capture))
            {
                DeckleVisionSource.Log.FrameConsumerThrewDetail(
                    ex.GetType().Name, ex.Message);
            }
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
