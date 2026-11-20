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

        string audioPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "speech.wav");
        float[] pcm = ReadPcm16MonoWav(audioPath);

        var host = new FakeHost();
        host.Transcription.Engine.ParakeetModelDirectory = ModelFolder;
        using var backend = new ParakeetBackend(host);

        ModelLoadResult loaded = await backend.LoadModelAsync(TestContext.Current.CancellationToken);
        Assert.True(loaded.Success, loaded.ErrorReason);

        TranscriptionResult result = await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken);

        Assert.False(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.NotEmpty(result.Segments);
        Assert.False(string.IsNullOrWhiteSpace(result.FullText));
        // Verified on the spike with every configuration: the reference clip says "Welcome to
        // Transcription World." (English, although the brief called it French).
        Assert.Contains("transcription", result.FullText, StringComparison.OrdinalIgnoreCase);
    }

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
