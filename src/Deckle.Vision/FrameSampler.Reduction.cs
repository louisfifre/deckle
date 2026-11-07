using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Vision;

/// <summary>How <see cref="FrameSampler"/> reduces the desktop frame to its
/// grid.</summary>
internal enum FrameSamplerPath
{
    /// <summary>A compute shader averages the desktop texture straight into
    /// the grid; the full frame is never copied.</summary>
    GpuReduction,

    /// <summary>The full frame is copied into a texture with a mip chain,
    /// GenerateMips builds the pyramid, and one level is read back. Kept
    /// until the shader path has been seen on screen.</summary>
    Legacy,
}

public sealed partial class FrameSampler
{
    // The path a sampler takes when its constructor is not told: the shader
    // path, unless DECKLE_AMBIENT_SAMPLER_PATH is "legacy" in the process
    // environment. The variable is the maintainer's way to compare both on
    // screen without a rebuild; delete it with the legacy path.
    internal const string PathVariable = "DECKLE_AMBIENT_SAMPLER_PATH";

    internal static FrameSamplerPath DefaultPath { get; set; } = ResolveDefaultPath();

    private static FrameSamplerPath ResolveDefaultPath()
        => string.Equals(
            Environment.GetEnvironmentVariable(PathVariable), "legacy",
            StringComparison.OrdinalIgnoreCase)
            ? FrameSamplerPath.Legacy
            : FrameSamplerPath.GpuReduction;

    internal FrameSamplerPath ActivePath => _path;

    private const string ShaderResourceName = "Deckle.Vision.FrameReduce.cso";

    // Mirrors the cbuffer in FrameReduce.hlsl: eight uints, 32 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct ReduceParams
    {
        public uint SrcWidth;
        public uint SrcHeight;
        public uint Cols;
        public uint Rows;
        public uint IsHdr;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;
    }

    private nint _reduceShader;
    private nint _reduceParams;
    private nint _reduceOutput;
    private nint _reduceOutputUav;
    private nint _readbackBuffer;

    private int BytesPerCell => _isHdr ? 8 : 4;
    private int GridBytes => _gridCols * _gridRows * BytesPerCell;

