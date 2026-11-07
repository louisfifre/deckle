using System.Runtime.InteropServices;

namespace Deckle.Vision;

internal static partial class ScreenCaptureInterop
{
    // ── D3D11 vtable indices (counted from IUnknown::QueryInterface = 0) ─────
    //
    // Counted from the d3d11.h declaration order. Stable because a published
    // COM interface never changes shape.

    internal static class D3D11Vtbl
    {
        // ID3D11Device methods (after IUnknown's 3).
        public const int Device_CreateBuffer               = 3;
        public const int Device_CreateTexture2D            = 5;
        public const int Device_CreateShaderResourceView   = 7;
        public const int Device_CreateUnorderedAccessView  = 8;
        public const int Device_CreateComputeShader        = 18;
        public const int Device_GetImmediateContext        = 40;

        // ID3D11DeviceContext methods (after IUnknown's 3 + ID3D11DeviceChild's 4).
        public const int Context_Map                       = 14;
        public const int Context_Unmap                     = 15;
        public const int Context_Dispatch                  = 41;
        public const int Context_CopySubresourceRegion     = 46;
        public const int Context_CopyResource              = 47;
        public const int Context_GenerateMips              = 54;
        public const int Context_CSSetShaderResources      = 67;
        public const int Context_CSSetUnorderedAccessViews = 68;
        public const int Context_CSSetShader               = 69;
        public const int Context_CSSetConstantBuffers      = 71;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BUFFER_DESC
    {
        public uint ByteWidth;
        public uint Usage;               // D3D11_USAGE
        public uint BindFlags;           // D3D11_BIND_FLAG
        public uint CPUAccessFlags;      // D3D11_CPU_ACCESS_FLAG
        public uint MiscFlags;           // D3D11_RESOURCE_MISC_FLAG
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SUBRESOURCE_DATA
    {
        public nint pSysMem;
        public uint SysMemPitch;
        public uint SysMemSlicePitch;
    }

    // D3D11_UNORDERED_ACCESS_VIEW_DESC for a buffer view; the union is
    // sized for its largest member (Texture3D, three uints), so the struct
    // is 20 bytes whichever dimension is used.
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_UAV_BUFFER_DESC
    {
        public uint Format;
        public uint ViewDimension;       // D3D11_UAV_DIMENSION
        public uint FirstElement;
        public uint NumElements;
        public uint Flags;               // D3D11_BUFFER_UAV_FLAG
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
    public const uint D3D11_USAGE_IMMUTABLE       = 1;
    public const uint D3D11_USAGE_STAGING         = 3;

    // D3D11_BIND_FLAG
    public const uint D3D11_BIND_SHADER_RESOURCE  = 0x8;
    public const uint D3D11_BIND_CONSTANT_BUFFER  = 0x4;
    public const uint D3D11_BIND_RENDER_TARGET    = 0x20;
    public const uint D3D11_BIND_UNORDERED_ACCESS = 0x80;

    // D3D11_UAV_DIMENSION / D3D11_BUFFER_UAV_FLAG
    public const uint D3D11_UAV_DIMENSION_BUFFER  = 1;
    public const uint D3D11_BUFFER_UAV_FLAG_RAW   = 0x1;

    // D3D11_RESOURCE_MISC_FLAG (buffers)
    public const uint D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS = 0x20;

    // D3D11_CPU_ACCESS_FLAG
    public const uint D3D11_CPU_ACCESS_WRITE      = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ       = 0x20000;

    // D3D11_RESOURCE_MISC_FLAG
    public const uint D3D11_RESOURCE_MISC_GENERATE_MIPS = 0x1;

    // D3D11_MAP
    public const uint D3D11_MAP_READ              = 1;

    // D3D11_MAP_FLAG: Map returns DXGI_ERROR_WAS_STILL_DRAWING at once
    // instead of waiting for the GPU.
    public const uint D3D11_MAP_FLAG_DO_NOT_WAIT  = 0x100000;
    public const int  DXGI_ERROR_WAS_STILL_DRAWING = unchecked((int)0x887A000A);

    // DXGI_FORMAT
    public const uint DXGI_FORMAT_R32_TYPELESS         = 39;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM       = 87;
    public const uint DXGI_FORMAT_R16G16B16A16_FLOAT   = 10;
}
