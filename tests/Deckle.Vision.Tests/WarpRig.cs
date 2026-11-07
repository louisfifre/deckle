using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Deckle.Vision.Tests;

// A Direct3D 11 device on WARP, the software rasteriser: the sampler's
// shaders and copies run for real, with no GPU and no display, so the tests
// that use it run on any build agent. Owns the textures it creates.
internal sealed class WarpRig : IDisposable
{
    private readonly List<nint> _textures = [];

    public IDirect3DDevice Device { get; } = ScreenCaptureInterop.CreateWarpDirect3DDevice();

    // A single-level, shader-readable texture filled from tightly packed
    // pixels: 4 bytes (B, G, R, A) per pixel in SDR, 8 (four halves) in HDR.
    public unsafe nint CreateTexture(int width, int height, bool hdr, byte[] pixels)
    {
        int bytesPerPixel = hdr ? 8 : 4;
        if (pixels.Length != width * height * bytesPerPixel)
            throw new ArgumentException("pixels does not match the size and format");

        nint device = ScreenCaptureInterop.GetD3D11Device(Device);
        try
        {
            var desc = new ScreenCaptureInterop.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = hdr
                    ? ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT
                    : ScreenCaptureInterop.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDescCount = 1,
                Usage = ScreenCaptureInterop.D3D11_USAGE_DEFAULT,
                BindFlags = ScreenCaptureInterop.D3D11_BIND_SHADER_RESOURCE,
            };

            var vtbl = *(nint**)device;
            var create = (delegate* unmanaged<nint, ScreenCaptureInterop.D3D11_TEXTURE2D_DESC*,
                    ScreenCaptureInterop.D3D11_SUBRESOURCE_DATA*, nint*, int>)
                vtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateTexture2D];

            fixed (byte* data = pixels)
            {
                var init = new ScreenCaptureInterop.D3D11_SUBRESOURCE_DATA
                {
                    pSysMem = (nint)data,
                    SysMemPitch = (uint)(width * bytesPerPixel),
                };
                nint texture;
                Marshal.ThrowExceptionForHR(create(device, &desc, &init, &texture));
                _textures.Add(texture);
                return texture;
            }
        }
        finally
        {
            Marshal.Release(device);
        }
    }

    public FrameSampler CreateSampler(int width, int height, bool hdr, FrameSamplerPath path)
        => new(
            Device,
            new SizeInt32 { Width = width, Height = height },
            hdr ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized,
            peakLuminance: 80f,
            path);

    public void Dispose()
    {
        foreach (var texture in _textures) Marshal.Release(texture);
        _textures.Clear();
        (Device as IDisposable)?.Dispose();
    }
}