    private static byte[] LoadShaderBytecode()
    {
        using var stream = typeof(FrameSampler).Assembly.GetManifestResourceStream(ShaderResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ShaderResourceName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void CreateReductionResources()
    {
        byte[] bytecode = LoadShaderBytecode();

        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;

            var createComputeShader = (delegate* unmanaged<nint, void*, nuint, nint, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateComputeShader];
            nint shader;
            fixed (byte* code = bytecode)
            {
                Marshal.ThrowExceptionForHR(
                    createComputeShader(_d3dDevice, code, (nuint)bytecode.Length, 0, &shader));
            }
            _reduceShader = shader;

            var createBuffer = (delegate* unmanaged<nint, ScreenCaptureInterop.D3D11_BUFFER_DESC*,
                    ScreenCaptureInterop.D3D11_SUBRESOURCE_DATA*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateBuffer];

            // The parameters never change for a sampler: immutable.
            var parameters = new ReduceParams
            {
                SrcWidth = (uint)_sourceWidth,
                SrcHeight = (uint)_sourceHeight,
                Cols = (uint)_gridCols,
                Rows = (uint)_gridRows,
                IsHdr = _isHdr ? 1u : 0u,
            };
            var paramsDesc = new ScreenCaptureInterop.D3D11_BUFFER_DESC
            {
                ByteWidth = (uint)sizeof(ReduceParams),
                Usage = ScreenCaptureInterop.D3D11_USAGE_IMMUTABLE,
                BindFlags = ScreenCaptureInterop.D3D11_BIND_CONSTANT_BUFFER,
            };
            var paramsData = new ScreenCaptureInterop.D3D11_SUBRESOURCE_DATA { pSysMem = (nint)(&parameters) };
            nint paramsBuffer;
            Marshal.ThrowExceptionForHR(createBuffer(_d3dDevice, &paramsDesc, &paramsData, &paramsBuffer));
            _reduceParams = paramsBuffer;

            // Raw (byte-addressable) so the shader writes BGRA bytes or
            // halves exactly as the readers expect them, whatever typed-UAV
            // formats the device supports.
            var outputDesc = new ScreenCaptureInterop.D3D11_BUFFER_DESC
            {
                ByteWidth = (uint)GridBytes,
                Usage = ScreenCaptureInterop.D3D11_USAGE_DEFAULT,
                BindFlags = ScreenCaptureInterop.D3D11_BIND_UNORDERED_ACCESS,
                MiscFlags = ScreenCaptureInterop.D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS,
            };
            nint output;
            Marshal.ThrowExceptionForHR(createBuffer(_d3dDevice, &outputDesc, null, &output));
            _reduceOutput = output;

            var createUav = (delegate* unmanaged<nint, nint, ScreenCaptureInterop.D3D11_UAV_BUFFER_DESC*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateUnorderedAccessView];
            var uavDesc = new ScreenCaptureInterop.D3D11_UAV_BUFFER_DESC
            {
                Format = ScreenCaptureInterop.DXGI_FORMAT_R32_TYPELESS,
                ViewDimension = ScreenCaptureInterop.D3D11_UAV_DIMENSION_BUFFER,
                FirstElement = 0,
                NumElements = (uint)(GridBytes / 4),
                Flags = ScreenCaptureInterop.D3D11_BUFFER_UAV_FLAG_RAW,
            };
            nint uav;
            Marshal.ThrowExceptionForHR(createUav(_d3dDevice, _reduceOutput, &uavDesc, &uav));
            _reduceOutputUav = uav;

            var readbackDesc = new ScreenCaptureInterop.D3D11_BUFFER_DESC
            {
                ByteWidth = (uint)GridBytes,
                Usage = ScreenCaptureInterop.D3D11_USAGE_STAGING,
                CPUAccessFlags = ScreenCaptureInterop.D3D11_CPU_ACCESS_READ,
            };
            nint readback;
            Marshal.ThrowExceptionForHR(createBuffer(_d3dDevice, &readbackDesc, null, &readback));
            _readbackBuffer = readback;
        }
    }

    // The buffers are a few tens of kilobytes and the closed vocabulary of
    // the resource trace has no buffer kind, so they are not traced; the
    // textures of the legacy path, which weigh megabytes, still are.
    private void ReleaseReductionResources()
    {
        ReleaseAndClear(ref _readbackBuffer);
        ReleaseAndClear(ref _reduceOutputUav);
        ReleaseAndClear(ref _reduceOutput);
        ReleaseAndClear(ref _reduceParams);
        ReleaseAndClear(ref _reduceShader);
    }

    private static void ReleaseAndClear(ref nint pointer)
    {
        if (pointer == 0) return;
        Marshal.Release(pointer);
        pointer = 0;
    }

    private enum ReduceOutcome { Sampled, Failed, Unsupported }

    // Dispatches the reduction for one desktop texture and reads the result
    // back. Unsupported (not Failed) when the desktop texture cannot be bound
    // as a shader input: the caller then runs the legacy path from here on.
    private ReduceOutcome ProcessReduced(CapturedFrame frame, nint capturedTex)
    {
        unsafe
        {
            var deviceVtbl = *(nint**)_d3dDevice;
            var ctxVtbl = *(nint**)_d3dContext;

            // Null desc: the view takes the texture's own format. The
            // duplication texture is not guaranteed to allow shader reads;
            // a refusal here is the signal to fall back.
            var createSrv = (delegate* unmanaged<nint, nint, void*, nint*, int>)
                deviceVtbl[ScreenCaptureInterop.D3D11Vtbl.Device_CreateShaderResourceView];
            nint srv;
            int srvHr = createSrv(_d3dDevice, capturedTex, null, &srv);
            if (srvHr != 0)
            {
                LogPathFallback(srvHr, "desktop_texture_not_shader_readable");
                return ReduceOutcome.Unsupported;
            }

            try
            {
                nint shader = _reduceShader;
                nint paramsBuffer = _reduceParams;
                nint uav = _reduceOutputUav;
                uint keepCounts = uint.MaxValue;

                var setShader = (delegate* unmanaged<nint, nint, nint*, uint, void>)
                    ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CSSetShader];
                var setConstants = (delegate* unmanaged<nint, uint, uint, nint*, void>)
                    ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CSSetConstantBuffers];
                var setResources = (delegate* unmanaged<nint, uint, uint, nint*, void>)
                    ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CSSetShaderResources];
                var setUavs = (delegate* unmanaged<nint, uint, uint, nint*, uint*, void>)
                    ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CSSetUnorderedAccessViews];
                var dispatch = (delegate* unmanaged<nint, uint, uint, uint, void>)
                    ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Dispatch];

                setShader(_d3dContext, shader, null, 0);
                setConstants(_d3dContext, 0, 1, &paramsBuffer);
                setResources(_d3dContext, 0, 1, &srv);
                setUavs(_d3dContext, 0, 1, &uav, &keepCounts);
                dispatch(_d3dContext, (uint)_gridCols, (uint)_gridRows, 1);

                // The device and its context belong to the capture service:
                // leave nothing bound, least of all the desktop texture the
                // duplication must be free to reuse.
                nint none = 0;
                setUavs(_d3dContext, 0, 1, &none, &keepCounts);
                setResources(_d3dContext, 0, 1, &none);
                setConstants(_d3dContext, 0, 1, &none);
                setShader(_d3dContext, 0, null, 0);
            }
            finally
            {
                Marshal.Release(srv);
            }

            var copyResource = (delegate* unmanaged<nint, nint, nint, void>)
                ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_CopyResource];
            copyResource(_d3dContext, _readbackBuffer, _reduceOutput);

            return ReadReadback(frame, _readbackBuffer);
        }
    }

    // Maps a readback buffer and publishes the grid it holds. A buffer maps
    // with no row pitch; the grid is tight, one row is cols * bytes per cell.
    private unsafe ReduceOutcome ReadReadback(CapturedFrame frame, nint readback)
    {
        var ctxVtbl = *(nint**)_d3dContext;
        var map = (delegate* unmanaged<nint, nint, uint, uint, uint, ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE*, int>)
            ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Map];
        ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped;
        int hr = map(_d3dContext, readback, 0, ScreenCaptureInterop.D3D11_MAP_READ, 0, &mapped);
        if (hr != 0)
        {
            Volatile.Write(ref _lastFailure, new FrameFailure("sampler_map", hr, "", ""));
            DeckleVisionSource.Log.SamplerMapFailedDetail(hr);
            return ReduceOutcome.Failed;
        }

        try
        {
            mapped.RowPitch = (uint)(_gridCols * BytesPerCell);
            PublishMapped(frame.TimestampTicks, in mapped);
        }
        finally
        {
            var unmap = (delegate* unmanaged<nint, nint, uint, void>)
                ctxVtbl[ScreenCaptureInterop.D3D11Vtbl.Context_Unmap];
            unmap(_d3dContext, readback, 0);
        }

        return ReduceOutcome.Sampled;
    }

    // Turns a mapped grid into the published sample; shared by both paths.
    private void PublishMapped(long timestampTicks, in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped)
    {
        double elapsedSeconds = _lastProcessedTicks == 0
            ? 0
            : (timestampTicks - _lastProcessedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastProcessedTicks = timestampTicks;

        var sample = ReadSampleFromMapped(in mapped, elapsedSeconds);
        Volatile.Write(ref _latestSample, sample);
    }

    private static int _lastLoggedPath = -1;

    private void LogPathSelected()
    {
        // Written when the path changes, not at each sampler build (a
        // resize or a format change rebuilds the sampler).
        int path = (int)_path;
        if (Interlocked.Exchange(ref _lastLoggedPath, path) == path) return;

        DeckleVisionSource.Log.SamplerPathSelected(
            _path == FrameSamplerPath.GpuReduction ? "gpu_reduction" : "legacy_mip_chain",
            Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 }
                ? "environment"
                : "default");
    }

    private void LogPathFallback(int hr, string reason)
        => DeckleVisionSource.Log.SamplerPathFallback("gpu_reduction", "legacy_mip_chain", hr, reason);
}
