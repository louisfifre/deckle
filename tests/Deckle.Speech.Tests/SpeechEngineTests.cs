using System;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Speech;
using Xunit;

namespace Deckle.Speech.Tests;

// The backend is the injectable seam: a fake that parks inside
// SynthesizeAsync, so the cancellation is observable without any real
// synthesis, waveOut, or UI.
public class SpeechEngineTests
{
    private sealed class BlockingBackend : ISpeechBackend
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public CancellationToken LastToken;
        public int CallCount;

        public string Name => "fake";
        public bool IsModelLoaded => true;
        public string? DetectedAccelerator => "fake";
        public Task LoadModelAsync(CancellationToken ct) => Task.CompletedTask;
        public void UnloadModel() { }

        public async Task<float[]> SynthesizeAsync(
            string text, SpeechVoice voice, double temperature, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            LastToken = ct;
            Entered.Set();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Array.Empty<float>();
        }

        public void Dispose() { }
    }

    [Fact]
    [Trait("Category", "unit")]
    public void SecondSpeak_CancelsTheInFlightOne()
    {
        var backend = new BlockingBackend();
        using var engine = new SpeechEngine(backend);

        engine.Speak("first");
        Assert.True(backend.Entered.Wait(2000, TestContext.Current.CancellationToken), "the backend should start synthesizing the first request");
        CancellationToken firstToken = backend.LastToken;

        engine.Speak("second");

        // Speak cancels the previous CTS synchronously, before launching the new
        // task, so the first token is already cancelled when Speak returns.
        Assert.True(firstToken.IsCancellationRequested);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Speak_Whitespace_DoesNotReachTheBackend()
    {
        var backend = new BlockingBackend();
        using var engine = new SpeechEngine(backend);

        engine.Speak("   ");

        Assert.Equal(0, Volatile.Read(ref backend.CallCount));
    }
}
