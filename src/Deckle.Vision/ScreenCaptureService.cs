using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// Screen capture pump on top of IDXGIOutputDuplication. One instance is one
// capture session: restart by disposing it and building a new one.
//
// Why DXGI Output Duplication and not Windows.Graphics.Capture. WGC draws a
// yellow notification border around the captured surface, and the only way
// to disable it is the MSIX capability `graphicsCaptureWithoutBorder`,
// which an unpackaged desktop app cannot declare. Output Duplication has no
// border.
//
// Threading. FrameArrived is raised on the capture loop's worker thread,
// never on the caller's UI thread; consumers that touch UI marshal
// themselves through DispatcherQueue.TryEnqueue.
//
// Cadence. CapturePacer admits frames at the target rate; the others are
// released without a copy. The rate is independent of the lamps: the
// AmbientEngine push loop reads the latest sample at its transport's own
// rate.
//
// Recovery. A lost duplication is reopened with backoff (see
// TryRecreateDuplication). Vision reports the technical cause; the
// workflow consumer owns the human terminal error.
public sealed partial class ScreenCaptureService : IDisposable
{
    /// <summary>Delivery rate used when the caller does not choose one.</summary>
    public const int DefaultTargetFrameRateHz = 60;

    private readonly int _targetFrameRateHz;

    // Bounds how quickly Stop is honoured on a static screen without
    // spinning the CPU. It does not cap the delivery rate: AcquireNextFrame
    // returns as soon as a desktop update exists.
    private const uint AcquireTimeoutMs = 200;

    // Keeps a transient driver error from busy-looping the generic failure
    // branch of the capture loop.
    private const int ErrorBackoffMs = 500;

    // About two seconds of consecutive generic AcquireNextFrame failures at
    // ErrorBackoffMs: past a transient glitch, short enough that a frozen
    // lamp has a visible cause.
    private const int AcquireFailureIncidentThreshold = 4;

    // Admission gates are re-read once per second instead of per frame: the
    // reader behind them consults persisted settings, and a verbosity toggle
    // needs no faster response.
    private const int AdmissionRefreshMs = 1_000;

    // Persistent INVALID_CALL means DXGI still thinks Deckle owns a frame
    // despite the recovery path closing ownership and/or recreating the
    // duplication. Bound the loop so Ambient stops instead of emitting a
    // long-lived Verbose storm while never pushing colors.
    private const int MaxInvalidCallRecoveryAttempts = 10;

    // The longer recreate interval keeps a locked session quiet.
    private const int RecreateInitialBackoffMs = 2_000;
    private const int RecreateExtendedBackoffMs = 5_000;
    private const int MaxUnexpectedRecreateAttempts = 5;

    // Matches the Ambient engine heartbeat so the two rollups line up in
    // the log, and keeps a multi-hour session readable.
    private const int HeartbeatIntervalMs = 5_000;

    // Formats offered to DuplicateOutput1, preferred first. Both lists keep
    // B8G8R8A8_UNORM: Microsoft asks for it in every list as the most
    // common desktop format.
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

    // WinRT wrapper of the D3D11 device: the form FrameSampler's
    // constructor takes.
    private IDirect3DDevice? _device;

    // AddRef'd on Start, released on Stop. _duplicationPtr is also
    // released and recreated by every recovery, and is 0 in between.
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

    private CancellationTokenSource? _cts;
    private Task? _captureLoopTask;

    private long _frameCount;
    private long _startTimestamp;

    // True from a successful Start until Stop has released the session. A
    // loop that ended on its own leaves IsRunning false while the device is
    // still held, so Stop keys its release and its stopped line on this.
    private bool _sessionStarted;

    // A CaptureStopReasons value written by the capture loop on exit, read
    // by Stop for the stopped line; null until the loop ends.
    private volatile string? _loopExitReason;

    private FrameFailure? _lastFrameFailure;

    // Stamped only when the Resource acquire event was emitted; 0 skips the
    // matching release event.
    private long _duplicationAcquiredTicks;

