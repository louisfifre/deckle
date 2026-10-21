using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── D3D11 device → IDirect3DDevice (WinRT) ───────────────────────────────

    // D3D_DRIVER_TYPE: D3D11CreateDevice requires UNKNOWN when it receives
    // an explicit adapter (the adapter implies its driver type). HARDWARE,
    // used with the default adapter, keeps WARP out.
    private const int D3D_DRIVER_TYPE_UNKNOWN  = 0;
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x00000020;
    private const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll", PreserveSig = false, ExactSpelling = true)]
    private static extern void D3D11CreateDevice(
        nint pAdapter,
        int driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out int pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport("d3d11.dll", PreserveSig = false, ExactSpelling = true)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public static IDirect3DDevice CreateDirect3DDevice(nint pAdapter = 0)
        => CreateDirect3DDevice(pAdapter, out _);

    // featureLevel is the raw D3D_FEATURE_LEVEL; FormatFeatureLevel turns
    // it into the "11_1" spelling of the documentation.
    public static IDirect3DDevice CreateDirect3DDevice(nint pAdapter, out int featureLevel)
    {
        // BGRA_SUPPORT is required to share the frames with the DirectX
        // composition stack (Win2D, CanvasBitmap).
        int driverType = pAdapter == 0 ? D3D_DRIVER_TYPE_HARDWARE : D3D_DRIVER_TYPE_UNKNOWN;
        D3D11CreateDevice(
            pAdapter:        pAdapter,
            driverType:      driverType,
            software:        0,
            flags:           D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            pFeatureLevels:  0,
            featureLevels:   0,
            sdkVersion:      D3D11_SDK_VERSION,
            ppDevice:        out nint d3dDevicePtr,
            pFeatureLevel:   out featureLevel,
            ppImmediateContext: out nint d3dContextPtr);

        // Not needed here: FrameSampler fetches its own immediate context
        // from the device.
        if (d3dContextPtr != 0) Marshal.Release(d3dContextPtr);

        try
        {
            int hr = Marshal.QueryInterface(d3dDevicePtr, in IID_IDXGIDevice, out nint dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // The export returns an IInspectable pointer;
                // MarshalInspectable projects it to the managed
                // IDirect3DDevice.
                CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out nint winrtDevicePtr);
                try
                {
                    return MarshalInspectable<IDirect3DDevice>.FromAbi(winrtDevicePtr);
                }
                finally
                {
                    if (winrtDevicePtr != 0) Marshal.Release(winrtDevicePtr);
                }
            }
            finally
            {
                if (dxgiDevicePtr != 0) Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            if (d3dDevicePtr != 0) Marshal.Release(d3dDevicePtr);
        }
    }

    // D3D_FEATURE_LEVEL packs major and minor in the high nibbles:
    // 0xb100 is 11_1.
    public static string FormatFeatureLevel(int featureLevel)
        => featureLevel == 0
            ? "unknown"
            : $"{(featureLevel >> 12) & 0xF}_{(featureLevel >> 8) & 0xF}";

    // ── Native interfaces behind the WinRT wrappers ──────────────────────────
    //
    // D3D11 and DXGI calls go through vtable slots and function pointers,
    // never [ComImport] interfaces. Mixing the CsWinRT projection that owns
    // IDirect3DDevice with a classic COM RCW throws InvalidCastException
    // ("element not found"): the runtime cannot cast an object CsWinRT has
    // already wrapped. MarshalInspectable<T>.FromManaged hands back the raw
    // ABI pointer instead, which is QI'd to IDirect3DDxgiInterfaceAccess;
    // its GetInterface returns the native ID3D11Device or ID3D11Texture2D.
    //
    // Every helper that returns an unmanaged COM pointer leaves the caller
    // to Release it.

    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private static readonly Guid IID_ID3D11Device           = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    private static readonly Guid IID_ID3D11Texture2D        = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IDXGIAdapter           = new("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
    private static readonly Guid IID_IDXGIFactory6          = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid IID_IDXGIOutput5           = new("80a07424-ab52-42eb-833c-0c42fd282d98");
    private static readonly Guid IID_IDXGIOutput6           = new("068346e8-aaec-4b84-add7-137f513f77a1");
    private static readonly Guid IID_IDXGIOutputDuplication = new("191cfac3-a341-470d-b26e-a864f428319c");

    // Consumes the AddRef'd ABI pointer (released here) and returns an
    // AddRef'd native pointer the caller owns.
    private static nint GetNativeInterfaceFromAbi(nint abiPtr, Guid targetIid)
    {
        try
        {
            int hr = Marshal.QueryInterface(abiPtr, in IID_IDirect3DDxgiInterfaceAccess, out nint accessPtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                unsafe
                {
                    // vtable layout : IUnknown (3 slots) + GetInterface (slot 3).
                    var vtbl = *(nint**)accessPtr;
                    var getInterface = (delegate* unmanaged<nint, Guid*, nint*, int>)vtbl[3];
                    nint targetPtr;
                    int gotHr = getInterface(accessPtr, &targetIid, &targetPtr);
                    Marshal.ThrowExceptionForHR(gotHr);
                    return targetPtr;
                }
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(abiPtr);
        }
    }

    // Returns an AddRef'd pointer; the caller releases it.
    public static nint GetD3D11Device(IDirect3DDevice device)
        => GetNativeInterfaceFromAbi(
            MarshalInspectable<IDirect3DDevice>.FromManaged(device),
            IID_ID3D11Device);

    // Returns an AddRef'd pointer; the caller releases it.
    public static nint GetD3D11Texture(IDirect3DSurface surface)
        => GetNativeInterfaceFromAbi(
            MarshalInspectable<IDirect3DSurface>.FromManaged(surface),
            IID_ID3D11Texture2D);

}
