using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// Screen capture pump on top of IDXGIOutputDuplication.
//
// Lifecycle. Construct → Start() → FrameArrived fires repeatedly →
// Stop() (or Dispose()) cancels the capture loop and releases the
// duplication, output, adapter and D3D device. Idempotent — Stop on a
// non-started service is a no-op, Dispose calls Stop. One instance =
// one capture session ; restart is done by disposing and rebuilding.
//
// Why DXGI Output Duplication and not Windows.Graphics.Capture. WGC is
// the modern API but the OS draws a yellow notification border around
// the captured surface, and the only way to disable it is the MSIX
// capability `graphicsCaptureWithoutBorder` — which can't be declared
// from an unpackaged desktop app. DXGI Output Duplication is the
// pre-WGC API (Windows 8+) and is not subject to the border. It's
// what HyperHDR / OBS / NVIDIA ShadowPlay use. The full architecture
// rationale lives in docs/architecture--color-science-pipeline--0.1.md
// axis 2 (the chantier that migrated us off WGC).
//
// Threading. The capture loop runs on a dedicated Task spun in Start.
// FrameArrived is raised on that worker thread, never on the caller's
// UI thread. Consumers that need to touch UI marshal themselves via
// DispatcherQueue.TryEnqueue. Matches the prior WGC contract.
//
// HDR. DuplicateOutput1 negotiates a pixel format from the supplied
// priority list (FP16-preferred when the display is in HDR mode,
// BGRA8-preferred for SDR). The negotiated format is read back via
// GetDuplicationDesc and exposed as ActiveFormat for FrameSampler to
// pick its tone-map path. Peak luminance comes from
// IDXGIOutput6::GetDesc1 during the adapter walk.
//
// Cadence. AcquireNextFrame blocks the loop thread until a desktop
// update or timeout. We throttle to ~15 Hz by sleeping the remainder
// of the 66 ms window after each delivered frame — same cadence as
// the AmbientEngine push loop.
//
// Recovery. DXGI_ERROR_ACCESS_LOST fires on desktop switch, mode
// change, or fullscreen exclusive swap ; DXGI_ERROR_ACCESS_DENIED
// and DXGI_ERROR_SESSION_DISCONNECTED fire on secure-desktop transitions
// (UAC, Win+L, password screensaver) and RDP disconnects. All four
// invalidate the IDXGIOutputDuplication. We release it, sleep 2 s,
// re-call DuplicateOutput1, and resume — for as long as the user
// has the engine running. The loop only exits on a fatal device
// error (DEVICE_REMOVED, DEVICE_HUNG) or on cancellation. This is
// the Hyperion.NG DDA grabber pattern : retry forever on transient,
// surface Stopped only on truly fatal.
public sealed class ScreenCaptureService : IDisposable
{
    // ~15 Hz target. Matches the AmbientEngine push cadence so we
    // don't acquire frames the engine never consumes.
    private const int ThrottleIntervalMs = 66;

    // AcquireNextFrame timeout. Short enough that cancellation responds
    // promptly on Stop, long enough that we don't spin the CPU between
    // frames on a static screen.
    private const uint AcquireTimeoutMs = 200;

    // Back-off when AcquireNextFrame returns an unexpected error
    // (anything that isn't S_OK / WAIT_TIMEOUT / ACCESS_LOST /
    // ACCESS_DENIED / SESSION_DISCONNECTED). Keeps a transient
    // driver hiccup from busy-looping.
    private const int ErrorBackoffMs = 500;

    // Sleep between recreate attempts after the duplication has been
    // invalidated (ACCESS_LOST, ACCESS_DENIED, SESSION_DISCONNECTED).
    // Each cause is transient — secure desktop (UAC, Win+L, password
    // screensaver), display mode change, fullscreen exclusive swap,
    // RDP disconnect — and resolves when the user returns. We retry
    // for as long as the engine is running, exiting only on a fatal
    // device error or cancellation. Mirrors Hyperion.NG DDA grabber.
    private const int RecreateBackoffMs = 2_000;

    // Heartbeat rollup cadence — one DeckleVisionSource.Log.Heartbeat
    // emission per window summarising throughput + latency percentiles.
    // 1 s gives a stable fps figure at our ~15 Hz target without flooding
    // the log. Mirrors the pattern in DeckleAmbientSource.Heartbeat.
    private const int HeartbeatIntervalMs = 1_000;

