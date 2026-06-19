using System.Runtime.InteropServices;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── DXGI Output Duplication ──────────────────────────────────────────────
    //
    // Desktop Duplication is the capture API that predates
    // Windows.Graphics.Capture. It runs on every desktop session without
    // a system-drawn capture indicator (the yellow border WGC paints
    // around the captured monitor), and supports HDR via DXGI 1.5's
    // DuplicateOutput1 + DXGI_FORMAT_R16G16B16A16_FLOAT in the format
    // list. Documented in Microsoft Learn "Desktop Duplication API" and
    // standard practice in OBS / NVIDIA ShadowPlay / HyperHDR.
    //
    // The pump is poll-based : IDXGIOutputDuplication::AcquireNextFrame
    // blocks up to a caller-specified timeout for a new desktop frame,
    // returning an IDXGIResource the caller QI's to ID3D11Texture2D.
    // ReleaseFrame returns the buffer to the OS. A worker thread loops
    // these two calls.
    //
    // Architecture note. The D3D11 device passed to DuplicateOutput1
    // MUST be created on the same DXGI adapter as the output being
    // duplicated, otherwise E_INVALIDARG. On multi-GPU laptops (Intel
    // iGPU + NVIDIA dGPU) the default adapter is rarely the one driving
    // the target monitor — we walk adapters/outputs to find the match,
    // then create the device on that specific adapter.

    public const int DXGI_ERROR_ACCESS_LOST           = unchecked((int)0x887A0026);
    public const int DXGI_ERROR_WAIT_TIMEOUT          = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_SESSION_DISCONNECTED  = unchecked((int)0x887A0028);
    public const int DXGI_ERROR_ACCESS_DENIED         = unchecked((int)0x887A002B);
    public const int DXGI_ERROR_INVALID_CALL          = unchecked((int)0x887A0001);
    public const int DXGI_ERROR_DEVICE_REMOVED        = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_HUNG           = unchecked((int)0x887A0006);

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MODE_DESC
    {
        public uint          Width;
        public uint          Height;
        public DXGI_RATIONAL RefreshRate;
        public uint          Format;            // DXGI_FORMAT
        public uint          ScanlineOrdering;  // DXGI_MODE_SCANLINE_ORDER
        public uint          Scaling;           // DXGI_MODE_SCALING
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_DESC
    {
        public DXGI_MODE_DESC ModeDesc;
        public uint           Rotation;                    // DXGI_MODE_ROTATION
        public int            DesktopImageInSystemMemory;  // BOOL
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public int X;
        public int Y;
        public int Visible; // BOOL
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long                          LastPresentTime;
        public long                          LastMouseUpdateTime;
        public uint                          AccumulatedFrames;
        public int                           RectsCoalesced;            // BOOL
        public int                           ProtectedContentMaskedOut; // BOOL
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint                          TotalMetadataBufferSize;
        public uint                          PointerShapeBufferSize;
    }

    /// <summary>The IDXGIAdapter + IDXGIOutput5 pair that drives a given
    /// HMONITOR, plus the HDR state of that output. Both pointers are
    /// AddRef'd ; caller releases via <see cref="Marshal.Release"/> when
    /// done (typically alongside the IDXGIOutputDuplication's own
    /// lifetime).</summary>
    public readonly record struct DxgiOutputMatch(
        nint     AdapterPtr,
        nint     Output5Ptr,
        HdrState Hdr);

    /// <summary>
    /// Walks every DXGI adapter / output combination and returns the
    /// pair whose IDXGIOutput6::GetDesc1 reports the requested HMONITOR.
    /// Throws <see cref="InvalidOperationException"/> if no match is
    /// found (display disconnected mid-startup, headless adapter, etc.).
    /// On success the caller owns one reference to each returned
    /// pointer and must Release both.
    /// </summary>
    public static DxgiOutputMatch FindDxgiOutputForMonitor(nint hmon)
    {
        nint factoryPtr = 0;
        try
        {
            CreateDXGIFactory2(0, in IID_IDXGIFactory6, out factoryPtr);

            unsafe
            {
                var factoryVtbl = *(nint**)factoryPtr;
                var enumAdapters = (delegate* unmanaged<nint, uint, nint*, int>)factoryVtbl[7];

                for (uint adapterIdx = 0; adapterIdx < 32; adapterIdx++)
                {
                    nint adapterPtr;
                    int hr = enumAdapters(factoryPtr, adapterIdx, &adapterPtr);
                    if (hr != 0 || adapterPtr == 0) break;

                    bool keepAdapter = false;
                    try
                    {
                        var adapterVtbl = *(nint**)adapterPtr;
                        var enumOutputs = (delegate* unmanaged<nint, uint, nint*, int>)adapterVtbl[7];

                        for (uint outputIdx = 0; outputIdx < 16; outputIdx++)
                        {
                            nint outputPtr;
                            hr = enumOutputs(adapterPtr, outputIdx, &outputPtr);
                            if (hr != 0 || outputPtr == 0) break;

                            bool keepOutput = false;
                            try
                            {
                                // QI to IDXGIOutput6 for GetDesc1 (HDR state +
                                // HMONITOR). Same vtable shape walk as
                                // DetectHdrState.
                                Guid iidOutput6 = IID_IDXGIOutput6;
                                hr = Marshal.QueryInterface(outputPtr, in iidOutput6, out nint output6Ptr);
                                if (hr != 0) continue;

                                try
                                {
                                    var output6Vtbl = *(nint**)output6Ptr;
                                    var getDesc1 = (delegate* unmanaged<nint, DXGI_OUTPUT_DESC1*, int>)output6Vtbl[27];
                                    DXGI_OUTPUT_DESC1 desc;
                                    hr = getDesc1(output6Ptr, &desc);
                                    if (hr != 0) continue;

                                    if (desc.Monitor != hmon) continue;

                                    bool isHdr =
                                        desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                                        desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709;
                                    float peak = desc.MaxLuminance > 0 ? desc.MaxLuminance : 80f;
                                    var hdrState = new HdrState(isHdr, peak, desc.ColorSpace);

                                    // QI down to IDXGIOutput5 — the
                                    // interface that exposes
                                    // DuplicateOutput1 (HDR-capable
                                    // duplication).
                                    Guid iidOutput5 = IID_IDXGIOutput5;
                                    hr = Marshal.QueryInterface(outputPtr, in iidOutput5, out nint output5Ptr);
                                    Marshal.ThrowExceptionForHR(hr);

                                    keepAdapter = true;
                                    keepOutput = true;
                                    return new DxgiOutputMatch(adapterPtr, output5Ptr, hdrState);
                                }
                                finally
                                {
                                    Marshal.Release(output6Ptr);
                                }
                            }
                            finally
                            {
                                if (!keepOutput) Marshal.Release(outputPtr);
                            }
                        }
                    }
                    finally
                    {
                        if (!keepAdapter) Marshal.Release(adapterPtr);
                    }
                }
            }
        }
        finally
        {
            if (factoryPtr != 0) Marshal.Release(factoryPtr);
        }

        throw new InvalidOperationException(
            $"No DXGI output found for HMONITOR 0x{hmon:X}. The monitor may have been disconnected, or its adapter doesn't expose a duplication-capable output.");
    }

    /// <summary>
    /// Calls IDXGIOutput5::DuplicateOutput1 on the supplied output with
    /// the given list of acceptable surface formats (priority order —
    /// DXGI picks the first the OS can fulfil). The returned
    /// IDXGIOutputDuplication pointer is AddRef'd ; caller releases
    /// when done. supportedFormats must include at least one valid
    /// DXGI_FORMAT (typically R16G16B16A16_FLOAT for HDR fallback to
    /// B8G8R8A8_UNORM for SDR).
    /// </summary>
    public static nint DuplicateOutput1(nint output5Ptr, nint d3dDevicePtr, uint[] supportedFormats)
    {
        if (supportedFormats is null || supportedFormats.Length == 0)
            throw new ArgumentException("supportedFormats must contain at least one DXGI_FORMAT", nameof(supportedFormats));

        unsafe
        {
            var output5Vtbl = *(nint**)output5Ptr;
            var duplicate = (delegate* unmanaged<nint, nint, uint, uint, uint*, nint*, int>)output5Vtbl[26];
            nint duplicationPtr;
            fixed (uint* fmtPtr = supportedFormats)
            {
                int hr = duplicate(
                    output5Ptr,
                    d3dDevicePtr,
                    /* Flags */ 0,
                    (uint)supportedFormats.Length,
                    fmtPtr,
                    &duplicationPtr);
                Marshal.ThrowExceptionForHR(hr);
            }
            return duplicationPtr;
        }
    }

    /// <summary>
    /// Calls IDXGIOutputDuplication::GetDesc to retrieve the negotiated
    /// surface format and dimensions. Useful right after DuplicateOutput1
    /// to learn which format DXGI picked from the supplied priority list.
    /// </summary>
    public static DXGI_OUTDUPL_DESC GetDuplicationDesc(nint duplicationPtr)
    {
        unsafe
        {
            var vtbl = *(nint**)duplicationPtr;
            var getDesc = (delegate* unmanaged<nint, DXGI_OUTDUPL_DESC*, void>)vtbl[7];
            DXGI_OUTDUPL_DESC desc;
            getDesc(duplicationPtr, &desc);
            return desc;
        }
    }

    /// <summary>
    /// Calls IDXGIOutputDuplication::AcquireNextFrame. Blocks up to
    /// <paramref name="timeoutMs"/> for a desktop image update. On
    /// success returns S_OK and populates the out parameters ;
    /// pDesktopResource is AddRef'd and the caller must call
    /// <see cref="ReleaseFrame"/> after processing (which also Releases
    /// the resource implicitly via OS bookkeeping — but we Release the
    /// COM ref ourselves for symmetry with the QI to ID3D11Texture2D).
    /// Other notable HRESULTs to handle explicitly :
    /// <see cref="DXGI_ERROR_WAIT_TIMEOUT"/> (no new frame in the
    /// interval — common, not an error), <see cref="DXGI_ERROR_ACCESS_LOST"/>
    /// (desktop switch / mode change / fullscreen swap — caller must
    /// recreate the IDXGIOutputDuplication).
    /// </summary>
    public static int AcquireNextFrame(
        nint duplicationPtr,
        uint timeoutMs,
        out DXGI_OUTDUPL_FRAME_INFO frameInfo,
        out nint desktopResourcePtr)
    {
        unsafe
        {
            var vtbl = *(nint**)duplicationPtr;
            var acquire = (delegate* unmanaged<nint, uint, DXGI_OUTDUPL_FRAME_INFO*, nint*, int>)vtbl[8];
            DXGI_OUTDUPL_FRAME_INFO info = default;
            nint resourcePtr = 0;
            int hr = acquire(duplicationPtr, timeoutMs, &info, &resourcePtr);
            frameInfo = info;
            desktopResourcePtr = resourcePtr;
            return hr;
        }
    }

    /// <summary>
    /// Calls IDXGIOutputDuplication::ReleaseFrame. Returns the previously
    /// acquired frame's GPU buffer to the OS. Must be called once per
    /// successful AcquireNextFrame ; calling it a second time returns
    /// DXGI_ERROR_INVALID_CALL.
    /// </summary>
    public static int ReleaseFrame(nint duplicationPtr)
    {
        unsafe
        {
            var vtbl = *(nint**)duplicationPtr;
            var release = (delegate* unmanaged<nint, int>)vtbl[14];
            return release(duplicationPtr);
        }
    }

    /// <summary>
    /// QI helper : given an IDXGIResource pointer (typically from
    /// AcquireNextFrame's out param), returns the underlying
    /// ID3D11Texture2D. The returned pointer is AddRef'd ; caller
    /// releases.
    /// </summary>
    public static nint QueryD3D11Texture(nint dxgiResourcePtr)
    {
        Guid iid = IID_ID3D11Texture2D;
        int hr = Marshal.QueryInterface(dxgiResourcePtr, in iid, out nint texturePtr);
        Marshal.ThrowExceptionForHR(hr);
        return texturePtr;
    }

}
