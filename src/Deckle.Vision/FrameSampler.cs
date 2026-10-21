using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI;
using Deckle.Composition;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// FrameSampler — GPU downsample + CPU readback for ambient-lighting pixel
// analysis. Each frame is copied into mip 0 of an intermediate texture,
// GenerateMips builds the pyramid on the GPU, and only the small target mip
// reaches a staging texture the CPU reads, never the full-size source.
//
// The per-cell grid stays in sRGB bytes; AmbientZoneSampler.SampleZone
// re-linearises it on its own.
//
// Ownership: the IDirect3DDevice is borrowed. The native device and
// immediate context pulled from it are AddRef'd and released in
// DisposeAsync; the WinRT wrapper is never closed here.
//
// Threading: Process() is serialised by _lock so successive frames never
// interleave their Map / Unmap. LatestSample is published and read with
// volatile accesses.
public sealed partial class FrameSampler : IAsyncDisposable
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    private readonly int _targetMip;
    private readonly int _gridCols;
    private readonly int _gridRows;

    private readonly uint _dxgiFormat;
    private readonly bool _isHdr;
    private readonly float _peakLuminance;

    // Ceiling on the rolling content peak, in scRGB units (1.0 = 80 nits),
    // so one freak bright pixel cannot crush the rest of the scene.
    private readonly float _displayPeakScRgb;

    // Rolling peak of recent frames' max-channel scRGB values: the
    // normalisation peak ColorSpace.ScRgbToSrgb tone-maps against.
    private float _contentPeak = 1.0f;

    // Share of the gap between the rolling peak and the current frame's
    // peak that survives one second of release: about 0.633, a half-life
    // of about 1.5 s (0.97 per frame at 15 Hz).
    internal static readonly double ContentPeakRetentionPerSecond = Math.Pow(0.97, 15);

    // Guarded by _lock; 0 before the first processed frame.
    private long _lastProcessedTicks;

    /// <summary>Share of the content-peak release gap kept after
    /// <paramref name="elapsedSeconds"/>. 1 (no release) for a zero or
    /// negative interval, such as the first frame of a session.</summary>
    internal static float ContentPeakRetention(double elapsedSeconds)
        => elapsedSeconds <= 0
            ? 1f
            : (float)Math.Pow(ContentPeakRetentionPerSecond, elapsedSeconds);

    private double _exposureEv = 0.0;

    private nint _d3dDevice;
    private nint _d3dContext;
    private nint _intermediateTex;
    private nint _intermediateSrv;
    private nint _stagingTex;

    private SampledFrame? _latestSample;
    private readonly object _lock = new();
    private bool _disposed;

    // Stamped only when the Resource acquire event was emitted; 0 tells
    // DisposeAsync to skip the matching release event.
    private long _intermediateTexAcquiredTicks;
    private long _stagingTexAcquiredTicks;

    private FrameFailure? _lastFailure;

    public SampledFrame? LatestSample => Volatile.Read(ref _latestSample);

    // Last cause behind a false Process(), for the consumer's incident
    // milestone; the per-frame failure itself stays a governed Verbose.
    public FrameFailure? LastFailure => Volatile.Read(ref _lastFailure);

    public int GridCols => _gridCols;
    public int GridRows => _gridRows;
    public bool IsHdr   => _isHdr;

    /// <summary>Rolling content peak in scRGB units that the tone-map
    /// normalises against. Never below 1.0; stays at 1.0 on an SDR
    /// session.</summary>
    public float ContentPeak => _contentPeak;

    /// <summary>Live-reload entry point for ambient exposure tuning.
    /// Applied on the next frame ; no restart required. EV is
    /// linear-light (one stop = ×2 of brightness).</summary>
    public void SetExposureEv(double exposureEv) => _exposureEv = exposureEv;

    public FrameSampler(
        IDirect3DDevice device,
        SizeInt32 sourceSize,
        DirectXPixelFormat poolFormat,
        float peakLuminance)
    {
        _sourceWidth  = sourceSize.Width;
        _sourceHeight = sourceSize.Height;
        _peakLuminance = peakLuminance > 0 ? peakLuminance : 80f;

        _isHdr = poolFormat == DirectXPixelFormat.R16G16B16A16Float;
        _dxgiFormat = _isHdr
            ? ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT
            : ScreenCaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM;

        _displayPeakScRgb = _isHdr ? MathF.Max(_peakLuminance / 80f, 1f) : 1f;

        (_targetMip, _gridCols, _gridRows) =
            ComputeTargetMip(_sourceWidth, _sourceHeight, targetCols: 30, targetRows: 17);

        _d3dDevice = ScreenCaptureInterop.GetD3D11Device(device);

        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;
            var getImmediateContext = (delegate* unmanaged<nint, nint*, void>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_GetImmediateContext];
            nint ctxPtr;
            getImmediateContext(_d3dDevice, &ctxPtr);
            _d3dContext = ctxPtr;
        }

        _intermediateTex = CreateIntermediateTexture();
        bool resourceDetailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Ambient,
            DeckleResourceSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Resource);
        _intermediateTexAcquiredTicks = resourceDetailOpen ? Stopwatch.GetTimestamp() : 0;
        // size_bytes counts mip 0 only; the full mip chain is about 4/3 of
        // that.
        if (resourceDetailOpen)
        {
            int bytesPerPixel = _isHdr ? 8 : 4;
            DeckleResourceSource.Log.ResourceAcquired(
                "d3d11-texture", (long)_intermediateTex,
                _sourceWidth * _sourceHeight * bytesPerPixel, "frame-sampler");
        }

        _intermediateSrv = CreateIntermediateSrv();
        // An SRV is a view with no memory of its own: size_bytes 0.
        if (resourceDetailOpen)
        {
            DeckleResourceSource.Log.ResourceAcquired(
                "dxgi-resource", (long)_intermediateSrv, 0, "frame-sampler");
        }

        _stagingTex = CreateStagingTexture();
        _stagingTexAcquiredTicks = resourceDetailOpen ? Stopwatch.GetTimestamp() : 0;
        if (resourceDetailOpen)
        {
            int bytesPerPixel = _isHdr ? 8 : 4;
            DeckleResourceSource.Log.ResourceAcquired(
                "d3d11-texture", (long)_stagingTex,
                _gridCols * _gridRows * bytesPerPixel, "frame-sampler");
        }

        DeckleVisionSource.Log.SamplerInitialized(
            _gridCols, _gridRows, _targetMip,
            _isHdr ? "scrgb_to_srgb" : "none", _peakLuminance);
    }

    // Stops before the halving that would drop below the target on either
    // axis, so the grid keeps the source aspect ratio. A 16:9 source lands
    // on 60×33, not 30×17: the next step gives 30×16, one row short.
    private static (int mip, int cols, int rows) ComputeTargetMip(
        int width, int height, int targetCols, int targetRows)
    {
        int mip = 0;
        int w = width, h = height;
        while (w / 2 >= targetCols && h / 2 >= targetRows)
        {
            w /= 2;
            h /= 2;
            mip++;
        }
        return (mip, w, h);
    }


}
