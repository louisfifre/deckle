using System.Buffers.Binary;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

// Loads the real model (about 2.4 GB of FP32 weights) and transcribes the repository's reference
// speech. It is a system test because it depends on the maintainer's local download; it skips when
// the model folder is absent, and nothing of the model or the audio is copied into the repository.
[Collection(ParakeetBackendCollection.Name)]
[Trait("Category", "system")]
public sealed class ParakeetBackendSystemTests
{
    private static readonly string ModelFolder = Environment.GetEnvironmentVariable("DECKLE_PARAKEET_MODEL_DIR") ?? "";

    [Fact]
    public async Task InstalledParakeetTranscribesTheReferenceSpeech()
    {
        var files = new ParakeetModelFiles(ModelFolder);
        Assert.SkipUnless(files.IsComplete, $"No complete Parakeet model at {ModelFolder}.");

        float[] pcm = ReadReferenceSpeech();
        using var backend = await LoadBackendAsync(ParakeetEncoderPrecision.Fp32);

        TranscriptionResult result = await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken);

        Assert.False(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.NotEmpty(result.Segments);
        Assert.False(string.IsNullOrWhiteSpace(result.FullText));
        // Verified on the spike with every configuration: the reference clip says "Welcome to
        // Transcription World." (English, although the brief called it French).
        Assert.Contains("transcription", result.FullText, StringComparison.OrdinalIgnoreCase);
    }

    // The FP16 encoder is a separate file with its own weights: it can load and still transcribe
    // badly, so it gets the same check as the FP32 one rather than a load-only test.
    [Fact]
    public async Task InstalledParakeetFp16EncoderTranscribesTheReferenceSpeech()
    {
        var files = new ParakeetModelFiles(ModelFolder);
        Assert.SkipUnless(files.IsComplete && files.HasFp16Encoder, $"No Parakeet model with an FP16 encoder at {ModelFolder}.");

        float[] pcm = ReadReferenceSpeech();
        using var backend = await LoadBackendAsync(ParakeetEncoderPrecision.Fp16);

        TranscriptionResult result = await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken);

        Assert.False(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.NotEmpty(result.Segments);
        Assert.Contains("transcription", result.FullText, StringComparison.OrdinalIgnoreCase);
    }

    // Audio over the 30 s chunk limit goes through LongAudioChunker, which cuts at a pause. The
    // signal is the reference speech repeated with 1.5 s of silence between copies, so every cut
    // can land in a gap; one copy is allowed to be lost in case a cut still lands badly.
    [Fact]
    public async Task InstalledParakeetTranscribesAudioLongerThanOneChunk()
    {
        var files = new ParakeetModelFiles(ModelFolder);
        Assert.SkipUnless(files.IsComplete, $"No complete Parakeet model at {ModelFolder}.");

        float[] speech = ReadReferenceSpeech();
        const int copies = 12;
        float[] gap = new float[(int)(1.5 * LongAudioChunker.SampleRate)];
        var signal = new List<float>();
        for (int i = 0; i < copies; i++)
        {
            if (i > 0) signal.AddRange(gap);
            signal.AddRange(speech);
        }
        float[] pcm = signal.ToArray();
        double audioSeconds = (double)pcm.Length / LongAudioChunker.SampleRate;

        Assert.True(audioSeconds > 40, $"The signal lasts {audioSeconds:F1} s, expected well over one chunk.");
        Assert.True(LongAudioChunker.Split(pcm).Count >= 2, "The signal must be cut into at least two chunks.");

        using var backend = await LoadBackendAsync(ParakeetEncoderPrecision.Fp32);
        TranscriptionResult result = await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken);

        Assert.False(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.True(string.IsNullOrEmpty(result.ErrorReason), result.ErrorReason);
        Assert.NotEmpty(result.Segments);

        for (int i = 1; i < result.Segments.Count; i++)
            Assert.True(result.Segments[i].T0Cs >= result.Segments[i - 1].T0Cs,
                $"Segment {i} starts at {result.Segments[i].T0Cs} cs, before segment {i - 1} at {result.Segments[i - 1].T0Cs} cs.");

        double lastEndSeconds = result.Segments[^1].T1Cs / 100.0;
        Assert.InRange(lastEndSeconds, audioSeconds - 2.5, audioSeconds + 0.5);

        int occurrences = System.Text.RegularExpressions.Regex.Matches(result.FullText, "transcription", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        Assert.True(occurrences >= copies - 1, $"Heard \"transcription\" {occurrences} times for {copies} copies: {result.FullText}");
    }

    private static async Task<ParakeetBackend> LoadBackendAsync(ParakeetEncoderPrecision precision)
    {
        var host = new FakeHost();
        host.Transcription.Engine.ParakeetModelDirectory = ModelFolder;
        host.Transcription.Engine.ParakeetEncoder = precision;
        var backend = new ParakeetBackend(host);
        ModelLoadResult loaded = await backend.LoadModelAsync(TestContext.Current.CancellationToken);
        if (!loaded.Success)
        {
            backend.Dispose();
            Assert.Fail(loaded.ErrorReason);
        }
        return backend;
    }

    private static float[] ReadReferenceSpeech() =>
        ReadPcm16MonoWav(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "speech.wav"));
    // The reference clip is PCM16 mono 16 kHz, so no conversion is needed; anything else is a
    // different clip and the test says so rather than feeding the model the wrong rate.
    private static float[] ReadPcm16MonoWav(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        int position = 12;
        while (position + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4));
            int body = position + 8;
            if (id == "fmt ")
            {
                Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body)));      // PCM
                Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 2)));  // mono
                Assert.Equal(16_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4)));
                Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + 14)));
            }
            else if (id == "data")
            {
                int count = Math.Min(size, bytes.Length - body) / 2;
                var pcm = new float[count];
                for (int i = 0; i < count; i++)
                    pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + i * 2)) / 32768f;
                return pcm;
            }
            position = body + size + (size & 1);
        }
        throw new InvalidDataException("No data chunk in the reference clip.");
    }
}
