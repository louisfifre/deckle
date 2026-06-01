using System.Threading;
using System.Threading.Tasks;
using Deckle.Audio.Preprocessing;

namespace Deckle.Audio;

// ── MicLevelTester ─────────────────────────────────────────────────────────
//
// Drives the Recording-page mic check: records a short sample on a background
// thread, then hands it to MicLevelCheck for the verdict. Thin on purpose — the
// capture is the existing MicrophoneCapture, the assessment is the pure
// MicLevelCheck, this just wires the two and owns the throwaway recording host.
//
// One-shot and self-contained: a fresh MicrophoneCapture per call, opened and
// closed inside Record(), so it never touches the orchestrator's own capture
// instance. The user is in Settings, not dictating, so there is no contention
// in practice; the cancellation token still lets the page abandon a measure if
// it is navigated away mid-capture.
public sealed class MicLevelTester
{
    // A few seconds of continuous speech is enough for a stable level read and
    // short enough not to feel like a chore. Record stops itself at the cap.
    public const int DefaultMeasureSeconds = 5;

    public async Task<MicLevelAssessment> MeasureAsync(
        int deviceId,
        PreprocessingSettings settings,
        int seconds = DefaultMeasureSeconds,
        CancellationToken ct = default)
    {
        using var capture = new MicrophoneCapture();
        var host = new MeasureHost(deviceId, seconds);

        // Record blocks for `seconds` (its duration cap) or until ct fires.
        CaptureResult result = await Task.Run(() => capture.Record(host, ct), ct).ConfigureAwait(false);

        if (result.Outcome == CaptureOutcome.MicError || result.Pcm.Length == 0)
        {
            return new MicLevelAssessment(false, -120.0, -120.0, settings.TargetRmsDbfs, 0.0, PreprocessingAdvice.NotNeeded);
        }

        return MicLevelCheck.Assess(result.Pcm, settings);
    }

    // Throwaway host: the configured device, a hard duration cap so the capture
    // self-terminates, and telemetry emission off — a mic check must not write a
    // per-recording telemetry event the way a real dictation does.
    private sealed class MeasureHost(int deviceId, int seconds) : IAudioRecordingHost
    {
        public int AudioInputDeviceId => deviceId;
        public int MaxRecordingDurationSeconds => seconds;
        public bool MicrophoneTelemetryEnabled => false;
    }
}
