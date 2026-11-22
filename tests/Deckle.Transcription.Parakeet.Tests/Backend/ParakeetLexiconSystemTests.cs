using System.Buffers.Binary;
using System.Diagnostics;
using Deckle.Transcription.Parakeet.Decoding;
using Deckle.Transcription.Parakeet.Tokenization;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

// The lexicon on the real model: the boost tree over the real vocabulary, and a take decoded with
// and without a large lexicon. System tests, because they depend on the maintainer's local model
// download (DECKLE_PARAKEET_MODEL_DIR); they skip when it is absent.
[Collection(ParakeetBackendCollection.Name)]
[Trait("Category", "system")]
public sealed class ParakeetLexiconSystemTests(ITestOutputHelper output)
{
    private static readonly string ModelFolder = Environment.GetEnvironmentVariable("DECKLE_PARAKEET_MODEL_DIR") ?? "";

    // Words that the reference clip does not contain, so a boost that leaked would show in its text.
    private static IReadOnlyList<string> UnrelatedTerms(int count) =>
        Enumerable.Range(0, count).Select(i => $"Zorblax{i:000}").Append("Deckle").Append("WinUI").Append("Anytype").ToList();

    [Fact]
    public void TheTreeBuildsOverTheRealVocabularyInAFewMilliseconds()
    {
        string path = new ParakeetModelFiles(ModelFolder).Vocabulary;
        Assert.SkipUnless(File.Exists(path), $"No model at {path}.");
        ParakeetVocabulary vocabulary = ParakeetVocabulary.Load(path);
        string[] texts = Enumerable.Range(0, vocabulary.Count).Select(vocabulary.TextOf).ToArray();
        IReadOnlyList<string> terms = UnrelatedTerms(500);

        var watch = Stopwatch.StartNew();
        LexiconBoostTree tree = LexiconBoostTree.Build(terms, texts, 2f)!;
        watch.Stop();

        output.WriteLine($"{tree.TermCount} terms, {tree.NodeCount} nodes, {tree.From(LexiconBoostTree.Root).Length} root tokens, built in {watch.ElapsedMilliseconds} ms");
        Assert.Equal(terms.Count, tree.TermCount);
        Assert.True(tree.From(LexiconBoostTree.Root).Length > 0);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"Built in {watch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task ALargeUnrelatedLexiconLeavesTheTextAndTheSpeedAlone()
    {
        var files = new ParakeetModelFiles(ModelFolder);
        Assert.SkipUnless(files.IsComplete, $"No complete Parakeet model at {ModelFolder}.");

        float[] speech = ReadReferenceSpeech();
        float[] pcm = Repeat(speech, copies: 6);

        var host = new FakeHost();
        host.Transcription.Engine.ParakeetModelDirectory = ModelFolder;
        using var backend = new ParakeetBackend(host);
        ModelLoadResult loaded = await backend.LoadModelAsync(TestContext.Current.CancellationToken);
        Assert.True(loaded.Success, loaded.ErrorReason);

        // Warm-up, then the same audio without and with the lexicon, twice each so a cold cache
        // on either side does not decide the comparison.
        await backend.TranscribeAsync(speech, null, TestContext.Current.CancellationToken);
        var plain = new List<long>();
        var boosted = new List<long>();
        string plainText = "", boostedText = "";
        for (int round = 0; round < 2; round++)
        {
            host.Transcription.Lexicon.Terms = [];
            var w = Stopwatch.StartNew();
            plainText = (await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken)).FullText;
            plain.Add(w.ElapsedMilliseconds);

            host.Transcription.Lexicon.Terms = [.. UnrelatedTerms(500)];
            w.Restart();
            boostedText = (await backend.TranscribeAsync(pcm, null, TestContext.Current.CancellationToken)).FullText;
            boosted.Add(w.ElapsedMilliseconds);
        }

        output.WriteLine($"audio {(double)pcm.Length / LongAudioChunker.SampleRate:F1} s; without lexicon {string.Join(", ", plain)} ms; with 503 terms {string.Join(", ", boosted)} ms");
        Assert.Equal(plainText, boostedText);
        // The boost is a few lookups per emitted token against an encoder pass; allow noise, not a doubling.
        Assert.True(boosted.Min() < plain.Min() * 1.5 + 200, $"without {plain.Min()} ms, with {boosted.Min()} ms");
    }

    [Fact]
    public async Task ATermWithCapitalsIsWrittenWithThem()
    {
        var files = new ParakeetModelFiles(ModelFolder);
        Assert.SkipUnless(files.IsComplete, $"No complete Parakeet model at {ModelFolder}.");

        var host = new FakeHost();
        host.Transcription.Engine.ParakeetModelDirectory = ModelFolder;
        host.Transcription.Lexicon.Terms = ["TRANSCRIPTION World"];
        using var backend = new ParakeetBackend(host);
        Assert.True((await backend.LoadModelAsync(TestContext.Current.CancellationToken)).Success);

        TranscriptionResult result = await backend.TranscribeAsync(ReadReferenceSpeech(), null, TestContext.Current.CancellationToken);

        output.WriteLine(result.FullText);
        Assert.Contains("TRANSCRIPTION World", result.FullText, StringComparison.Ordinal);
    }

    private static float[] Repeat(float[] speech, int copies)
    {
        float[] gap = new float[(int)(1.5 * LongAudioChunker.SampleRate)];
        var signal = new List<float>();
        for (int i = 0; i < copies; i++)
        {
            if (i > 0) signal.AddRange(gap);
            signal.AddRange(speech);
        }
        return [.. signal];
    }

    private static float[] ReadReferenceSpeech()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "speech.wav"));
        int position = 12;
        while (position + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4));
            int body = position + 8;
            if (id == "data")
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
