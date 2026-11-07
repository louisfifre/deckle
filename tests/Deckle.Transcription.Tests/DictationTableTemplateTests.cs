using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// The dictation lines a person compares row by row, the heartbeat of a take,
// its utterances and segments, and the figures of each take, keep their
// columns in place whatever the values. Each sample pair is the narrowest and
// the widest value the field takes in practice, read from real traces.
[Trait("Category", "unit")]
public sealed class DictationTableTemplateTests
{
    public static TheoryData<string, object?[], object?[]> Rows => new()
    {
        {
            nameof(DeckleWhispSource.StreamingHeartbeat),
            ["Speech", -9.5, -1.0, 0, 500, 0, 0, 0],
            ["Hangover", -120.0, -120.0, 999950, 5000, 99, 999, 99999]
        },
        {
            nameof(DeckleWhispSource.SegmenterUtteranceEmitted),
            [0, 1, 1, 0.0, 0.5, 0],
            [999, 99999, 99999, 9999.99, 9999.99, 15000]
        },
        {
            nameof(DeckleWhispSource.UtteranceSentToBackend),
            [0, 200, -9.5, -0.1, 0],
            [999, 600000, -120.0, -120.0, 1000]
        },
        {
            nameof(DeckleWhispSource.ConsumerUtterance),
            [0, 0L, 0, 0, 0, false],
            [999, 999999L, 9999, 999, 999, true]
        },
        {
            nameof(DeckleWhispSource.UtteranceSkippedRepeat),
            [0, "IOException", "short"],
            [999, "IOException", "a longer message"]
        },
        {
            nameof(DeckleWhispSource.SegmenterBlipDropped),
            [1, 50],
            [999, 9999]
        },
        {
            nameof(DeckleWhispSource.SegmentRecognized),
            [0, 0.0, 0.5, 0.5, 0.0, 0.0, 0.0, 0, 0, 0],
            [999, 9999.9, 9999.9, 999.9, 1.0, 1.0, 1.0, 9999, 9999, 99999]
        },
        {
            nameof(DeckleWhispSource.TranscriptionPreprocessed),
            [-9.5, -9.5, 0.0, 0.5],
            [-120.0, -120.0, 99.9, 1.0]
        },
        {
            nameof(DeckleWhispSource.TranscribeDecodeUsed),
            [0.0, 0, true, true, 0.0, 0.2, 0, 0.0, 0.5, true],
            [9999.99, 9999, false, false, 1.0, 0.2, 999, 9999.99, 9999.99, false]
        },
        {
            nameof(DeckleWhispSource.StreamingDrainedDetail),
            [0, 0.5, 0L, 0, 0],
            [999, 9999.9, 999999L, 99999, 999]
        },
        {
            nameof(DeckleWhispSource.PipelineTimings),
            [0.5, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L],
            [9999.9, 999999L, 99999L, 99999L, 999999L, 999999L, 999999L, 999999L, 99999L, 99999L]
        },
        {
            nameof(DeckleWhispSource.PipelineLlmMetrics),
            [0L, 0L, 0L, 0, 0],
            [999999L, 999999L, 999999L, 999999, 999999]
        },
        {
            nameof(DeckleWhispSource.PipelineOutputs),
            [0, 0, 0, "whisper", "(none)", "None"],
            [999, 999999, 99999, "whisper", "Arrangement", "ClipboardOnly"]
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void ColumnsHoldAcrossNarrowAndWideValues(string eventName, object?[] narrow, object?[] wide)
    {
        Assert.Null(TableTemplate.Breach(Message(eventName), narrow, wide));
    }

    private static string Message(string eventName)
    {
        MethodInfo method = typeof(DeckleWhispSource).GetMethod(eventName)
            ?? throw new InvalidOperationException($"no event {eventName}");
        return method.GetCustomAttribute<EventAttribute>()?.Message
            ?? throw new InvalidOperationException($"{eventName} has no Message");
    }
}
