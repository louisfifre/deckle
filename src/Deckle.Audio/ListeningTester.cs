using System;
using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Audio;

// ── ListeningTester ────────────────────────────────────────────────────────
//
// Drives the Recording-page listening test: records a short take from the
// microphone, then plays a render of it on the default output device. Thin on
// purpose, like MicLevelTester: the capture is the existing MicrophoneCapture,
// the render is the pure ListeningTake, the output is SpeakerOutput; this only
// wires them and owns the threading.
//
// Capture follows MicLevelTester: a fresh MicrophoneCapture per take and a
// throwaway host whose duration cap IS the take length, so the capture stops
// itself. Playback follows SpeechEngine: one CancellationTokenSource swapped
// under a lock, so a new play (or a new take) cancels the clip in flight.
//
// No log of its own: MicrophoneCapture and SpeakerOutput log the take and the
// playback on the Deckle-Audio provider, and TakeHost's RecordingPurpose tells
// the take apart from a dictation there.
public sealed class ListeningTester : IDisposable
{
    // One spoken sentence: long enough for the compressor's 150 ms release to
    // be audible, short enough not to feel like a chore.
    public const int TakeSeconds = 6;

    private readonly object _lock = new();
    private CancellationTokenSource? _playCts;
    private bool _disposed;

    // Records a take of TakeSeconds from `deviceId`. Returns null when the
    // microphone failed or captured nothing. Stops any playback first, so the
    // speakers are not heard in the take.
    public async Task<ListeningTake?> RecordAsync(int deviceId, CancellationToken ct = default)
    {
        StopPlayback();

        using var capture = new MicrophoneCapture();
        var host = new TakeHost(deviceId, TakeSeconds);

        // Record blocks for TakeSeconds (its duration cap) or until ct fires.
        CaptureResult result = await Task.Run(() => capture.Record(host, ct), ct).ConfigureAwait(false);

        if (result.Outcome == CaptureOutcome.MicError || result.Pcm.Length == 0) return null;

        return new ListeningTake(result.Pcm);
    }

    // Plays `samples` (16 kHz mono) on the default render device and completes
    // when the clip ends. Cancels the previous play first. Returns false only
    // when the clip never reached the device (empty clip, device refused to
    // open or take the buffer) or the tester is disposed; a play
    // interrupted by a newer one or by StopPlayback counts as true.
    public async Task<bool> PlayAsync(float[] samples)
    {
        CancellationToken ct;
        lock (_lock)
        {
            if (_disposed) return false;
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = new CancellationTokenSource();
            ct = _playCts.Token;
        }

        try
        {
            return await Task.Run(
                () => SpeakerOutput.Play(samples, (int)TranscriptionPreprocessor.SampleRate, ct),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the render thread started: interrupted, not failed.
            return true;
        }
    }

    public void StopPlayback()
    {
        lock (_lock)
        {
            _playCts?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = null;
        }
    }

    // Throwaway host: the chosen device, a hard cap so the capture stops
    // itself, and telemetry off — a listening take must not write the
    // per-recording telemetry event a real dictation does.
    private sealed class TakeHost(int deviceId, int seconds) : IAudioRecordingHost
    {
        public int AudioInputDeviceId => deviceId;
        public int MaxRecordingDurationSeconds => seconds;
        public bool MicrophoneTelemetryEnabled => false;
        public string RecordingPurpose => "listening_test";
    }
}
