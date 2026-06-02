using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI;
using Deckle.Composition;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// FrameSampler — GPU downsample + CPU readback for the ambient-lighting
// pixel analysis path (J3 step 2).
//
// Per-frame flow (runs on the capture service's worker thread,
// serialised via _lock) :
//   1. Read CapturedFrame.TexturePtr — already an ID3D11Texture2D
//      pointer, borrowed for the handler scope by the capture service.
//   2. CopyResource into our intermediate texture (allocated at source
//      resolution with mip levels enabled and MISC_GENERATE_MIPS).
//   3. GenerateMips on the intermediate SRV — the GPU walks the
//      pyramid in hardware, microseconds on modern adapters.
//   4. CopySubresourceRegion the target mip into a small staging
//      texture (CPU-readable, USAGE_STAGING + CPU_ACCESS_READ).
//      Readback bus traffic ≈ 1 KB at 30×17×4 bytes (BGRA8) or
//      ≈ 2 KB at 30×17×8 bytes (FP16). Sub-millisecond.
//   5. Map the staging, iterate the ~510 pixels, compute the
//      gamma-correct RGB average (via SrgbToLinear8Lut) + fill the
//      per-cell Color[] grid.
//   6. If the source format is FP16 (HDR), tone-map scRGB → 8-bit sRGB
//      via ColorSpace.ScRgbToSrgb against the display's reported
//      peak luminance.
//   7. Unmap and atomically publish the new SampledFrame via
//      Volatile.Write on _latestSample.
//
// Averaging is gamma-correct : per-pixel sRGB bytes go through
// ColorSpace.SrgbToLinear8Lut before summing, the mean is computed in
// linear light, then re-encoded via LinearToSrgb on the way out. The
// per-cell grid stays in sRGB bytes for the downstream consumers
// (Playground preview, AmbientEngine.SampleZone which re-linearises on
// its own).
//
// What we don't do (out of scope J3 step 2) :
//   - Black-border detection (J4).
//   - Zone weighting / multi-region extraction (J4).
//   - Spring-damper smoothing (J5).
//
// Ownership :
//   - The IDirect3DDevice is borrowed (passed in the constructor). We
//     extract the native ID3D11Device + immediate context (AddRef'd
//     pointers we Release in DisposeAsync) but never close the
//     WinRT wrapper.
//   - The intermediate texture, intermediate SRV, and staging texture
//     are owned ; allocated at construction, released on dispose.
//
// Threading :
//   - Process() may be called from any thread (typically the frame
//     pool's worker). Serialised by _lock — successive frames don't
//     interleave their Map / Unmap.
//   - LatestSample is read by the engine push loop and the Playground
//     preview timer ; both via volatile read.
public sealed partial class FrameSampler : IAsyncDisposable
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    // Mip level we sample. Chosen at construction so the resulting grid
    // is as close as possible to the target shape (30 × 17 for 16:9
    // sources, adjusted for other aspect ratios).
    private readonly int _targetMip;
    private readonly int _gridCols;
    private readonly int _gridRows;

    // DXGI format of the pool and our textures. Decides whether we read
    // BGRA8 bytes or FP16 floats, and whether to tone-map.
    private readonly uint _dxgiFormat;
    private readonly bool _isHdr;
    private readonly float _peakLuminance;

    // Hard ceiling on the rolling content peak, in scRGB units (=
    // display peak nits / 80). Caps the rolling max so a transient
    // sun-glint pixel cannot crush the rest of the scene below it.
    // Floored at 1.0 even in non-HDR sessions where the field is
    // unused.
    private readonly float _displayPeakScRgb;

    // Rolling max of the recent frames' max-channel scRGB values,
    // consumed by ColorSpace.ScRgbToSrgb as the normalisation peak.
    // Updated at the end of each ReadGridFP16 ; consumed at the start
    // of the next frame's tone-map (one-frame lag is harmless at
    // 15 Hz). Asymmetric attack / release :
    //   - attack instant (rises with the first bright frame)
    //   - release exponential — ContentPeakReleaseDecay per frame
    private float _contentPeak = 1.0f;
    private const float ContentPeakReleaseDecay = 0.97f;

    // Live-reloaded by the engine before each tick when the user
    // moves the AmbientPage Exposure slider. Applied as a linear-
    // light EV bias inside ColorSpace.ScRgbToSrgb.
    private double _exposureEv = 0.0;

    // Native COM pointers. AddRef'd ; Released in DisposeAsync.
    private nint _d3dDevice;
    private nint _d3dContext;
    private nint _intermediateTex;
    private nint _intermediateSrv;
    private nint _stagingTex;

    private SampledFrame? _latestSample;
    private readonly object _lock = new();
    private bool _disposed;

    // Acquire timestamps des textures persistantes du sampler — lues
    // par DisposeAsync pour calculer age_ms côté DeckleResourceSource.
    // Chaque texture est créée une fois au ctor et release une fois
    // au dispose.
    private long _intermediateTexAcquiredTicks;
    private long _stagingTexAcquiredTicks;

    // Most recent snapshot — volatile read so consumers see the latest
    // value published by Process.
    public SampledFrame? LatestSample => Volatile.Read(ref _latestSample);

    public int GridCols => _gridCols;
    public int GridRows => _gridRows;
    public bool IsHdr   => _isHdr;

    /// <summary>Rolling content-peak in scRGB units that the tone-map
    /// is currently normalising against. Exposed for tuning surfaces
    /// such as the Playground preview (shows whether the
    /// auto-exposure is biting). Always ≥ 1.0 (SDR floor). On a non-
    /// HDR session the value is pinned at 1.0.</summary>
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

        // Display peak in scRGB units (80 nits = 1.0 by scRGB
        // convention). Floored at 1.0 so non-HDR sessions and
        // pathological 0-nit reports still produce a sensible
        // ceiling.
        _displayPeakScRgb = _isHdr ? MathF.Max(_peakLuminance / 80f, 1f) : 1f;

        (_targetMip, _gridCols, _gridRows) =
            ComputeTargetMip(_sourceWidth, _sourceHeight, targetCols: 30, targetRows: 17);

        // Extract the native ID3D11Device (AddRef'd) and its immediate
        // context. Both are released in DisposeAsync.
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

        int bytesPerPixel = _isHdr ? 8 : 4;

        _intermediateTex = CreateIntermediateTexture();
        _intermediateTexAcquiredTicks = Stopwatch.GetTimestamp();
        // Sub-provider transverse Resource — acquire des trois textures
        // persistantes du sampler. owner="frame-sampler" pour les
        // différencier des textures per-frame de "capture-loop".
        // size_bytes approxime l'allocation mémoire (full mip chain
        // pour l'intermédiaire ≈ 4/3 du mip 0, on simplifie à mip 0
        // pour rester lisible).
        DeckleResourceSource.Log.ResourceAcquired(
            "d3d11-texture", (long)_intermediateTex,
            _sourceWidth * _sourceHeight * bytesPerPixel, "frame-sampler");

        _intermediateSrv = CreateIntermediateSrv();
        // SRV : pas de mémoire propre, c'est une vue. On le trace en
        // "dxgi-resource" générique avec size_bytes=0 pour distinguer
        // le handle visiblement dans la trace.
        DeckleResourceSource.Log.ResourceAcquired(
            "dxgi-resource", (long)_intermediateSrv, 0, "frame-sampler");

        _stagingTex = CreateStagingTexture();
        _stagingTexAcquiredTicks = Stopwatch.GetTimestamp();
        DeckleResourceSource.Log.ResourceAcquired(
            "d3d11-texture", (long)_stagingTex,
            _gridCols * _gridRows * bytesPerPixel, "frame-sampler");

        DeckleVisionSource.Log.SamplerInitialized(
            _gridCols, _gridRows, _targetMip,
            _isHdr ? "scrgb_to_srgb" : "none", _peakLuminance);
    }

    // Pick the mip level whose dimensions land closest to (targetCols,
    // targetRows) without going below. Halves the source size at each
    // step ; stops when the next halving would undershoot the target.
    // 3840×2160 with target 30×17 → mip 7 (30×17). 1920×1080 → mip 6
    // (30×17). 2560×1440 → mip 6 (40×22). Aspect ratio of the source
    // is preserved in the resulting grid.
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
