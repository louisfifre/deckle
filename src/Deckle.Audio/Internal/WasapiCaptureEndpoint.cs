using Deckle.Core;

namespace Deckle.Audio.Internal;

// One opened shared-mode capture stream: the IAudioClient, its
// IAudioCaptureClient and the event the engine signals each period. Built by
// WasapiEndpointProvider, which owns every decision about how it was opened;
// this class only runs it.
//
// Used from one thread, the one that opened it, inside a ComApartment.
internal sealed class WasapiCaptureEndpoint : ICaptureEndpoint
{
    private nint _client;
    private nint _capture;
    private nint _event;        // 0 when the endpoint is polled, not event-driven
    private readonly int _blockAlign;
    private bool _disposed;

    // The endpoint takes ownership of the three handles; each is released by
    // Dispose.
    public WasapiCaptureEndpoint(CaptureEndpointInfo info, nint client, nint capture, nint eventHandle)
    {
        Info = info;
        _client = client;
        _capture = capture;
        _event = eventHandle;
        _blockAlign = info.Format.BlockAlign;
    }

    public CaptureEndpointInfo Info { get; }

    public int Start() => WasapiInterop.ClientStart(_client);

    public int Stop() => WasapiInterop.ClientStop(_client);

    // Event-driven: sleeps on the engine's signal, so the wake comes within one
    // period of the sound. A polled endpoint (its driver does not support the
    // event) sleeps a quarter of a period instead and reports a wake each time;
    // ReadPacket then finds nothing or a packet.
    public bool WaitForData(int timeoutMilliseconds)
    {
        if (_event != 0)
            return NativeMethods.WaitForSingleObject(_event, (uint)timeoutMilliseconds) == 0;

        Thread.Sleep(Math.Clamp((int)(Info.PeriodMs / 4), 1, timeoutMilliseconds));
        return true;
    }

    public unsafe PacketReadStatus ReadPacket(CapturePacketHandler handler, out int hresult)
    {
        hresult = WasapiInterop.CaptureNextPacketSize(_capture, out uint waiting);
        if (hresult < 0) return Lost(hresult);
        if (waiting == 0) return PacketReadStatus.Empty;

        hresult = WasapiInterop.CaptureGetBuffer(_capture, out byte* data, out uint frames, out uint flags);
        if (hresult < 0) return Lost(hresult);
        if (hresult == WasapiInterop.AUDCLNT_S_BUFFER_EMPTY) return PacketReadStatus.Empty;

        try
        {
            var packetFlags = CapturePacketFlags.None;
            if ((flags & WasapiInterop.AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0)
                packetFlags |= CapturePacketFlags.Discontinuity;

            bool silent = (flags & WasapiInterop.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
            if (silent) packetFlags |= CapturePacketFlags.Silent;

            // A silent packet's bytes are undefined: the handler gets none, only
            // the length.
            ReadOnlySpan<byte> bytes = silent
                ? ReadOnlySpan<byte>.Empty
                : new ReadOnlySpan<byte>(data, checked((int)frames * _blockAlign));
            handler(bytes, (int)frames, packetFlags);
        }
        finally
        {
            // Always released, even if the handler throws: an unreleased buffer
            // would stall the stream for the rest of the take.
            WasapiInterop.CaptureReleaseBuffer(_capture, frames);
        }
        return PacketReadStatus.Packet;
    }

    private static PacketReadStatus Lost(int hresult)
        => hresult is WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED
            or WasapiInterop.AUDCLNT_E_RESOURCES_INVALIDATED
            or WasapiInterop.E_NOTFOUND
            ? PacketReadStatus.DeviceLost
            : PacketReadStatus.Failed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        WasapiInterop.Release(_capture);
        WasapiInterop.Release(_client);
        _capture = 0;
        _client = 0;
        if (_event != 0)
        {
            NativeMethods.CloseHandle(_event);
            _event = 0;
        }
    }
}
