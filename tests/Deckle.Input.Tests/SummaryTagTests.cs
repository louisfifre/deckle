using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Input.Tests;

// The input rollups are summaries the always-on log keeps: log.jsonl admits a
// Verbose event only through its tag, so a rollup that loses the tag drops out
// of the file without any other test noticing.
[Trait("Category", "observability")]
public sealed class SummaryTagTests
{
    [Theory]
    [InlineData(nameof(DeckleInputSource.FrameRollup))]
    [InlineData(nameof(DeckleInputSource.KeyboardRollup))]
    public void AnInputRollupCarriesTheSummaryTag(string eventName)
    {
        MethodInfo method = typeof(DeckleInputSource).GetMethod(eventName)!;
        EventAttribute attribute = method.GetCustomAttribute<EventAttribute>()!;

        Assert.True(ObservationTags.IsSummary(attribute.Tags), $"{eventName} lost its Summary tag");
    }
}
