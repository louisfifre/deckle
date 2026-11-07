namespace Deckle.Audio;

// Live microphone source consumed by audio orchestrators. The production
// implementation owns a WASAPI stream; deterministic substitutes can emit the
// same CaptureFrame stream without requiring interactive hardware.
public interface IMicrophoneCapture : IDisposable
{
    event Action<float>? AudioLevel;
    event Action<CaptureFrame>? Frame;
    event Action? CaptureStarted;
    event Action? LowAudioDetected;

    // The per-packet level and the description of how the microphone was opened,
    // for an instrument that follows the voice as it happens. Optional: a
    // substitute that has neither need not implement them, and a subscriber then
    // simply hears nothing.
    event Action<CaptureLevel>? Level { add { } remove { } }
    event Action<CaptureSource>? Opened { add { } remove { } }

    // endpointId is an MMDevice endpoint id; empty is the system default device.
    ProbeResult Probe(string endpointId);
    CaptureResult Record(IAudioRecordingHost host, CancellationToken cancellationToken);
}
