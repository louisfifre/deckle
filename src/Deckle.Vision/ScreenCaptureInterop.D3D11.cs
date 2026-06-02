using System.Runtime.InteropServices;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── D3D11 vtable indices (counted from IUnknown::QueryInterface = 0) ─────
    //
    // Pulled from d3d11.h. Stable per COM contract (interfaces never change
    // shape once published). Exposed as constants so call sites read clearly.

    internal static class D3D11Vtbl
    {
        // ID3D11Device methods (after IUnknown's 3).
        public const int Device_CreateTexture2D            = 5;
        public const int Device_CreateShaderResourceView   = 7;
        public const int Device_GetImmediateContext        = 40;

        // ID3D11DeviceContext methods (after IUnknown's 3 + ID3D11DeviceChild's 4).
        public const int Context_Map                       = 14;
        public const int Context_Unmap                     = 15;
        public const int Context_CopySubresourceRegion     = 46;
        public const int Context_CopyResource              = 47;
        // GenerateMips is at slot 54 — after the Map/Unmap/Copy*/Update*/
        // Clear* family. Trust the d3d11.h declaration order ; the slot
        // is stable per COM contract.
        public const int Context_GenerateMips              = 54;
    }

    // ── D3D11 structs + constants ────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;          // DXGI_FORMAT
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;           // D3D11_USAGE
        public uint BindFlags;       // D3D11_BIND_FLAG
        public uint CPUAccessFlags;  // D3D11_CPU_ACCESS_FLAG
        public uint MiscFlags;       // D3D11_RESOURCE_MISC_FLAG
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BOX
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    // D3D11_USAGE
    public const uint D3D11_USAGE_DEFAULT         = 0;
    public const uint D3D11_USAGE_STAGING         = 3;

    // D3D11_BIND_FLAG
    public const uint D3D11_BIND_SHADER_RESOURCE  = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET    = 0x20;

    // D3D11_CPU_ACCESS_FLAG
    public const uint D3D11_CPU_ACCESS_WRITE      = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ       = 0x20000;

    // D3D11_RESOURCE_MISC_FLAG
    public const uint D3D11_RESOURCE_MISC_GENERATE_MIPS = 0x1;

    // D3D11_MAP
    public const uint D3D11_MAP_READ              = 1;

    // DXGI_FORMAT
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM       = 87;
    public const uint DXGI_FORMAT_R16G16B16A16_FLOAT   = 10;
}
