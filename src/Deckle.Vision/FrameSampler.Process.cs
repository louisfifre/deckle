using System.Diagnostics;

namespace Deckle.Vision;

public sealed partial class FrameSampler
{
    public bool Process(CapturedFrame frame)
    {
        if (_disposed) return false;
        if (frame.TexturePtr == 0) return false;

        // Borrowed: the capture service releases it after the handler.
        nint capturedTex = frame.TexturePtr;

        try
        {
            lock (_lock)
            {
                if (_disposed) return false;

                unsafe
                {
                    var ctxVtbl = *(nint**)_d3dContext;

                    // Not CopyResource: it needs matching mip counts, and the
                    // single-level capture against the full-chain
                    // intermediate fails silently, leaving every cell black.
                    var copySubresourceRegion = (delegate* unmanaged<nint, nint, uint, uint, uint, uint, nint, uint, void*, void>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CopySubresourceRegion];
                    copySubresourceRegion(_d3dContext, _intermediateTex, 0, 0, 0, 0, capturedTex, 0, null);

                    var generateMips = (delegate* unmanaged<nint, nint, void>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_GenerateMips];
                    generateMips(_d3dContext, _intermediateSrv);

                    copySubresourceRegion(_d3dContext, _stagingTex, 0, 0, 0, 0, _intermediateTex, (uint)_targetMip, null);

                    var map = (delegate* unmanaged<nint, nint, uint, uint, uint, ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE*, int>)
                        ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Map];
                    ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped;
                    int hr = map(_d3dContext, _stagingTex, 0, ScreenCaptureInterop.D3D11_MAP_READ, 0, &mapped);
                    if (hr != 0)
                    {
                        Volatile.Write(ref _lastFailure, new FrameFailure("sampler_map", hr, "", ""));
                        DeckleVisionSource.Log.SamplerMapFailedDetail(hr);
                        return false;
                    }

                    try
                    {
                        double elapsedSeconds = _lastProcessedTicks == 0
                            ? 0
                            : (frame.TimestampTicks - _lastProcessedTicks) / (double)Stopwatch.Frequency;
                        _lastProcessedTicks = frame.TimestampTicks;

                        var sample = ReadSampleFromMapped(in mapped, elapsedSeconds);
                        Volatile.Write(ref _latestSample, sample);
                    }
                    finally
                    {
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
            Volatile.Write(ref _lastFailure, new FrameFailure(
                "sampler_process", ex.HResult, ex.GetType().Name, ex.Message));
            // The event gates itself on the Ambient scope.
            DeckleVisionSource.Log.SamplerProcessFailedDetail(
                ex.GetType().Name, ex.Message);
            return false;
        }
    }

}
