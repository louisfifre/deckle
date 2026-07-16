using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    private void CaptureLoop(CancellationToken ct)
    {
        long lastDeliveredTicks = 0;
        long throttleTicks = Stopwatch.Frequency * ThrottleIntervalMs / 1000;
        int invalidCallRecoveryAttempts = 0;

        // Heartbeat rollup accumulators — reset every HeartbeatIntervalMs
        // by EmitHeartbeatIfDue. Allocated lazily (capacity 320 covers a
        // 5 s window at our cadence with margin) and only populated
        // when the Verbose|Heartbeat gate is open. The collection itself
        // is bypassed when the gate is closed — zero alloc on the hot
        // path of a typical session with no listener attached.
        long heartbeatWindowStartTicks = Stopwatch.GetTimestamp();
        int hbAcquired = 0;
        int hbDropped = 0;
        List<long>? hbAcquireDurationsUs = null;
        List<long>? hbSampleDurationsUs = null;

        while (!ct.IsCancellationRequested)
        {
            // AcquireNextFrame blocks up to AcquireTimeoutMs. The duplication
            // pointer might be 0 transiently after a recovery handler released
            // it (ACCESS_LOST / ACCESS_DENIED / SESSION_DISCONNECTED) — the
            // recreate helper retries internally with backoff for as long as
            // the engine is running, only returning when DuplicateOutput1
            // succeeds or cancellation fires.
            if (_duplicationPtr == 0)
            {
                bool recreated = TryRecreateDuplication(ct);
                if (ct.IsCancellationRequested) break;
                if (!recreated) break;
            }

            // Heartbeat gate evaluated once per iteration. When closed,
            // skip the per-tick latency Stopwatch and all per-window
            // collection — the only residual cost is the IsEnabled
            // probe itself plus the throttle/timestamp arithmetic that
            // the loop already does.
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
                // Static screen — no new frame in the window. Normal,
                // not an error.
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_LOST)
            {
                invalidCallRecoveryAttempts = 0;
                // Desktop switch, mode change, DWM on/off, fullscreen
                // exclusive swap. Drop the duplication, recreate next
                // iteration.
                DeckleVisionSource.Log.AccessLostRecovering();
                ReleaseDuplicationForRecreate();
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_DENIED ||
                hr == ScreenCaptureInterop.DXGI_ERROR_SESSION_DISCONNECTED)
            {
                invalidCallRecoveryAttempts = 0;
                // Secure desktop (UAC, Win+L, password screensaver) or
                // session disconnect (RDP, "switch user"). Both are
                // transient — drop the duplication, the next recreate
                // attempt will succeed when the user returns to the
                // interactive desktop.
                DeckleVisionSource.Log.SecureDesktopRecovering(hr);
                ReleaseDuplicationForRecreate();
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL)
            {
                invalidCallRecoveryAttempts++;
                // Microsoft documents INVALID_CALL from AcquireNextFrame as
                // "the previous frame is still owned". In the field this can
                // otherwise wedge Ambient forever: Acquire keeps returning
                // INVALID_CALL, the sampler receives no frames, and the push
                // loop drops every tick. First close the leaked ownership; if
                // DXGI says there was no frame to release, the duplication is
                // internally inconsistent, so recreate it.
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
                // Fatal — GPU gone or hung. Surface Stopped so the
                // engine can clean up ; recovery would need a full
                // D3D device rebuild that lives outside this loop.
                DeckleVisionSource.Log.DeviceLostDetail(hr);
                break;
            }

            if (hr != 0)
            {
                invalidCallRecoveryAttempts = 0;
                // Verbose : the generic backoff path. Used to catch
                // the 4-duplication NOT_CURRENTLY_AVAILABLE limit and the
                // UNSUPPORTED corner case (mode change to 8bpp / DWM off).
                // All transient — sleep 500 ms and retry.
                DeckleVisionSource.Log.AcquireFrameFailed(hr, ErrorBackoffMs);
                if (desktopResourcePtr != 0) Marshal.Release(desktopResourcePtr);
                try { Task.Delay(ErrorBackoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    // Stop() cancelled ct during transient backoff:
                    // Cancellation sub-provider, age_ms relative to session.
                    long ageMs = _startTimestamp != 0
                        ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                        : -1;
                    DeckleCancellationSource.Log.OperationCancelled(
                        "vision-capture", "upstream", (int)ageMs);
                    break;
                }
                continue;
            }

            // Heartbeat — acquire path completed successfully. Track
            // the frame regardless of whether it gets delivered to a
            // sampler (throttle-skipped frames and consumer failures
            // still count as "acquired" because the bridge round-trip
            // happened). delivered=true marks the subset that ran the
            // sample path.
            invalidCallRecoveryAttempts = 0;
            bool delivered = false;
            long sampleDurationUs = 0;
            try
            {
                long now = Stopwatch.GetTimestamp();
                bool skipForThrottle = lastDeliveredTicks != 0
                                    && (now - lastDeliveredTicks) < throttleTicks;

                if (skipForThrottle)
                {
                    // Honour the cadence cap: release the GPU buffer
                    // without copying it into the consumer's grid.
                    // Counted as dropped in the heartbeat — frame was
                    // acquired but not processed.
                    continue;
                }

                // QI the desktop image to ID3D11Texture2D. AddRef'd;
                // released in the inner finally. A QI failure here is
                // unusual (the resource is guaranteed to back a texture
                // by the duplication contract) but we wrap to avoid
                // killing the loop on a one-off driver hiccup.
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

                // Cross-cutting Resource sub-provider: frame texture acquire.
                // High-frequency loop (~15 Hz target) gated by
                // IsEnabled(Verbose, Resource) on the provider side: zero alloc
                // and zero WriteEvent when no listener is attached. Timestamp
                // capture is done here because release is in the downstream
                // finally; accept the double gate test (here + in release) to
                // keep code linear without per-iteration local state.
                // bytes_per_pixel = 4 (BGRA8) or 8 (FP16).
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
                    lastDeliveredTicks = now;

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

        // Loop exited — surface Stopped if we didn't get there via a
        // user-triggered Stop() call (which sets IsRunning=false before
        // cancelling the token).
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

    // Rollup emitter — emits one DeckleVisionSource.Log.Heartbeat per
    // HeartbeatIntervalMs window and resets the accumulators. Called at
    // the tail of every loop iteration when the Verbose|Heartbeat gate
    // is open ; the gate is re-checked here for safety but the bulk of
    // the cost (sample collection) is already gated upstream. Sorts
    // both duration buffers in place to pick percentiles — buffer
    // capacity is bounded by the per-window frame count (~15 at the
    // engine push cadence) so the sort cost is negligible.
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
