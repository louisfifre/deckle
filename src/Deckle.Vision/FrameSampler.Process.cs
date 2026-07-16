using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class FrameSampler
{
    public bool Process(CapturedFrame frame)
    {
        if (_disposed) return false;
        if (frame.TexturePtr == 0) return false;

        // The texture pointer is borrowed — the capture service owns
        // it for the duration of this handler and Releases on return.
        // We do not Marshal.Release it ourselves.
        nint capturedTex = frame.TexturePtr;

        try
        {
            lock (_lock)
            {
                if (_disposed) return false;

                unsafe
                {
                    var ctxVtbl = *(nint**)_d3dContext;

                    // Copy the captured frame's mip 0 into the intermediate's
                    // mip 0. We can NOT use ID3D11DeviceContext::CopyResource
                    // here : it requires src and dst to have the same mip
                    // level count, and our intermediate has a full mip chain
                    // while the captured frame is single-level. The silent
                    // failure mode of CopyResource on mismatched MipLevels
                    // leaves the intermediate uninitialised (zeros), which
                    // is what gave every grid cell a black sample at first
                    // run. CopySubresourceRegion copies a specific
                    // (subresource, region) pair and is the right tool.
                    var copySubresourceRegion = (delegate* unmanaged<nint, nint, uint, uint, uint, uint, nint, uint, void*, void>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CopySubresourceRegion];
                    copySubresourceRegion(_d3dContext, _intermediateTex, 0, 0, 0, 0, capturedTex, 0, null);

                    // GenerateMips on the intermediate SRV — fills mip 1+
                    // by averaging mip 0 in hardware.
                    var generateMips = (delegate* unmanaged<nint, nint, void>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_GenerateMips];
                    generateMips(_d3dContext, _intermediateSrv);

                    // CopySubresourceRegion(staging, 0, 0,0,0, intermediate, mip, null).
                    copySubresourceRegion(_d3dContext, _stagingTex, 0, 0, 0, 0, _intermediateTex, (uint)_targetMip, null);

                    // Map(staging, 0, READ, 0, &mapped).
                    var map = (delegate* unmanaged<nint, nint, uint, uint, uint, ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE*, int>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Map];
                    ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped;
                    int hr = map(_d3dContext, _stagingTex, 0, ScreenCaptureInterop.D3D11_MAP_READ, 0, &mapped);
                    if (hr != 0)
                    {
                        DeckleVisionSource.Log.SamplerMapFailedDetail(hr);
                        return false;
                    }

                    try
                    {
                        var sample = ReadSampleFromMapped(in mapped);
                        Volatile.Write(ref _latestSample, sample);
                    }
                    finally
                    {
                        // Unmap(staging, 0).
                        var unmap = (delegate* unmanaged<nint, nint, uint, void>)
                            ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Unmap];
                        unmap(_d3dContext, _stagingTex, 0);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            if (OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleVisionSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Pipeline))
            {
                DeckleVisionSource.Log.SamplerProcessFailedDetail(
                    ex.GetType().Name, ex.Message);
            }
            return false;
        }
    }

}
