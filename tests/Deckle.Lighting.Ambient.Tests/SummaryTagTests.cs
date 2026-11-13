using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The push heartbeat is a summary the always-on log keeps: log.jsonl admits a
// Verbose event only through its tag, so a heartbeat that loses the tag drops
// out of the file without any other test noticing.
[Trait("Category", "observability")]
public sealed class SummaryTagTests
{
    [Fact]
    public void ThePushHeartbeatCarriesTheSummaryTag()
    {
        MethodInfo method = typeof(DeckleAmbientSource).GetMethod(nameof(DeckleAmbientSource.Heartbeat))!;
        EventAttribute attribute = method.GetCustomAttribute<EventAttribute>()!;

        Assert.True(ObservationTags.IsSummary(attribute.Tags), "Heartbeat lost its Summary tag");
    }
}
