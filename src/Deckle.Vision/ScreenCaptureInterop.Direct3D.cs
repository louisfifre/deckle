using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── D3D11 device → IDirect3DDevice (WinRT) ───────────────────────────────

    // D3D_DRIVER_TYPE — UNKNOWN is mandatory when D3D11CreateDevice
    // receives an explicit adapter pointer (the adapter implies its
    // own driver type) ; HARDWARE is used when we let DXGI pick the
    // default adapter.
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

    // IDXGIDevice IID, queried from the freshly-created ID3D11Device.
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public static IDirect3DDevice CreateDirect3DDevice(nint pAdapter = 0)
    {
        // 1. Create the D3D11 device. BGRA_SUPPORT is required for any
        //    consumer that wants to share the frames with the DirectX
        //    composition stack (Win2D, CanvasBitmap, etc.). When the
        //    caller passes a specific adapter (pAdapter != 0), the
        //    driver type must be UNKNOWN per the D3D11CreateDevice
        //    contract — the adapter implies its type. With pAdapter=0
        //    (default) we ask for HARDWARE explicitly to skip WARP.
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
            pFeatureLevel:   out _,
            ppImmediateContext: out nint d3dContextPtr);

        // We never use the immediate context here — capture goes through
        // the frame pool, not direct draw calls. Release immediately.
        if (d3dContextPtr != 0) Marshal.Release(d3dContextPtr);

        try
        {
            // 2. QI to IDXGIDevice. Required because CreateDirect3D11Device-
            //    FromDXGIDevice operates on the DXGI face of the device,
            //    not on ID3D11Device directly.
            int hr = Marshal.QueryInterface(d3dDevicePtr, in IID_IDXGIDevice, out nint dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // 3. Wrap as the WinRT IDirect3DDevice. The d3d11.dll export
                //    hands back an IInspectable pointer ; we project it via
                //    CsWinRT's MarshalInspectable to get the managed
                //    IDirect3DDevice the FramePool wants.
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

    // ── D3D11 staging support (J3 step 2 — FrameSampler) ─────────────────────
    //
    // FrameSampler runs the GPU downsample path : take the captured texture,
    // GenerateMips on an intermediate that has mip levels enabled, then
    // CopySubresourceRegion the target mip into a CPU-readable staging
    // texture. The CPU only ever reads the small mip (~500 pixels), never
    // the 4K source — that's where the perf comes from.
    //
    // We don't declare full ComImport interfaces for ID3D11Device /
    // ID3D11DeviceContext (60+ methods each), only stub vtable indices for
    // the calls we need and call them via function pointers. This is the
    // modern .NET 7+ pattern : less code than ComImport stubs, no
    // dependency on a third-party D3D11 wrapper. The vtable indices are
    // stable across Windows versions (D3D11 interfaces never change once
    // shipped, by COM convention).
    //
    // Every helper that returns an unmanaged COM pointer requires the
    // caller to Release it. Helpers that return managed wrappers
    // (IDirect3DDevice etc.) follow the existing release convention.

    // IDirect3DDxgiInterfaceAccess — the bridge between WinRT IDirect3D*
    // wrappers and native DXGI/D3D11 interfaces. Every IDirect3DDevice
    // and IDirect3DSurface implements it ; GetInterface returns the
    // underlying ID3D11Device / ID3D11Texture2D for a requested IID.
    //
    // We never declare a [ComImport] managed interface here. The mix of
    // CsWinRT projection (which owns the WinRT IDirect3DDevice) and
    // classic COM RCW threw "InvalidCastException: element not found"
    // at every pipeline start — the runtime couldn't reconcile a managed
    // cast on an object that CsWinRT had already wrapped its own way.
    //
    // The fix takes the canonical CsWinRT path : MarshalInspectable<T>.
    // FromManaged returns the raw AddRef'd ABI pointer of the WinRT
    // object (the same pointer the CsWinRT runtime uses internally).
    // We QI that to IDirect3DDxgiInterfaceAccess, then call GetInterface
    // via the vtable directly (slot 3, after IUnknown's 3 methods).
    // Zero managed cast in the path = no InvalidCastException.

    // IDirect3DDxgiInterfaceAccess IID. Held as a static field rather
    // than reconstructed each call so the GUID parsing cost is paid once.
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    // Native D3D11 + DXGI interface IIDs. Used as QI targets through
    // IDirect3DDxgiInterfaceAccess.GetInterface (D3D11) or
    // IDXGIAdapter / IDXGIOutput chains (DXGI).
    private static readonly Guid IID_ID3D11Device           = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    private static readonly Guid IID_ID3D11Texture2D        = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IDXGIAdapter           = new("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
    private static readonly Guid IID_IDXGIFactory6          = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid IID_IDXGIOutput5           = new("80a07424-ab52-42eb-833c-0c42fd282d98");
    private static readonly Guid IID_IDXGIOutput6           = new("068346e8-aaec-4b84-add7-137f513f77a1");
    private static readonly Guid IID_IDXGIOutputDuplication = new("191cfac3-a341-470d-b26e-a864f428319c");

    // Internal helper. Given a freshly AddRef'd ABI pointer (from
    // MarshalInspectable.FromManaged) and a target native COM IID, returns
    // an AddRef'd native interface pointer by QI'ing to IDirect3DDxgi-
    // InterfaceAccess and calling GetInterface via vtable[3]. The input
    // ABI pointer is Released in finally — caller doesn't own it
    // afterwards. Caller does own the returned pointer.
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

    // Extracts the native ID3D11Device behind a WinRT IDirect3DDevice. The
    // returned pointer is AddRef'd ; caller must Marshal.Release it.
    public static nint GetD3D11Device(IDirect3DDevice device)
        => GetNativeInterfaceFromAbi(
            MarshalInspectable<IDirect3DDevice>.FromManaged(device),
            IID_ID3D11Device);

    // Extracts the native ID3D11Texture2D behind a Direct3D11CaptureFrame's
    // Surface. The returned pointer is AddRef'd ; caller must Marshal.Release
    // it (typically inside the FrameArrived handler, paired with the frame's
    // own Dispose).
    public static nint GetD3D11Texture(IDirect3DSurface surface)
        => GetNativeInterfaceFromAbi(
            MarshalInspectable<IDirect3DSurface>.FromManaged(surface),
            IID_ID3D11Texture2D);

}
