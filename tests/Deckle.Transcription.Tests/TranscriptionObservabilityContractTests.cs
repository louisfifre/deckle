using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "observability")]
[Collection(OperationalObservabilityCollection.Name)]
public sealed class TranscriptionObservabilityContractTests : IDisposable
{
    public TranscriptionObservabilityContractTests()
        => OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void OperationalDiagnosticsCarryMeasurementsButNoSpokenContent()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.TranscribePromptConfigured(42, carry: true);
        DeckleWhispSource.Log.TranscribeRepetitionLoopMetrics(streak: 3, period: 1);
        DeckleWhispSource.Log.SegmentRecognized(
            index: 1,
            start_s: 0,
            end_s: 1.2,
            duration_s: 1.2,
            no_speech_probability: 0.1f,
            average_probability: 0.9f,
            minimum_probability: 0.8f,
            text_tokens: 4,
            tokens: 6,
            characters: 24);

        Assert.Collection(listener.Events,
            prompt => AssertPayloadNames(prompt, "prompt_len", "carry"),
            repetition => AssertPayloadNames(repetition, "streak", "period"),
            segment => AssertPayloadNames(segment,
                "index", "start_s", "end_s", "duration_s", "no_speech_probability",
                "average_probability", "minimum_probability", "text_tokens", "tokens",
                "characters"));
    }

    [Fact]
    public void CorrelationIdJoinsOperationalLogAndCorpusDataset()
    {
        using var listener = new TestEventListener("Deckle-Whisp");
        const string id = "0123456789abcdef0123456789abcdef";

        // The take's start milestone carries the id the corpus dataset
        // repeats, at every verbosity.
        DeckleWhispSource.Log.TakeStarted("w1", "dictation", id);
        DeckleWhispSource.Log.CorpusAsrRecorded(
            transcription_id: id,
            audio_file: "",
            bucket: "raw",
            tier: "short",
            backend: "whisper",
            model: "test",
            language: "fr",
            prompt_or_instruction: "",
            text: "test",
            text_words: 1,
            text_chars: 4,
            duration_seconds: 1,
            words_per_second: 1,
            elapsed_ms: 1,
            audio_content: "raw");
        Assert.Collection(listener.Events,
            operational => Assert.Equal(id, PayloadValue(operational, "transcription_id")),
            dataset =>
            {
                Assert.Equal(ObservationTags.Dataset, dataset.Tags & ObservationTags.Dataset);
                Assert.Equal(id, PayloadValue(dataset, "transcription_id"));
            });
    }

    private static void AssertPayloadNames(EventWrittenEventArgs e, params string[] expected) =>
        Assert.Equal(expected, e.PayloadNames);

    private static object? PayloadValue(EventWrittenEventArgs e, string name)
    {
        int index = e.PayloadNames?.IndexOf(name) ?? -1;
        return index >= 0 ? e.Payload?[index] : null;
    }
}
