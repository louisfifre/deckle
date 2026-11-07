using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The preview scan is lasting work of its own: its start and its end travel
// in the log with one work_id, and an end is written only for a scan that
// started. The capture itself needs a display, so the scan's start is not
// exercised here.
[Trait("Category", "observability")]
public sealed class AmbientPreviewScanLogTests
{
    private sealed class Host : IAmbientEngineHost
    {
        public AmbientSettings Ambient { get; } = new();
    }

    [Fact]
    public void TheScanMilestonesTravelWithTheirWorkId()
    {
        string[] milestones =
        [
            nameof(DeckleAmbientSource.PreviewScanStarting),
            nameof(DeckleAmbientSource.PreviewScanStartFailed),
            nameof(DeckleAmbientSource.PreviewScanStopped),
        ];

        foreach (var declared in ProviderManifest.EventsOf(typeof(DeckleAmbientSource))
                     .Where(e => milestones.Contains(e.Name) || e.Name == nameof(DeckleAmbientSource.PreviewScanSkipped)))
        {
            Assert.True(ObservationTags.IsMilestone(declared.Tags), $"{declared.Name} must travel in the log.");
        }

        IReadOnlyList<string> payloads = ProviderManifest.PayloadNamesOf(typeof(DeckleAmbientSource));
        foreach (string milestone in milestones)
            Assert.Contains($"{milestone}.{WorkId.FieldName}", payloads);
    }

    [Fact]
    public async Task StoppingWithNoScanWritesNoEnd()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());

        await engine.StopPreviewScanAsync();

        Assert.False(engine.IsPreviewScanning);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleAmbientSource.EvtPreviewScanStopped);
    }
}