    /// <summary>
    /// Creates a capture service that delivers at most
    /// <paramref name="targetFrameRateHz"/> frames per second to
    /// <see cref="FrameArrived"/>. The rate is fixed for the lifetime of
    /// the instance; a different rate means a new instance.
    /// </summary>
    /// <param name="targetFrameRateHz">Target delivery rate in Hz.
    /// Defaults to <see cref="DefaultTargetFrameRateHz"/>. The desktop's
    /// own refresh rate and update activity remain upper bounds.</param>
    public ScreenCaptureService(int targetFrameRateHz = DefaultTargetFrameRateHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFrameRateHz);
        _targetFrameRateHz = targetFrameRateHz;
    }

    public int TargetFrameRateHz => _targetFrameRateHz;

    public bool IsRunning { get; private set; }

    /// <summary>Frames delivered since the last Start().</summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);

    // Last cause behind FrameProcessingFailed, for the consumer's incident
    // milestone; null until a frame is dropped.
    public FrameFailure? LastFrameFailure => Volatile.Read(ref _lastFrameFailure);

    /// <summary>Null when the service isn't running. Borrowed by
    /// FrameSampler: never dispose it from outside.</summary>
    public IDirect3DDevice? Device => _device;

    /// <summary>Either <see cref="DirectXPixelFormat.B8G8R8A8UIntNormalized"/>
    /// (SDR) or <see cref="DirectXPixelFormat.R16G16B16A16Float"/> (HDR);
    /// a change mid-session raises <see cref="FormatChanged"/>.</summary>
    public DirectXPixelFormat ActiveFormat => _activeFormat;

    /// <summary>True when the captured display reports an HDR colour
    /// space (HDR10 or scRGB); refreshed on every recreate.</summary>
    public bool IsHdrSession => _isHdrSession;

    /// <summary>Peak luminance the display reports, in nits; 80 (the SDR
    /// reference white) when it reports none.</summary>
    public float PeakLuminance => _peakLuminance;

    /// <summary>Valid only while <see cref="IsRunning"/> is true.</summary>
    public Windows.Graphics.SizeInt32 ContentSize => _lastSize;

    /// <summary>
    /// Raised on the capture loop's worker thread for each delivered frame.
    /// The frame's TexturePtr is valid only during the handler.
    /// </summary>
    public event Action<CapturedFrame>? FrameArrived;

    /// <summary>
    /// Raised when a captured frame cannot reach or complete consumer
    /// processing. Consumers aggregate these per-frame causes into their own
    /// workflow incident instead of turning each dropped frame into a warning.
    /// </summary>
    public event Action? FrameProcessingFailed;

    /// <summary>Raised on the worker thread when capture ends on its own: a
    /// lost device, a frame-ownership or duplication recovery abandoned,
    /// or a crashed loop. The argument is the <see cref="CaptureStopReasons"/>
    /// value Vision logged for that end. <see cref="IsRunning"/> is already
    /// false when it fires.</summary>
    public event Action<string>? Stopped;

    /// <summary>
    /// Raised on the capture worker thread after a recreate negotiated a
    /// different pixel format (the HDR↔SDR toggle flips FP16↔BGRA8) or a
    /// different surface size; a resize alone raises it too. Either
    /// invalidates the consumer's FrameSampler, which it must rebuild from
    /// the fresh <see cref="ActiveFormat"/> / <see cref="ContentSize"/> /
    /// <see cref="PeakLuminance"/>. Raised on the thread that raises
    /// <see cref="FrameArrived"/>, so the swap cannot race a Process() call.
    /// </summary>
    public event Action? FormatChanged;

    /// <summary>
    /// Always true: Output Duplication exists on every Windows 8+ desktop
    /// session.
    /// </summary>
    public static bool IsSupported() => true;

    /// <summary>
    /// Returns the displays currently attached to the desktop. The result is a
    /// snapshot; call again after a Windows display configuration change.
    /// </summary>
    public static IReadOnlyList<CaptureMonitor> GetAvailableMonitors()
        => ScreenCaptureInterop.EnumerateMonitors()
            .Select(static monitor => new CaptureMonitor(
                monitor.DeviceName,
                monitor.IsPrimary,
                monitor.Width,
                monitor.Height))
            .ToArray();


}
