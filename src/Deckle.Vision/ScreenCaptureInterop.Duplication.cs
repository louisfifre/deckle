using System.Runtime.InteropServices;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── DXGI Output Duplication ──────────────────────────────────────────────
    //
    // DuplicateOutput1 (IDXGIOutput5, DXGI 1.5) is the HDR-capable entry
    // point. The D3D11 device passed to it must be created on the adapter
    // that drives the output, or the call fails with E_INVALIDARG; on a
    // multi-GPU laptop the default adapter is often not that one.

    public const int DXGI_ERROR_ACCESS_LOST           = unchecked((int)0x887A0026);
    public const int DXGI_ERROR_WAIT_TIMEOUT          = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_SESSION_DISCONNECTED  = unchecked((int)0x887A0028);
    public const int DXGI_ERROR_ACCESS_DENIED         = unchecked((int)0x887A002B);
    public const int DXGI_ERROR_NOT_CURRENTLY_AVAILABLE = unchecked((int)0x887A0022);
    public const int DXGI_ERROR_INVALID_CALL          = unchecked((int)0x887A0001);
    public const int DXGI_ERROR_UNSUPPORTED           = unchecked((int)0x887A0004);
    public const int DXGI_ERROR_DEVICE_REMOVED        = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_HUNG           = unchecked((int)0x887A0006);
    public const int E_ACCESSDENIED                   = unchecked((int)0x80070005);

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

    /// <summary>Both pointers are AddRef'd; the caller releases
    /// them.</summary>
    public readonly record struct DxgiOutputMatch(
        nint     AdapterPtr,
        nint     Output5Ptr,
        HdrState Hdr);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when no output
    /// reports the HMONITOR (display disconnected mid-startup, headless
    /// adapter).
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
                                // Vtable slots as explained in DetectHdrState.
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
    /// DXGI delivers the desktop in one of <paramref name="supportedFormats"/>,
    /// converting when the current format is not listed; read the result
    /// back with <see cref="GetDuplicationDesc"/>. The returned pointer is
    /// AddRef'd; the caller releases it.
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
    /// Returns the HRESULT instead of throwing:
    /// <see cref="DXGI_ERROR_WAIT_TIMEOUT"/> (no update in the interval) is
    /// routine and <see cref="DXGI_ERROR_ACCESS_LOST"/> means the
    /// duplication must be recreated. On success the resource is AddRef'd:
    /// release it, then call <see cref="ReleaseFrame"/>.
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
    /// Call once per successful AcquireNextFrame; a second call returns
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
    /// Returns an AddRef'd ID3D11Texture2D; the caller releases it.
    /// </summary>
    public static nint QueryD3D11Texture(nint dxgiResourcePtr)
    {
        Guid iid = IID_ID3D11Texture2D;
        int hr = Marshal.QueryInterface(dxgiResourcePtr, in iid, out nint texturePtr);
        Marshal.ThrowExceptionForHR(hr);
        return texturePtr;
    }

}
