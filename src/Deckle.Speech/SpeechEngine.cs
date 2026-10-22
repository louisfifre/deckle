using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Audio;

namespace Deckle.Speech;

// Read-aloud orchestrator, the synthesis-side counterpart of
// TranscriptionEngine.
//
//   • Speak(text) returns immediately; synthesis + playback run on a
//     background task, so a caller on the UI thread is never blocked.
//   • A new request interrupts the one in flight. No queue.
//
// Output is fixed at 24 kHz mono, Chatterbox's S3Gen rate. Voice and
// temperature are read per call from SpeechSettingsService: the engine reads
// only its own module's settings, so it needs no host bridge.
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
        DeckleSpeechSource.Log.SpeechEngineStarted(backend.Name, backend.DetectedAccelerator ?? "none");
    }

    public void Speak(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        CancellationToken ct;
        lock (_lock)
        {
            // Checked under the lock Dispose takes to set the flag: a Speak
            // cannot start a request on a backend Dispose is about to release.
            if (_disposed) return;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
        }

        var settings = SpeechSettingsService.Instance.Current;
        SpeechVoice voice = settings.Voice;
        double temperature = settings.Temperature;

        long requestedAt = Stopwatch.GetTimestamp();
        DeckleSpeechSource.Log.ReadAloudRequested();
        DeckleSpeechSource.Log.ReadAloudRequestedDetail(text!.Length, voice.ToString(), temperature);

        // Not handed ct: a request cancelled before the task starts must still
        // run SpeakCoreAsync, whose finally closes the ReadAloudRequested bracket.
        _ = Task.Run(() => SpeakCoreAsync(text!, voice, temperature, requestedAt, ct));
    }

    private async Task SpeakCoreAsync(
        string text, SpeechVoice voice, double temperature, long requestedAt, CancellationToken ct)
    {
        // Every planned exit sets reason, and outcome when it is not a failure;
        // "unexpected" stands only if something unforeseen escapes.
        string outcome = "failed";
        string reason = "unexpected";
        int playedSamples = 0;
        try
        {
            if (ct.IsCancellationRequested)
            {
                (outcome, reason) = Cancelled();
                return;
            }

            float[] samples;
            try
            {
                samples = await _backend.SynthesizeAsync(text, voice, temperature, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                (outcome, reason) = Cancelled();
                return;
            }
            catch (Exception ex)
            {
                DeckleSpeechSource.Log.SpeechSynthesisFailed(ex.GetType().Name, ex.Message);
                reason = "synthesis_failed";
                return;
            }

            if (ct.IsCancellationRequested)
            {
                (outcome, reason) = Cancelled();
                return;
            }
            if (samples.Length == 0)
            {
                reason = "no_audio";
                return;
            }

            bool played;
            try
            {
                played = SpeakerOutput.Play(samples, OutputSampleRate, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                (outcome, reason) = Cancelled();
                return;
            }
            catch (Exception ex)
            {
                DeckleSpeechSource.Log.SpeechPlaybackFailed("exception", ex.GetType().Name, ex.Message);
                reason = "playback_failed";
                return;
            }

            // Interrupted mid-clip by a fresh Speak or by Dispose.
            if (ct.IsCancellationRequested)
            {
                (outcome, reason) = Cancelled();
                return;
            }

            if (!played)
            {
                // The render device could not be opened (SpeakerOutput logged the
                // mmsys error under [AUDIO]); surface it in the speech narrative too
                // so a reader following [SPEECH] sees the read produced no sound.
                DeckleSpeechSource.Log.SpeechPlaybackFailed("device_unavailable", "", "");
                reason = "playback_failed";
                return;
            }

            playedSamples = samples.Length;
            (outcome, reason) = ("completed", "played");
        }
        finally
        {
            long elapsedMs = (long)Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds;
            DeckleSpeechSource.Log.ReadAloudEnded(outcome, reason, elapsedMs);
            if (playedSamples > 0)
            {
                long durationMs = playedSamples * 1000L / OutputSampleRate;
                DeckleSpeechSource.Log.ReadAloudCompleteDetail(playedSamples, durationMs, OutputSampleRate);
            }
        }
    }

    // Dispose sets _disposed before it cancels, so a cancelled request reads
    // which of the two ended it.
    private (string Outcome, string Reason) Cancelled()
        => ("cancelled", Volatile.Read(ref _disposed) ? "disposed" : "interrupted");

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        _backend.Dispose();
        DeckleSpeechSource.Log.SpeechEngineStopped();
    }
}