    // Format priorities passed to DuplicateOutput1. The first format
    // the OS can honour wins. HDR sessions prefer FP16 scRGB ; SDR
    // sessions prefer BGRA8 (FP16 still acceptable as a fallback).
    private static readonly uint[] HdrFormats = new[]
    {
        ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT,
        ScreenCaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM,
    };
    private static readonly uint[] SdrFormats = new[]
    {
        ScreenCaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM,
        ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT,
    };

    private readonly object _lock = new();

    // Managed WinRT wrapper around the native D3D11 device. Kept for
    // FrameSampler's existing constructor signature ; the sampler
    // extracts the native pointer via ScreenCaptureInterop.GetD3D11Device.
    private IDirect3DDevice? _device;

    // Native COM pointers — AddRef'd on Start, Released on Stop.
    // _duplicationPtr is also released and re-AddRef'd on ACCESS_LOST.
    private nint _d3dDevicePtr;
    private nint _adapterPtr;
    private nint _output5Ptr;
    private nint _duplicationPtr;

    private Windows.Graphics.SizeInt32 _lastSize;
    private nint _hmon;
    private bool _disposed;

    private DirectXPixelFormat _activeFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private uint _activeDxgiFormat = ScreenCaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM;
    private float _peakLuminance = 80f;
    private bool _isHdrSession;

    // Capture loop task + cancellation. _captureLoopTask is non-null
    // while IsRunning, gated by _lock for visibility from Stop.
    private CancellationTokenSource? _cts;
    private Task? _captureLoopTask;

    private long _frameCount;
    private long _startTimestamp;

    // Acquire timestamp du _duplicationPtr courant, lu au moment du
    // release pour calculer age_ms dans DeckleResourceSource. Réécrit
    // à chaque (re)création de la duplication.
    private long _duplicationAcquiredTicks;

    /// <summary>True when a capture session is currently running.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Total frames delivered since the last Start().</summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);

    /// <summary>The WinRT IDirect3DDevice the duplication is bound to.
    /// Null when the service isn't running. Borrowed by consumers
    /// (FrameSampler) for D3D11 texture allocation — do not Dispose
    /// from outside.</summary>
    public IDirect3DDevice? Device => _device;

    /// <summary>Negotiated pixel format for the current session.
    /// Either <see cref="DirectXPixelFormat.B8G8R8A8UIntNormalized"/>
    /// (SDR) or <see cref="DirectXPixelFormat.R16G16B16A16Float"/>
    /// (HDR). Decided by DuplicateOutput1 based on the priority list
    /// and the OS's current display mode.</summary>
    public DirectXPixelFormat ActiveFormat => _activeFormat;

    /// <summary>True when the primary display reports an HDR colour
    /// space (HDR10 or scRGB) at Start time.</summary>
    public bool IsHdrSession => _isHdrSession;

    /// <summary>Reported peak luminance of the display in nits. 80 nits
    /// (SDR reference white) when HDR is off or unknown. Used by the
    /// FrameSampler tone-map to normalise scRGB FP16 values.</summary>
    public float PeakLuminance => _peakLuminance;

    /// <summary>Size of the captured surface (the source monitor's
    /// resolution). Valid only when <see cref="IsRunning"/> is true.</summary>
    public Windows.Graphics.SizeInt32 ContentSize => _lastSize;

    /// <summary>
    /// Raised on the capture loop's worker thread for every desktop
    /// frame the duplication delivers. The supplied
    /// <see cref="CapturedFrame"/>'s TexturePtr is valid only for the
    /// duration of the handler — do not retain it past return.
    /// </summary>
    public event Action<CapturedFrame>? FrameArrived;

    /// <summary>Raised on the worker thread when the capture stops
    /// after a sustained failure to recreate the duplication (display
    /// disconnected, mode change loop, etc.). Service is already in
    /// stopped state by the time this fires.</summary>
    public event Action? Stopped;

    /// <summary>
    /// Probes whether the running OS supports DXGI Output Duplication.
    /// Returns true on every Windows 8+ desktop session — kept as a
    /// method to preserve the call shape from the previous WGC-based
    /// service (where the WinRT API needed a feature check).
    /// </summary>
    public static bool IsSupported() => true;

