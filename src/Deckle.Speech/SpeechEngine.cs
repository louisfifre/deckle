using System.Threading;
using System.Threading.Tasks;
using Deckle.Audio;

namespace Deckle.Speech;

// Read-aloud orchestrator — the synthesis-side counterpart of
// TranscriptionEngine. Turns a piece of text into spoken audio on the default
// render device. Kept deliberately minimal for the skeleton:
//
//   • Speak(text) returns immediately; synthesis + playback run on a
//     background task (the hotkey callback that calls it is on the UI thread).
//   • A new request interrupts the one in flight — pressing the hotkey again
//     stops the current read and starts the new one. No queue.
//
// Output is fixed at 24 kHz mono — Chatterbox's S3Gen rate. The backend (and
// later its voice/temperature settings) are read per call from
// SpeechSettingsService; no host-bridge is needed yet because the engine only
// reads its own module's settings.
public sealed class SpeechEngine : IDisposable
{
    private const int OutputSampleRate = 24000;

    private readonly ISpeechBackend _backend;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public SpeechEngine(ISpeechBackend backend)
    {
        _backend = backend;
    }

    // Fire-and-forget read of `text`. Empty/whitespace is a no-op. Any previous
    // read is cancelled first.
    public void Speak(string? text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;

        CancellationToken ct;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
        }

        var settings = SpeechSettingsService.Instance.Current;
        SpeechVoice voice = settings.Voice;
        double temperature = settings.Temperature;

        DeckleSpeechSource.Log.ReadAloudRequested();
        DeckleSpeechSource.Log.ReadAloudRequestedDetail(text!.Length, voice.ToString(), temperature);

        _ = Task.Run(() => SpeakCoreAsync(text!, voice, temperature, ct), ct);
    }

    private async Task SpeakCoreAsync(string text, SpeechVoice voice, double temperature, CancellationToken ct)
    {
        float[] samples;
        try
        {
            samples = await _backend.SynthesizeAsync(text, voice, temperature, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DeckleSpeechSource.Log.SynthesisFailed();
            DeckleSpeechSource.Log.SynthesisFailedDetail(ex.GetType().Name, ex.Message);
            return;
        }

        if (ct.IsCancellationRequested || samples.Length == 0) return;

        bool played;
        try
        {
            played = SpeakerOutput.Play(samples, OutputSampleRate, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DeckleSpeechSource.Log.PlaybackFailed();
            DeckleSpeechSource.Log.PlaybackFailedDetail(ex.GetType().Name, ex.Message);
            return;
        }

        // Interrupted mid-clip by a fresh Speak — no terminal milestone.
        if (ct.IsCancellationRequested) return;

        if (!played)
        {
            // The render device could not be opened (SpeakerOutput logged the
            // mmsys error under [AUDIO]); surface it in the speech narrative too
            // so a reader following [SPEECH] sees the read produced no sound.
            DeckleSpeechSource.Log.PlaybackFailed();
            return;
        }

        // Close the read-aloud bracket: ReadAloudRequested → ReadAloudComplete
        // makes the happy path legible at Informational level.
        long durationMs = samples.Length * 1000L / OutputSampleRate;
        DeckleSpeechSource.Log.ReadAloudComplete();
        DeckleSpeechSource.Log.ReadAloudCompleteDetail(samples.Length, durationMs, OutputSampleRate);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        _backend.Dispose();
    }
}
