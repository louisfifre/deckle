using System.Runtime.InteropServices;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── HDR detection (IDXGIOutput6::GetDesc1) ───────────────────────────────
    //
    // An output is in HDR mode when it reports HDR10 (G2084, PQ) or scRGB
    // (G10, linear). A display that reports no peak luminance, or any
    // failure, falls back to 80 nits, the sRGB reference white.

    private const int DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709     = 0;  // sRGB
    private const int DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709     = 1;  // scRGB
    private const int DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020  = 12; // HDR10

    private const uint DXGI_CREATE_FACTORY_DEBUG = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC1
    {
        // WCHAR DeviceName[32]: 64 bytes, held as eight ulongs to keep the
        // layout; the name is never read.
        public ulong DeviceName0;
        public ulong DeviceName1;
        public ulong DeviceName2;
        public ulong DeviceName3;
        public ulong DeviceName4;
        public ulong DeviceName5;
        public ulong DeviceName6;
        public ulong DeviceName7;

        public int  DesktopLeft;
        public int  DesktopTop;
        public int  DesktopRight;
        public int  DesktopBottom;
        public int  AttachedToDesktop;       // BOOL
        public int  Rotation;                // DXGI_MODE_ROTATION enum
        public nint Monitor;                 // HMONITOR
        public uint BitsPerColor;
        public int  ColorSpace;              // DXGI_COLOR_SPACE_TYPE enum

        public float RedPrimary0;
        public float RedPrimary1;
        public float GreenPrimary0;
        public float GreenPrimary1;
        public float BluePrimary0;
        public float BluePrimary1;
        public float WhitePoint0;
        public float WhitePoint1;

        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    [DllImport("dxgi.dll", PreserveSig = false, ExactSpelling = true)]
    private static extern void CreateDXGIFactory2(uint flags, [In] in Guid iid, out nint factory);

    public readonly record struct HdrState(bool IsHdr, float PeakLuminance, int ColorSpace);

    public static HdrState DetectHdrState(nint hmon)
    {
        const float SdrReferenceNits = 80f;
        var fallback = new HdrState(false, SdrReferenceNits, DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);

        nint factoryPtr = 0;
        try
        {
            try
            {
                CreateDXGIFactory2(0, in IID_IDXGIFactory6, out factoryPtr);
            }
            catch
            {
                return fallback;
            }

            // IDXGIFactory6::EnumAdapters lives at vtable slot 7 (after
            // IUnknown's 3 + IDXGIObject's 4 = 7). IDXGIAdapter::EnumOutputs
            // lives at vtable slot 7 as well.
            unsafe
            {
                var factoryVtbl = *(nint**)factoryPtr;
                var enumAdapters = (delegate* unmanaged<nint, uint, nint*, int>)factoryVtbl[7];

                for (uint adapterIdx = 0; adapterIdx < 32; adapterIdx++)
                {
                    nint adapterPtr;
                    int hr = enumAdapters(factoryPtr, adapterIdx, &adapterPtr);
                    if (hr != 0 || adapterPtr == 0) break;

                    try
                    {
                        var adapterVtbl = *(nint**)adapterPtr;
                        var enumOutputs = (delegate* unmanaged<nint, uint, nint*, int>)adapterVtbl[7];

                        for (uint outputIdx = 0; outputIdx < 16; outputIdx++)
                        {
                            nint outputPtr;
                            hr = enumOutputs(adapterPtr, outputIdx, &outputPtr);
                            if (hr != 0 || outputPtr == 0) break;

                            try
                            {
                                Guid iidOutput6 = IID_IDXGIOutput6;
                                hr = Marshal.QueryInterface(outputPtr, in iidOutput6, out nint output6Ptr);
                                if (hr != 0) continue;

                                try
                                {
                                    // IDXGIOutput6::GetDesc1 is at vtable
                                    // slot 27 (3 IUnknown + 4 IDXGIObject
                                    // + 12 IDXGIOutput + 4 IDXGIOutput1
                                    // + 1 IDXGIOutput2 + 1 IDXGIOutput3
                                    // + 1 IDXGIOutput4 + 1 IDXGIOutput5
                                    // = 27, GetDesc1 is the first method
                                    // declared in IDXGIOutput6).
                                    var output6Vtbl = *(nint**)output6Ptr;
                                    var getDesc1 = (delegate* unmanaged<nint, DXGI_OUTPUT_DESC1*, int>)output6Vtbl[27];
                                    DXGI_OUTPUT_DESC1 desc;
                                    hr = getDesc1(output6Ptr, &desc);
                                    if (hr != 0) continue;

                                    if (desc.Monitor == hmon)
                                    {
                                        bool isHdr =
                                            desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                                            desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709;
                                        float peak = desc.MaxLuminance > 0 ? desc.MaxLuminance : SdrReferenceNits;
                                        return new HdrState(isHdr, peak, desc.ColorSpace);
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(output6Ptr);
                                }
                            }
                            finally
                            {
                                Marshal.Release(outputPtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapterPtr);
                    }
                }
            }

            return fallback;
        }
        finally
        {
            if (factoryPtr != 0) Marshal.Release(factoryPtr);
        }
    }

}
