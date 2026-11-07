using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The Ambient heartbeat is a table row: consecutive periods compare column by
// column whatever the values.
[Trait("Category", "unit")]
public sealed class AmbientTableTemplateTests
{
    [Fact]
    public void HeartbeatColumnsHoldAcrossNarrowAndWideValues()
    {
        string message = typeof(DeckleAmbientSource)
            .GetMethod(nameof(DeckleAmbientSource.Heartbeat))!
            .GetCustomAttribute<EventAttribute>()!
            .Message!;

        Assert.Null(TableTemplate.Breach(
            message,
            ["group", 5.0, 10, 0.0, 0, 0, 0, 0L, 0, 0, 0.0, 0.0, 0.0],
            ["multi", 99.9, 100, 100.0, 99999, 99999, 99999, 99999L, 999, 99999, 9999.9, 9999.9, 9999.9]));
    }
}
