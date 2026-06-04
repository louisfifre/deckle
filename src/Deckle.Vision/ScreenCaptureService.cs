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
// what HyperHDR / OBS / NVIDIA ShadowPlay use.
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
public sealed partial class ScreenCaptureService : IDisposable
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
    // 5 s matches the Ambient engine cadence (DeckleAmbientSource.Heartbeat)
    // so the two rollups line up in the log, and keeps a long session
    // readable — at 1 s a multi-hour capture flooded the window with
    // thousands of lines. fps stays stable at our ~15 Hz target over 5 s.
    private const int HeartbeatIntervalMs = 5_000;

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

    // Acquire timestamp of the current _duplicationPtr, read at release time
    // to compute age_ms in DeckleResourceSource. Rewritten on each duplication
    // (re)creation.
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
    /// Raised on the capture worker thread after a duplication recreate
    /// renegotiated a surface that no longer matches what the consumer's
    /// format-dependent resources were built against — a different pixel
    /// format (the HDR↔SDR desktop toggle, which flips FP16↔BGRA8) or a
    /// different surface size (a display mode / resolution change). The
    /// consumer (AmbientEngine) must rebuild its FrameSampler from the
    /// fresh <see cref="ActiveFormat"/> / <see cref="ContentSize"/> /
    /// <see cref="PeakLuminance"/>. Raised synchronously on the same
    /// worker thread that raises <see cref="FrameArrived"/>, so the
    /// handler is serialised against frame delivery and can swap the
    /// sampler without racing a Process() call. The name keeps the
    /// "format" framing of the diagnosed bug even though a pure resize
    /// (same format) also raises it — both invalidate the sampler.
    /// </summary>
    public event Action? FormatChanged;

    /// <summary>
    /// Probes whether the running OS supports DXGI Output Duplication.
    /// Returns true on every Windows 8+ desktop session — kept as a
    /// method to preserve the call shape from the previous WGC-based
    /// service (where the WinRT API needed a feature check).
    /// </summary>
    public static bool IsSupported() => true;


}
