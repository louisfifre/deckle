using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

[Collection(ParakeetBackendCollection.Name)]
[Trait("Category", "unit")]
public sealed class ParakeetBackendTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16_000)];

    private static async Task<ParakeetBackend> LoadedAsync(FakeLoader loader, FakeHost? host = null)
    {
        var backend = new ParakeetBackend(host ?? new FakeHost(), loader);
        ModelLoadResult load = await backend.LoadModelAsync(Ct);
        Assert.True(load.Success);
        return backend;
    }

    // ── Loading ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadReportsTheAcceleratorAndIsIdempotent()
    {
        var loader = FakeLoader.Succeeding(accelerator: ParakeetAccelerators.DirectMl);
        using var backend = new ParakeetBackend(new FakeHost(), loader);

        ModelLoadResult first = await backend.LoadModelAsync(Ct);
        ModelLoadResult second = await backend.LoadModelAsync(Ct);

        Assert.True(first.Success);
        Assert.Equal("DirectML", first.Accelerator);
        Assert.Equal("DirectML", backend.DetectedAccelerator);
        Assert.True(backend.IsModelLoaded);
        Assert.True(second.Success);
        Assert.Equal(0, second.LoadDurationMs);
        Assert.Equal(1, loader.Loads);
    }

    [Fact]
    public async Task LoadPassesTheSettingsChoicesToTheLoader()
    {
        var host = new FakeHost();
        host.Transcription.Engine.ParakeetEncoder = ParakeetEncoderPrecision.Fp16;
        host.Transcription.Engine.UseGpu = false;
        var loader = FakeLoader.Succeeding();
        using var backend = new ParakeetBackend(host, loader);

        await backend.LoadModelAsync(Ct);

        Assert.Equal(ParakeetEncoderPrecision.Fp16, loader.LastPrecision);
        Assert.False(loader.LastUseGpu);
    }

    [Theory]
    [InlineData("file_not_found")]
    [InlineData("init_failed")]
    public async Task LoadFailureCarriesTheStableReasonAndTheFileItStoppedOn(string reason)
    {
        var loader = new FakeLoader(() => ParakeetLoadOutcome.Failed(reason, @"C:\models\x\vocab.txt"));
        using var backend = new ParakeetBackend(new FakeHost(), loader);

        ModelLoadResult result = await backend.LoadModelAsync(Ct);

        Assert.False(result.Success);
        Assert.Equal(reason, result.ErrorReason);
        Assert.Equal(@"C:\models\x\vocab.txt", result.ModelPath);
        Assert.True(result.GpuRequested);
        Assert.False(backend.IsModelLoaded);
        Assert.Null(backend.DetectedAccelerator);
    }

    [Fact]
    public async Task LoadHonoursACancelledToken()
    {
        using var backend = new ParakeetBackend(new FakeHost(), FakeLoader.Succeeding());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.LoadModelAsync(cts.Token));
    }

    [Fact]
    public async Task UnloadFreesTheModelAndIsIdempotent()
    {
        int disposed = 0;
        using var backend = await LoadedAsync(FakeLoader.Succeeding(onDispose: () => disposed++));

        backend.UnloadModel();
        backend.UnloadModel();

        Assert.Equal(1, disposed);
        Assert.False(backend.IsModelLoaded);
        TranscriptionResult after = await backend.TranscribeAsync(Audio(1), null, Ct);
        Assert.Equal("no_context", after.ErrorReason);
    }

    [Fact]
    public async Task UnloadWaitsForARunningTranscription()
    {
        var inEncoder = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var encoder = new FakeEncoder { OnEncode = _ => { inEncoder.Set(); release.Wait(Ct); } };
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));

        Task<TranscriptionResult> running = Task.Run(() => backend.TranscribeAsync(Audio(1), null, Ct), Ct);
        inEncoder.Wait(Ct);
        Task unload = Task.Run(backend.UnloadModel, Ct);

        // The sessions are in use: freeing them now would crash the process.
        Assert.NotSame(unload, await Task.WhenAny(unload, Task.Delay(150, Ct)));
        release.Set();
        TranscriptionResult result = await running;
        await unload;

        Assert.Equal(0, result.ResultCode);
        Assert.NotEmpty(result.Segments);
        Assert.False(backend.IsModelLoaded);
    }

    // ── Transcription ────────────────────────────────────────────────

    [Fact]
    public async Task TranscribeWithoutAModelReportsNoContext()
    {
        using var backend = new ParakeetBackend(new FakeHost(), FakeLoader.Succeeding());

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), null, Ct);

        Assert.Equal(-1, result.ResultCode);
        Assert.Equal("no_context", result.ErrorReason);
        Assert.Empty(result.Segments);
        Assert.False(result.Aborted);
    }

    [Fact]
    public async Task EmptyAudioIsASuccessWithNoText()
    {
        var encoder = new FakeEncoder();
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));

        TranscriptionResult result = await backend.TranscribeAsync(ReadOnlyMemory<float>.Empty, null, Ct);

        Assert.Equal(0, result.ResultCode);
        Assert.Empty(result.Segments);
        Assert.Equal("", result.FullText);
        Assert.Equal(0, encoder.Calls);
    }

    [Fact]
    public async Task ShortAudioBecomesOneSegmentAlsoDeliveredToTheSink()
    {
        using var backend = await LoadedAsync(FakeLoader.Succeeding());
        var sunk = new List<TranscriptionSegment>();

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), sunk.Add, Ct);

        Assert.Equal(0, result.ResultCode);
        Assert.False(result.Aborted);
        TranscriptionSegment segment = Assert.Single(result.Segments);
        Assert.Equal("hello world.", segment.Text);
        Assert.Equal(0, segment.T0Cs);
        Assert.Equal(24, segment.T1Cs); // last token on frame 2, so the segment ends with frame 3 at 8 cs a frame
        Assert.Equal("hello world.", result.FullText);
        Assert.Equal(result.Segments, sunk);
    }

    [Fact]
    public async Task ControlTokensNeverReachTheText()
    {
        int[] script = [FakeVocabulary.Hello, FakeVocabulary.Unknown, FakeVocabulary.World];
        using var backend = await LoadedAsync(FakeLoader.Succeeding(script: script));

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), null, Ct);

        Assert.Equal("hello world", result.FullText);
    }

    [Fact]
    public async Task LongAudioIsEncodedInChunksOnOneTimeline()
    {
        var encoder = new FakeEncoder();
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));
        float[] audio = Audio(70);
        IReadOnlyList<AudioChunk> chunks = LongAudioChunker.Split(audio);

        TranscriptionResult result = await backend.TranscribeAsync(audio, null, Ct);

        Assert.True(chunks.Count >= 3);
        Assert.Equal(chunks.Count, encoder.Calls);
        // Each chunk starts the same script, so every segment must start at its chunk's offset.
        Assert.Equal(
            chunks.Select(c => (long)c.Offset * 100 / 16_000),
            result.Segments.Select(s => s.T0Cs));
        Assert.Equal(string.Join(" ", Enumerable.Repeat("hello world.", chunks.Count)), result.FullText);
    }

    [Fact]
    public async Task ATimelineOffsetDoesNotShiftTheReturnedSegments()
    {
        using var backend = await LoadedAsync(FakeLoader.Succeeding());

        TranscriptionResult result = await backend.TranscribeAsync(
            Audio(1), null, Ct, new TranscriptionContext(null, TimelineOffsetSec: 42));

        // The contract: segment times are relative to the call; the offset only dresses the log.
        Assert.Equal(0, Assert.Single(result.Segments).T0Cs);
    }

    [Fact]
    public async Task PrimingAndContinuityAreIgnored()
    {
        using var backend = await LoadedAsync(FakeLoader.Succeeding());

        TranscriptionResult plain = await backend.TranscribeAsync(Audio(1), null, Ct);
        TranscriptionResult primed = await backend.TranscribeAsync(
            Audio(1), null, Ct, new TranscriptionContext("a style sample", ContinuesPreviousCall: true));

        Assert.Equal(plain.FullText, primed.FullText);
    }

    // ── Cancellation and failure ─────────────────────────────────────

    [Fact]
    public async Task CancellationBetweenChunksAbortsAndKeepsTheFinishedOnes()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var encoder = new FakeEncoder { OnEncode = call => { if (call == 2) cts.Cancel(); } };
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));

        TranscriptionResult result = await backend.TranscribeAsync(Audio(70), null, cts.Token);

        Assert.True(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.Null(result.ErrorReason);
        // The second chunk was cancelled while encoding: only the first chunk's segment remains.
        Assert.Single(result.Segments);
        Assert.Equal(2, encoder.Calls);
    }

    [Fact]
    public async Task ACancelledTokenAbortsBeforeAnyWork()
    {
        var encoder = new FakeEncoder();
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), null, cts.Token);

        Assert.True(result.Aborted);
        Assert.Empty(result.Segments);
        Assert.Equal(0, encoder.Calls);
    }

    [Fact]
    public async Task AnInferenceFailureIsAResultNotAnException()
    {
        var encoder = new FakeEncoder { Failure = new InvalidOperationException("boom") };
        using var backend = await LoadedAsync(FakeLoader.Succeeding(encoder));

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), null, Ct);

        Assert.Equal(-1, result.ResultCode);
        Assert.Equal("inference_failed", result.ErrorReason);
        Assert.Empty(result.Segments);
        Assert.False(result.Aborted);
    }

    [Fact]
    public async Task ASinkThatThrowsDoesNotLoseTheCall()
    {
        using var backend = await LoadedAsync(FakeLoader.Succeeding());

        TranscriptionResult result = await backend.TranscribeAsync(
            Audio(1), _ => throw new InvalidOperationException("sink"), Ct);

        Assert.Equal(0, result.ResultCode);
        Assert.Single(result.Segments);
    }
}
