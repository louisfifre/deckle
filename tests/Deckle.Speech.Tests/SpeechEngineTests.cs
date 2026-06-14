using System;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Speech;
using Xunit;

namespace Deckle.Speech.Tests;

// Pins the read-aloud orchestrator's load-bearing behavior: interrupt-on-press
// (a second Speak cancels the one in flight) and the whitespace no-op. The
// backend is the injectable seam — a fake that parks inside SynthesizeAsync, so
// the cancellation is observable without any real synthesis, waveOut, or UI.
public class SpeechEngineTests
{
    // ISpeechBackend stub that blocks inside SynthesizeAsync until its token is
    // cancelled, exposing the token it was handed so a test can assert a later
    // Speak cancelled it.
    private sealed class BlockingBackend : ISpeechBackend
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public CancellationToken LastToken;

        public string Name => "fake";
        public bool IsModelLoaded => true;
        public string? DetectedAccelerator => "fake";
        public Task LoadModelAsync(CancellationToken ct) => Task.CompletedTask;
        public void UnloadModel() { }

        public async Task<float[]> SynthesizeAsync(
            string text, SpeechVoice voice, double temperature, CancellationToken ct)
        {
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

        Assert.False(backend.Entered.Wait(200, TestContext.Current.CancellationToken), "whitespace text must be a no-op — the backend is never called");
    }
}
