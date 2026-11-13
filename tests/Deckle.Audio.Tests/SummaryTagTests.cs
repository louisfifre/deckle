using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Audio.Tests;

// The take's capture figures are a summary the always-on log keeps: log.jsonl
// admits a Verbose event only through its tag, so a summary event that loses
// the tag drops out of the file without any other test noticing.
[Trait("Category", "observability")]
public sealed class SummaryTagTests
{
    [Fact]
    public void TheCaptureSummaryCarriesTheSummaryTag()
    {
        MethodInfo method = typeof(DeckleAudioSource).GetMethod(nameof(DeckleAudioSource.CaptureCompleted))!;
        EventAttribute attribute = method.GetCustomAttribute<EventAttribute>()!;

        Assert.True(ObservationTags.IsSummary(attribute.Tags), "CaptureCompleted lost its Summary tag");
    }
}
