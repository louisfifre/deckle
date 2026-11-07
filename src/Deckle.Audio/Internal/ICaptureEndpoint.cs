namespace Deckle.Audio.Internal;

// The seam between the capture loop and the audio device. The production
// implementation is WASAPI (WasapiCaptureEndpoint); a test stands a scripted
// endpoint behind it and drives the whole loop, the format conversion and the
// anomaly handling without a microphone.

// Which device a take ran on, as the log names it. EndpointId is the MMDevice
// endpoint id ({0.0.1.00000000}.{guid}); IsDefault says the system default
// device was asked for, whatever it resolved to.
internal readonly record struct CaptureDeviceIdentity(string EndpointId, string Name, bool IsDefault)
{
    public const string UnknownName = "unknown";

    // Before a device could be resolved, or when no listener needs the name.
    public static CaptureDeviceIdentity Unresolved(string requestedEndpointId)
        => new(requestedEndpointId, UnknownName, string.IsNullOrEmpty(requestedEndpointId));
}

// What the opened endpoint is, once, for the take.
//   Mode        the processing mode Windows granted.
//   Format      the device's mix format, which the packets arrive in.
//   PeriodMs    the interval at which the device hands over a packet.
//   LowLatency  true when the engine accepted a period shorter than its default.
internal readonly record struct CaptureEndpointInfo(
    CaptureDeviceIdentity Device,
    CaptureMode Mode,
    CaptureFormat Format,
    double PeriodMs,
    bool LowLatency);

// AUDCLNT_BUFFERFLAGS_* values the loop reacts to (audioclient.h).
[Flags]
internal enum CapturePacketFlags
{
    None = 0,

    // Data was lost between the previous packet and this one. The first packet
    // of a stream carries it by design, so the loop ignores it there.
    Discontinuity = 1,

    // The device delivers silence: muted, blocked by the microphone privacy
    // setting, or a driver with nothing to say. The packet's content is not
    // meaningful and is read as zeros.
    Silent = 2,
}

// Receives one packet's bytes in the device's format, valid only during the
// call. For a silent packet the span is empty and frames still counts the
// packet's length.
internal delegate void CapturePacketHandler(ReadOnlySpan<byte> data, int frames, CapturePacketFlags flags);

internal enum PacketReadStatus
{
    // A packet was handed to the handler.
    Packet,

    // Nothing is waiting.
    Empty,

    // The device went away (unplugged, disabled, format changed): the stream
    // cannot continue.
    DeviceLost,

    // Any other refusal; the HRESULT says which.
    Failed,
}

internal interface ICaptureEndpoint : IDisposable
{
    CaptureEndpointInfo Info { get; }

    // Both return an HRESULT, 0 on success.
    int Start();
    int Stop();

    // Blocks until the device signals that a packet is ready or the timeout
    // passes. True when signalled. A device that stops signalling shows as
    // timeouts, which the loop's no-data watchdog turns into a warning.
    bool WaitForData(int timeoutMilliseconds);

    // Hands the next waiting packet to the handler, if any.
    PacketReadStatus ReadPacket(CapturePacketHandler handler, out int hresult);
}

// A refused stage of opening: which call the device or the engine refused, and
// the HRESULT. Stage names are the log's vocabulary ("activate",
// "set_client_properties", "get_mix_format", "initialize", ...).
//
// Fallbacks lists every step down the provider took to get an endpoint, in
// order, so the capture can log what was asked and what was obtained.
internal sealed record CaptureOpenResult(
    ICaptureEndpoint? Endpoint,
    string? FailedStage,
    int Hresult,
    CaptureDeviceIdentity Device,
    IReadOnlyList<CaptureModeFallback> Fallbacks)
{
    public static CaptureOpenResult Opened(ICaptureEndpoint endpoint, IReadOnlyList<CaptureModeFallback> fallbacks)
        => new(endpoint, null, 0, endpoint.Info.Device, fallbacks);

    public static CaptureOpenResult Failed(string stage, int hresult, CaptureDeviceIdentity device)
        => new(null, stage, hresult, device, []);
}

// One step down: the reason the provider gave up what it first tried.
//   Reason   raw_unsupported (the endpoint does not declare raw processing),
//            raw_refused (it declared it but the engine refused the stream),
//            low_latency_refused (the engine kept its default period).
internal readonly record struct CaptureModeFallback(string Reason, int Hresult);

internal interface ICaptureEndpointProvider
{
    // Opens the endpoint with the given MMDevice id, or the system default
    // capture device when the id is empty. The endpoint is prepared but not
    // started.
    CaptureOpenResult Open(string endpointId);
}