    public void Start(string? targetMonitorDeviceName = null)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning) return;

            DeckleVisionSource.Log.ScreenCaptureStarting();

            try
            {
                _hmon = ResolveTargetMonitor(targetMonitorDeviceName);
                if (_hmon == 0)
                {
                    throw new InvalidOperationException(
                        "Primary monitor not found — MonitorFromPoint returned NULL.");
                }

                // Find the DXGI adapter + output5 driving this monitor,
                // capture the HDR state in passing. Throws if no match
                // (display disconnected mid-startup).
                var match = ScreenCaptureInterop.FindDxgiOutputForMonitor(_hmon);
                _adapterPtr    = match.AdapterPtr;
                _output5Ptr    = match.Output5Ptr;
                _isHdrSession  = match.Hdr.IsHdr;
                _peakLuminance = match.Hdr.PeakLuminance;

                // Create the D3D11 device on that specific adapter —
                // mandatory for DuplicateOutput1 (E_INVALIDARG otherwise
                // on multi-GPU laptops where the default adapter
                // doesn't drive the target monitor).
                _device = ScreenCaptureInterop.CreateDirect3DDevice(_adapterPtr);
                _d3dDevicePtr = ScreenCaptureInterop.GetD3D11Device(_device);

                // DuplicateOutput1 with the format priority list that
                // matches the OS's current display mode. The negotiated
                // format is read back via GetDuplicationDesc.
                uint[] formatList = _isHdrSession ? HdrFormats : SdrFormats;
                _duplicationPtr = ScreenCaptureInterop.DuplicateOutput1(
                    _output5Ptr, _d3dDevicePtr, formatList);

                var desc = ScreenCaptureInterop.GetDuplicationDesc(_duplicationPtr);
                _lastSize = new Windows.Graphics.SizeInt32
                {
                    Width  = (int)desc.ModeDesc.Width,
                    Height = (int)desc.ModeDesc.Height,
                };
                _activeDxgiFormat = desc.ModeDesc.Format;
                _activeFormat = _activeDxgiFormat == ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT
                    ? DirectXPixelFormat.R16G16B16A16Float
                    : DirectXPixelFormat.B8G8R8A8UIntNormalized;

                // Sub-provider transverse Resource — acquire de la duplication
                // output. size_bytes=0 parce que c'est un handle de
                // synchronisation, pas une allocation mémoire mesurable.
                _duplicationAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "duplication-output", (long)_duplicationPtr, 0, "capture-loop");

                _frameCount = 0;
                _startTimestamp = Stopwatch.GetTimestamp();

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureLoopTask = Task.Run(() => CaptureLoop(token), token);

                IsRunning = true;

                DeckleVisionSource.Log.CaptureSessionConfigured(
                    (long)_hmon, _lastSize.Width, _lastSize.Height, _activeFormat.ToString(),
                    _isHdrSession ? "on" : "off", _peakLuminance, (int)AcquireTimeoutMs, ThrottleIntervalMs);
                DeckleVisionSource.Log.ScreenCaptureStarted();
            }
            catch (Exception ex)
            {
                DeckleVisionSource.Log.CaptureStartFailed(ex.GetType().Name, ex.Message);
                DeckleVisionSource.Log.CaptureStartFailedDetail(ex.HResult, ex.GetType().Name, ex.Message);

                DisposeInternals();
                throw;
            }
        }
    }

    private nint ResolveTargetMonitor(string? targetMonitorDeviceName)
    {
        if (string.IsNullOrEmpty(targetMonitorDeviceName))
        {
            return ScreenCaptureInterop.GetPrimaryMonitor();
        }

        var resolved = ScreenCaptureInterop.FindMonitorByDeviceName(targetMonitorDeviceName);
        if (resolved != 0)
        {
            DeckleVisionSource.Log.TargetMonitorResolved(targetMonitorDeviceName, (long)resolved);
            return resolved;
        }

        DeckleVisionSource.Log.MonitorNotFound(targetMonitorDeviceName);
        return ScreenCaptureInterop.GetPrimaryMonitor();
    }

    public void Stop()
    {
        Task? loopTask;
        CancellationTokenSource? cts;
        bool wasRunning;
        lock (_lock)
        {
            if (!IsRunning && _duplicationPtr == 0) return;

            wasRunning = IsRunning;
            loopTask = _captureLoopTask;
            cts = _cts;

            // Flip the state first so a concurrent FrameArrived
            // consumer sees IsRunning=false even before the loop
            // actually wraps up.
            IsRunning = false;
            _captureLoopTask = null;
            _cts = null;
        }

        // Cancel the loop outside the lock — Wait might re-enter
        // via Stopped event subscribers.
        try { cts?.Cancel(); } catch { /* best effort */ }
        try { loopTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Expected — cooperative cancellation. Trace l'OCE attendu sur le
            // sub-provider transverse Cancellation : Stop() a explicitement
            // demandé l'arrêt, la boucle a propagé. `age_ms` reflète la durée
            // entière de la session de capture car le worker a tourné de Start
            // jusqu'au moment du cancel.
            long ageMs = _startTimestamp != 0
                ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                : -1;
            DeckleCancellationSource.Log.OperationCancelled(
                "vision-capture", "upstream", (int)ageMs);
        }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.CaptureLoopWaitFailed(ex.GetType().Name, ex.Message);
        }
        try { cts?.Dispose(); } catch { /* best effort */ }

        lock (_lock)
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long durationMs = (endTimestamp - _startTimestamp) * 1000 / Stopwatch.Frequency;
            long frames = Interlocked.Read(ref _frameCount);
            double fpsAvg = durationMs > 0 ? frames * 1000.0 / durationMs : 0.0;
            double durationSec = durationMs / 1000.0;

            DisposeInternals();

            if (wasRunning)
            {
                DeckleVisionSource.Log.ScreenCaptureStopped(frames, durationSec);
                DeckleVisionSource.Log.ScreenCaptureStoppedDetail(frames, durationMs, fpsAvg);
            }
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        long lastDeliveredTicks = 0;
        long throttleTicks = Stopwatch.Frequency * ThrottleIntervalMs / 1000;

        // Heartbeat rollup accumulators — reset every HeartbeatIntervalMs
        // by EmitHeartbeatIfDue. Allocated lazily (capacity 64 covers a
        // 1 s window at ~15 Hz target with margin) and only populated
        // when the Verbose|Heartbeat gate is open. The collection itself
        // is bypassed when the gate is closed — zero alloc on the hot
        // path of a typical session with no listener attached.
        long heartbeatWindowStartTicks = Stopwatch.GetTimestamp();
        int hbAcquired = 0;
        int hbDropped = 0;
        var hbAcquireDurationsUs = new List<long>(64);
        var hbSampleDurationsUs = new List<long>(64);

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
                TryRecreateDuplication(ct);
                if (ct.IsCancellationRequested) break;
                if (_duplicationPtr == 0) continue;
            }

            // Heartbeat gate evaluated once per iteration. When closed,
            // skip the per-tick latency Stopwatch and all per-window
            // collection — the only residual cost is the IsEnabled
            // probe itself plus the throttle/timestamp arithmetic that
            // the loop already does.
            bool heartbeatGateOpen = DeckleVisionSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat);

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
                // Desktop switch, mode change, DWM on/off, fullscreen
                // exclusive swap. Drop the duplication, recreate next
                // iteration.
                DeckleVisionSource.Log.AccessLostRecovering();
                if (_duplicationPtr != 0)
                {
                    long releasedHandle = (long)_duplicationPtr;
                    int ageMs = (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                                       * 1000L / Stopwatch.Frequency);
                    Marshal.Release(_duplicationPtr);
                    _duplicationPtr = 0;
                    DeckleResourceSource.Log.ResourceReleased(
                        "duplication-output", releasedHandle, ageMs, "capture-loop");
                }
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_DENIED ||
                hr == ScreenCaptureInterop.DXGI_ERROR_SESSION_DISCONNECTED)
            {
                // Secure desktop (UAC, Win+L, password screensaver) or
                // session disconnect (RDP, "switch user"). Both are
                // transient — drop the duplication, the next recreate
                // attempt will succeed when the user returns to the
                // interactive desktop.
                DeckleVisionSource.Log.SecureDesktopRecovering(hr);
                if (_duplicationPtr != 0)
                {
                    long releasedHandle = (long)_duplicationPtr;
                    int ageMs = (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                                       * 1000L / Stopwatch.Frequency);
                    Marshal.Release(_duplicationPtr);
                    _duplicationPtr = 0;
                    DeckleResourceSource.Log.ResourceReleased(
                        "duplication-output", releasedHandle, ageMs, "capture-loop");
                }
                continue;
            }

            if (hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_REMOVED ||
                hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_HUNG)
            {
                // Fatal — GPU gone or hung. Surface Stopped so the
                // engine can clean up ; recovery would need a full
                // D3D device rebuild that lives outside this loop.
                DeckleVisionSource.Log.DeviceLost(hr);
                break;
            }

            if (hr != 0)
            {
                // Verbose : the generic backoff path. Used to catch
                // INVALID_CALL transitions during HDR toggle, the
                // 4-duplication NOT_CURRENTLY_AVAILABLE limit, and the
                // UNSUPPORTED corner case (mode change to 8bpp / DWM
                // off). All transient — sleep 500 ms and retry.
                DeckleVisionSource.Log.AcquireFrameFailed(hr, ErrorBackoffMs);
                if (desktopResourcePtr != 0) Marshal.Release(desktopResourcePtr);
                try { Task.Delay(ErrorBackoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    // Stop() a cancel le ct pendant le backoff transient —
                    // sub-provider Cancellation, age_ms relatif à la session.
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
            bool delivered = false;
            long sampleDurationUs = 0;
            try
            {
                long now = Stopwatch.GetTimestamp();
                bool skipForThrottle = lastDeliveredTicks != 0
                                    && (now - lastDeliveredTicks) < throttleTicks;

                if (skipForThrottle)
                {
                    // Honour the cadence cap : release the GPU buffer
                    // without copying it into the consumer's grid.
                    // Counted as dropped in the heartbeat — frame was
                    // acquired but not processed.
                    continue;
                }

                // QI the desktop image to ID3D11Texture2D. AddRef'd ;
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
                    DeckleVisionSource.Log.TextureQueryFailed(ex.GetType().Name, ex.Message);
                    continue;
                }

                // Sub-provider transverse Resource — acquire de la
                // texture frame. Boucle haute fréquence (~15 Hz cible)
                // gated par IsEnabled(Verbose, Resource) côté provider :
                // zéro alloc et zéro WriteEvent quand aucun listener
                // n'écoute. La capture du timestamp est faite ici parce
                // que la release est dans le finally en aval ; on
                // accepte le test gate double (ici + dans le release)
                // pour garder le code linéaire sans state local
                // per-iteration. bytes_per_pixel = 4 (BGRA8) ou 8 (FP16).
                int bytesPerPixel = _activeDxgiFormat == ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT ? 8 : 4;
                int textureSizeBytes = _lastSize.Width * _lastSize.Height * bytesPerPixel;
                long textureAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "d3d11-texture", (long)texturePtr, textureSizeBytes, "capture-loop");

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
                        DeckleVisionSource.Log.FrameConsumerThrew(ex.GetType().Name, ex.Message);
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
                        long releasedTextureHandle = (long)texturePtr;
                        int textureAgeMs = (int)((Stopwatch.GetTimestamp() - textureAcquiredTicks)
                                                  * 1000L / Stopwatch.Frequency);
                        Marshal.Release(texturePtr);
                        DeckleResourceSource.Log.ResourceReleased(
                            "d3d11-texture", releasedTextureHandle, textureAgeMs, "capture-loop");
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
                    hbAcquireDurationsUs.Add(acquireDurationUs);
                    if (delivered)
                    {
                        hbSampleDurationsUs.Add(sampleDurationUs);
                    }
                    hbAcquired++;
                    if (!delivered) hbDropped++;

                    EmitHeartbeatIfDue(
                        ref heartbeatWindowStartTicks, ref hbAcquired, ref hbDropped,
                        hbAcquireDurationsUs, hbSampleDurationsUs);
                }
                else if (hbAcquired > 0 || hbDropped > 0
                      || hbAcquireDurationsUs.Count > 0 || hbSampleDurationsUs.Count > 0)
                {
                    // Gate flipped off mid-window — discard the partial
                    // accumulation so we don't emit a stale fragment on
                    // the next time it flips back on.
                    hbAcquired = 0;
                    hbDropped = 0;
                    hbAcquireDurationsUs.Clear();
                    hbSampleDurationsUs.Clear();
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

    // Reopen the duplication after it was invalidated (ACCESS_LOST,
    // ACCESS_DENIED, SESSION_DISCONNECTED). Retry forever with a 2 s
    // backoff until either DuplicateOutput1 succeeds — meaning the
    // user has returned from the secure desktop / unplugged the
    // headset / cleared the UAC prompt / etc. — or the engine is
    // cancelled. Never returns false ; the only exits are "succeeded"
    // (with _duplicationPtr set) and "cancelled" (with _duplicationPtr
    // still 0 and the caller seeing ct.IsCancellationRequested).
    private void TryRecreateDuplication(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested && _duplicationPtr == 0)
        {
            attempt++;
            try
            {
                uint[] formatList = _isHdrSession ? HdrFormats : SdrFormats;
                _duplicationPtr = ScreenCaptureInterop.DuplicateOutput1(
                    _output5Ptr, _d3dDevicePtr, formatList);

                var desc = ScreenCaptureInterop.GetDuplicationDesc(_duplicationPtr);
                var newSize = new Windows.Graphics.SizeInt32
                {
                    Width  = (int)desc.ModeDesc.Width,
                    Height = (int)desc.ModeDesc.Height,
                };
                if (newSize.Width != _lastSize.Width || newSize.Height != _lastSize.Height)
                {
                    DeckleVisionSource.Log.DuplicationResizeDetected(
                        _lastSize.Width, _lastSize.Height, newSize.Width, newSize.Height);
                    _lastSize = newSize;
                }

                // Sub-provider transverse Resource — re-acquire d'une
                // nouvelle duplication après invalidation. Le handle
                // diffère du précédent (Marshal.Release a déjà été
                // appelé en amont sur l'ancienne valeur, l'event
                // ResourceReleased correspondant a été émis dans le
                // bras ACCESS_LOST / SECURE_DESKTOP de CaptureLoop ou
                // par le finalizer d'attempt précédent ratée).
                _duplicationAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "duplication-output", (long)_duplicationPtr, 0, "capture-loop");

                DeckleVisionSource.Log.DuplicationRecreated(
                    attempt, _lastSize.Width, _lastSize.Height);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DeckleVisionSource.Log.DuplicationRecreateAttemptFailed(
                    attempt, ex.GetType().Name, ex.Message);
                try { Task.Delay(RecreateBackoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    // Stop() a cancel pendant qu'on attendait le prochain
                    // essai de recreate. age_ms relatif à la session — on
                    // n'a pas d'ancre dédiée à TryRecreateDuplication.
                    long ageMs = _startTimestamp != 0
                        ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                        : -1;
                    DeckleCancellationSource.Log.OperationCancelled(
                        "vision-capture", "upstream", (int)ageMs);
                    return;
                }
            }
        }
    }

    private void DisposeInternals()
    {
        if (_duplicationPtr != 0)
        {
            // Sub-provider transverse Resource — release de la duplication
            // sur Stop / Dispose. age calculé depuis le dernier acquire
            // (Start ou TryRecreateDuplication). Émis avant le Release
            // pour ne pas perdre l'event si le Release lève.
            long releasedHandle = (long)_duplicationPtr;
            int ageMs = (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                               * 1000L / Stopwatch.Frequency);
            DeckleResourceSource.Log.ResourceReleased(
                "duplication-output", releasedHandle, ageMs, "capture-loop");
            try { Marshal.Release(_duplicationPtr); } catch { /* best effort */ }
            _duplicationPtr = 0;
        }
        if (_output5Ptr != 0)
        {
            try { Marshal.Release(_output5Ptr); } catch { /* best effort */ }
            _output5Ptr = 0;
        }
        if (_adapterPtr != 0)
        {
            try { Marshal.Release(_adapterPtr); } catch { /* best effort */ }
            _adapterPtr = 0;
        }
        if (_d3dDevicePtr != 0)
        {
            try { Marshal.Release(_d3dDevicePtr); } catch { /* best effort */ }
            _d3dDevicePtr = 0;
        }
        if (_device is not null)
        {
            // IDirect3DDevice implements IDisposable through IClosable in
            // CsWinRT projection. Release here so the underlying D3D11
            // device is freed promptly.
            try { (_device as IDisposable)?.Dispose(); } catch { /* best effort */ }
            _device = null;
        }
        _hmon = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
