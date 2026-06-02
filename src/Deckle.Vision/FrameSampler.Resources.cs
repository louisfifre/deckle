using System.Diagnostics;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class FrameSampler
{
    private nint CreateIntermediateTexture()
    {
        var desc = new ScreenCaptureInterop.D3D11_TEXTURE2D_DESC
        {
            Width             = (uint)_sourceWidth,
            Height            = (uint)_sourceHeight,
            MipLevels         = 0,           // 0 = full mip chain
            ArraySize         = 1,
            Format            = _dxgiFormat,
            SampleDescCount   = 1,
            SampleDescQuality = 0,
            Usage             = ScreenCaptureInterop.D3D11_USAGE_DEFAULT,
            BindFlags         = ScreenCaptureInterop.D3D11_BIND_SHADER_RESOURCE
                              | ScreenCaptureInterop.D3D11_BIND_RENDER_TARGET,
            CPUAccessFlags    = 0,
            MiscFlags         = ScreenCaptureInterop.D3D11_RESOURCE_MISC_GENERATE_MIPS,
        };

        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;
            var createTexture2D = (delegate* unmanaged<nint, ScreenCaptureInterop.D3D11_TEXTURE2D_DESC*, void*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateTexture2D];
            nint texPtr;
            int hr = createTexture2D(_d3dDevice, &desc, null, &texPtr);
            Marshal.ThrowExceptionForHR(hr);
            return texPtr;
        }
    }

    private nint CreateIntermediateSrv()
    {
        // null SRV desc → SRV inherits the texture's format and full
        // mip chain. Sufficient for GenerateMips.
        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;
            var createSrv = (delegate* unmanaged<nint, nint, void*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateShaderResourceView];
            nint srvPtr;
            int hr = createSrv(_d3dDevice, _intermediateTex, null, &srvPtr);
            Marshal.ThrowExceptionForHR(hr);
            return srvPtr;
        }
    }

    private nint CreateStagingTexture()
    {
        var desc = new ScreenCaptureInterop.D3D11_TEXTURE2D_DESC
        {
            Width             = (uint)_gridCols,
            Height            = (uint)_gridRows,
            MipLevels         = 1,
            ArraySize         = 1,
            Format            = _dxgiFormat,
            SampleDescCount   = 1,
            SampleDescQuality = 0,
            Usage             = ScreenCaptureInterop.D3D11_USAGE_STAGING,
            BindFlags         = 0,
            CPUAccessFlags    = ScreenCaptureInterop.D3D11_CPU_ACCESS_READ,
            MiscFlags         = 0,
        };

        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;
            var createTexture2D = (delegate* unmanaged<nint, ScreenCaptureInterop.D3D11_TEXTURE2D_DESC*, void*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateTexture2D];
            nint texPtr;
            int hr = createTexture2D(_d3dDevice, &desc, null, &texPtr);
            Marshal.ThrowExceptionForHR(hr);
            return texPtr;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;

        lock (_lock)
        {
            _disposed = true;

            // Sub-provider transverse Resource — release des trois
            // textures persistantes. Le contexte et le device ne sont
            // pas tracés ici parce qu'ils sont borrowed (AddRef'd
            // depuis l'extérieur via ScreenCaptureInterop.GetD3D11Device,
            // owned ailleurs) — leur cycle de vie n'est pas spécifique
            // au sampler.
            if (_intermediateSrv != 0)
            {
                long h = (long)_intermediateSrv;
                Marshal.Release(_intermediateSrv);
                _intermediateSrv = 0;
                // SRV : pas de timestamp tracké séparément, ageMs=0
                // (acquire et release effectivement simultanés à
                // l'échelle de la trace dispose).
                DeckleResourceSource.Log.ResourceReleased(
                    "dxgi-resource", h, 0, "frame-sampler");
            }
            if (_intermediateTex != 0)
            {
                long h = (long)_intermediateTex;
                int ageMs = (int)((Stopwatch.GetTimestamp() - _intermediateTexAcquiredTicks)
                                   * 1000L / Stopwatch.Frequency);
                Marshal.Release(_intermediateTex);
                _intermediateTex = 0;
                DeckleResourceSource.Log.ResourceReleased(
                    "d3d11-texture", h, ageMs, "frame-sampler");
            }
            if (_stagingTex != 0)
            {
                long h = (long)_stagingTex;
                int ageMs = (int)((Stopwatch.GetTimestamp() - _stagingTexAcquiredTicks)
                                   * 1000L / Stopwatch.Frequency);
                Marshal.Release(_stagingTex);
                _stagingTex = 0;
                DeckleResourceSource.Log.ResourceReleased(
                    "d3d11-texture", h, ageMs, "frame-sampler");
            }
            if (_d3dContext != 0)      { Marshal.Release(_d3dContext);      _d3dContext = 0; }
            if (_d3dDevice != 0)       { Marshal.Release(_d3dDevice);       _d3dDevice = 0; }
        }

        return ValueTask.CompletedTask;
    }}
