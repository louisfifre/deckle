namespace Deckle.Audio.Internal;

// The seam between the capture loop and the audio device: start, stop, and
// packets of sound. It says nothing of Windows, so the loop, the signal path
// and their tests stay the same whatever sits behind it. The production
// implementation is PortAudio (PortAudioCaptureEndpoint), whose WASAPI host API
// serves Windows today and whose other host APIs (ALSA, PipeWire, Core Audio,
// ASIO) would serve another platform or interface without touching the loop. A
// test stands a scripted endpoint behind it and drives the loop without a
// microphone.

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

// What the opened stream is, once, for the take.
//   Mode            the processing mode obtained.
//   Exclusive       the stream holds the device alone (WASAPI exclusive mode).
//   Format          the format the frames arrive in.
//   InputLatencyMs  the latency the backend reports for the stream, the
//                   closest it says to how long a sound takes to reach a read.
internal readonly record struct CaptureEndpointInfo(
    CaptureDeviceIdentity Device,
    CaptureMode Mode,
    bool Exclusive,
    CaptureFormat Format,
    double InputLatencyMs);

internal interface ICaptureEndpoint : IDisposable
{
    CaptureEndpointInfo Info { get; }

    // Both return 0 on success, an error code otherwise (CaptureErrors).
    int Start();
    int Stop();

    // Frames ready to read without waiting, or a negative error code when the
    // device can no longer deliver (unplugged, disabled, format changed).
    int Available();

    // Reads exactly frames frames, interleaved, into the destination, which
    // holds at least frames * Format.Channels samples. The loop asks only for
    // what Available reported, so the read never waits. 0 on success.
    int Read(Span<float> interleaved, int frames);
}

// A refused open: which step refused, and the error code. Stage names are the
// log's vocabulary (resolve, open, start).
//
// Fallbacks lists every step down the provider took to get a stream, in order,
// so the capture can log what was asked and what was obtained.
internal sealed record CaptureOpenResult(
    ICaptureEndpoint? Endpoint,
    string? FailedStage,
    int ErrorCode,
    CaptureDeviceIdentity Device,
    IReadOnlyList<CaptureModeFallback> Fallbacks)
{
    public static CaptureOpenResult Opened(ICaptureEndpoint endpoint, IReadOnlyList<CaptureModeFallback> fallbacks)
        => new(endpoint, null, 0, endpoint.Info.Device, fallbacks);

    public static CaptureOpenResult Failed(string stage, int errorCode, CaptureDeviceIdentity device, IReadOnlyList<CaptureModeFallback> fallbacks)
        => new(null, stage, errorCode, device, fallbacks);
}

// One step down: what the provider gave up, and the error that made it.
//   Reason   exclusive_refused (the device could not be held alone, so it is
//            shared), raw_refused (the device refused the raw processing mode,
//            so the default one is used).
internal readonly record struct CaptureModeFallback(string Reason, int ErrorCode);

// How the take asks for the device.
//   Exclusive  hold the device alone, past the Windows audio engine (a setting,
//              off by default: no other application can use the microphone
//              while Deckle records).
internal readonly record struct CaptureOptions(bool Exclusive);

internal interface ICaptureEndpointProvider
{
    // Opens the endpoint with the given MMDevice id, or the system default
    // capture device when the id is empty. The stream is open but not started.
    CaptureOpenResult Open(string endpointId, CaptureOptions options);
}
