namespace Deckle.Audio;

// Live microphone source consumed by audio orchestrators. The production
// implementation owns waveIn; deterministic substitutes can emit the same
// CaptureFrame stream without requiring interactive hardware.
public interface IMicrophoneCapture : IDisposable
{
    event Action<float>? AudioLevel;
    event Action<CaptureFrame>? Frame;
    event Action? CaptureStarted;
    event Action? LowAudioDetected;

    ProbeResult Probe(int deviceId);
    CaptureResult Record(IAudioRecordingHost host, CancellationToken cancellationToken);
}
