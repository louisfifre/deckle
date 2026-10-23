using System.Diagnostics.Tracing;
using Deckle.Lighting.Ambient;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "observability")]
[Collection(AmbientObservabilityCollection.Name)]
public sealed class AmbientPresetSelectionLogTests
{
    // The family reports an unknown stored id through a callback; the service
    // hands it the Ambient provider. The line must be a Warning that names both
    // ids, since the tuning on screen is then not the one that was chosen.
    [Fact]
    public void AReplacedPresetSelectionIsAWarningNamingTheAskedAndObtainedIds()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.PresetSelectionReplaced("retired-preset", "custom");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAmbientSource.EvtPresetSelectionReplaced);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("retired-preset", ev.Payload![0]);
        Assert.Equal("custom", ev.Payload![1]);
    }
}