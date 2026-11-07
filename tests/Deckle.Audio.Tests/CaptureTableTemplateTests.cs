using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

// The capture figures of each take are table rows: takes compare with each
// other column by column whatever the values.
[Trait("Category", "unit")]
public sealed class CaptureTableTemplateTests
{
    public static TheoryData<string, object?[], object?[]> Rows => new()
    {
        {
            nameof(DeckleAudioSource.CaptureCompleted),
            [0.5, 1, 1600, 0.0, 0.0, -120.0],
            [9999.9, 99999, 99999999, 1.0, 1.0, -9.5]
        },
        {
            nameof(DeckleAudioSource.CaptureDrainDetail),
            [0L, 0, 0],
            [99999L, 999999, 4]
        },
        {
            nameof(DeckleAudioSource.RecordingTailSummaryDetail),
            [50, -9.5],
            [600, -120.0]
        },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void ColumnsHoldAcrossNarrowAndWideValues(string eventName, object?[] narrow, object?[] wide)
    {
        MethodInfo method = typeof(DeckleAudioSource).GetMethod(eventName)
            ?? throw new InvalidOperationException($"no event {eventName}");
        string message = method.GetCustomAttribute<EventAttribute>()?.Message
            ?? throw new InvalidOperationException($"{eventName} has no Message");

        Assert.Null(TableTemplate.Breach(message, narrow, wide));
    }
}
