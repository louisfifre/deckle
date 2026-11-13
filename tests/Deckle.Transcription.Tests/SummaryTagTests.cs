using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Transcription.Tests;

// The take's figures are the summary the always-on log keeps: log.jsonl admits
// an event only through its tag, so a summary event that loses the tag drops out
// of the file without any other test noticing.
[Trait("Category", "observability")]
public sealed class SummaryTagTests
{
    [Theory]
    [InlineData(nameof(DeckleWhispSource.PipelineTimings))]
    [InlineData(nameof(DeckleWhispSource.PipelineOutputs))]
    [InlineData(nameof(DeckleWhispSource.StreamingDrainedDetail))]
    [InlineData(nameof(DeckleWhispSource.StreamingHeartbeat))]
    public void ATakeSummaryCarriesTheSummaryTag(string eventName)
    {
        MethodInfo method = typeof(DeckleWhispSource).GetMethod(eventName)!;
        EventAttribute attribute = method.GetCustomAttribute<EventAttribute>()!;

        Assert.True(ObservationTags.IsSummary(attribute.Tags), $"{eventName} lost its Summary tag");
    }
}
