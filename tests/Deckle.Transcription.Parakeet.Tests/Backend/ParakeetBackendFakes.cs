using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription.Parakeet.Decoding;
using Deckle.Transcription.Parakeet.Tokenization;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

// The Deckle-Parakeet provider is process-wide and the tests listen to it, so the tests that
// run the backend do not run in parallel with each other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParakeetBackendCollection
{
    public const string Name = "Parakeet backend";
}

internal sealed class FakeHost : ITranscriptionEngineHost
{
    public TranscriptionSettings Transcription { get; } = new();
    public CaptureSettings Audio { get; } = new();
    public TelemetrySettings Telemetry { get; } = new();
    public LlmSettings Llm { get; } = new();
    public string ResolveModelsDirectory() => Path.GetTempPath();
}

// Vocabulary: 0 <unk> (silent), 1 "hello", 2 "world", 3 ".", 4 blank.
internal static class FakeVocabulary
{
    public const int Hello = 1, World = 2, Period = 3, Blank = 4, Unknown = 0;

    public static ParakeetVocabulary Create() => ParakeetVocabulary.Parse(
        ["<unk> 0", "▁hello 1", "▁world 2", ". 3", "<blk> 4"]);
}

// One frame per 80 ms (1280 samples); frame[0] carries its own index so the joint can read it.
internal sealed class FakeEncoder : IParakeetEncoder
{
    public int Calls { get; private set; }
    public Action<int>? OnEncode { get; init; }
    public Exception? Failure { get; init; }

    public IReadOnlyList<ReadOnlyMemory<float>> Encode(ReadOnlySpan<float> samples)
    {
        Calls++;
        OnEncode?.Invoke(Calls);
        if (Failure is not null) throw Failure;
        int count = Math.Max(1, samples.Length / 1280);
        return Enumerable.Range(0, count).Select(i => (ReadOnlyMemory<float>)new float[] { i }).ToArray();
    }
}

// Emits Script[t] on frame t (blank after the script), always jumping one frame.
internal sealed class FakeJoint : ITdtJoint
{
    public required int[] Script { get; init; }

    public TdtDecoderState CreateInitialState() => new([0], [0]);

    public TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state)
    {
        int frame = (int)encoderFrame.Span[0];
        int token = frame < Script.Length ? Script[frame] : FakeVocabulary.Blank;
        var tokenLogits = new float[5];
        tokenLogits[token] = 5f;
        var durationLogits = new float[5];
        durationLogits[1] = 1f;
        return new TdtJointOutput(tokenLogits, durationLogits, state);
    }
}

internal sealed class FakeLoader : IParakeetModelLoader
{
    public FakeLoader(Func<ParakeetLoadOutcome> outcome) => _outcome = outcome;

    private readonly Func<ParakeetLoadOutcome> _outcome;
    public int Loads { get; private set; }
    public ParakeetEncoderPrecision? LastPrecision { get; private set; }
    public bool? LastUseGpu { get; private set; }

    public ParakeetLoadOutcome Load(ParakeetModelFiles files, ParakeetEncoderPrecision precision, bool useGpu)
    {
        Loads++;
        LastPrecision = precision;
        LastUseGpu = useGpu;
        return _outcome();
    }

    public static FakeLoader Succeeding(
        FakeEncoder? encoder = null,
        int[]? script = null,
        string accelerator = ParakeetAccelerators.DirectMl,
        string? fallbackType = null,
        Action? onDispose = null) =>
        new(() => ParakeetLoadOutcome.Loaded(new ParakeetLoadedModel(
            FakeVocabulary.Create(),
            encoder ?? new FakeEncoder(),
            new FakeJoint { Script = script ?? [FakeVocabulary.Hello, FakeVocabulary.World, FakeVocabulary.Period] },
            accelerator,
            fallbackType,
            new DisposeProbe(onDispose))));

    private sealed class DisposeProbe(Action? onDispose) : IDisposable
    {
        public void Dispose() => onDispose?.Invoke();
    }
}
