using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

[Collection(ParakeetBackendCollection.Name)]
[Trait("Category", "observability")]
public sealed class ParakeetBackendObservabilityTests
{
    private const string Provider = "Deckle-Parakeet";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALoadLogsItsStartAndWhereTheEncoderRuns()
    {
        using var listener = new TestEventListener(Provider);
        using var backend = new ParakeetBackend(new FakeHost(), FakeLoader.Succeeding());

        await backend.LoadModelAsync(Ct);

        Assert.Equal(
            [DeckleParakeetSource.EvtModelLoadStarting, DeckleParakeetSource.EvtModelLoadStartingDetail,
             DeckleParakeetSource.EvtModelOnGpu, DeckleParakeetSource.EvtModelLoadComplete],
            listener.Events.Select(e => e.EventId));
    }

    [Fact]
    public async Task AGpuFallbackIsOneWarningPerIncidentClosedByTheNextHonouredLoad()
    {
        using var listener = new TestEventListener(Provider);
        string? fallback = "OnnxDeviceUnavailableException";
        var loader = new FakeLoader(() => FakeLoader.Succeeding(
            accelerator: fallback is null ? ParakeetAccelerators.DirectMl : ParakeetAccelerators.Cpu,
            fallbackType: fallback).Load(new ParakeetModelFiles(""), default, true));
        using var backend = new ParakeetBackend(new FakeHost(), loader);

        await backend.LoadModelAsync(Ct);
        backend.UnloadModel();
        await backend.LoadModelAsync(Ct);   // still falling back: no second warning
        backend.UnloadModel();
        fallback = null;
        await backend.LoadModelAsync(Ct);   // the device answers again: the incident closes

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtModelGpuUnavailable);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtModelOnGpu);
    }

    [Fact]
    public async Task ALoadFailureIsMirroredOncePerIncident()
    {
        using var listener = new TestEventListener(Provider);
        var loader = new FakeLoader(() => ParakeetLoadOutcome.Failed("file_not_found", @"C:\m\vocab.txt"));
        using var backend = new ParakeetBackend(new FakeHost(), loader);

        await backend.LoadModelAsync(Ct);
        await backend.LoadModelAsync(Ct);

        Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtModelLoadFailed);
        Assert.Equal(2, listener.Events.Count(e => e.EventId == DeckleParakeetSource.EvtModelLoadFailedDetail));
    }

    [Fact]
    public async Task ACallLeavesOneSummaryAndNoTranscriptText()
    {
        using var listener = new TestEventListener(Provider);
        using var backend = new ParakeetBackend(new FakeHost(), FakeLoader.Succeeding());
        await backend.LoadModelAsync(Ct);

        await backend.TranscribeAsync(new float[16_000], null, Ct);

        EventWrittenEventArgs summary = Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtTranscribeSummary);
        Assert.True(ObservationTags.IsSummary(summary.Tags));
        Assert.DoesNotContain(
            listener.Events.SelectMany(e => e.Payload ?? []).OfType<string>(),
            text => text.Contains("hello", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ASinkThatKeepsThrowingIsOneErrorClosedByARecovery()
    {
        using var listener = new TestEventListener(Provider);
        using var backend = new ParakeetBackend(new FakeHost(), FakeLoader.Succeeding());
        await backend.LoadModelAsync(Ct);

        await backend.TranscribeAsync(new float[16_000], _ => throw new InvalidOperationException("a"), Ct);
        await backend.TranscribeAsync(new float[16_000], _ => throw new InvalidOperationException("b"), Ct);
        await backend.TranscribeAsync(new float[16_000], _ => { }, Ct);

        Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtSegmentCallbackFailed);
        Assert.Single(listener.Events, e => e.EventId == DeckleParakeetSource.EvtSegmentCallbackRecovered);
    }

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(DeckleParakeetSource)], new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RecoveryClosersTravelInTheLog()
    {
        Assert.Empty(ProviderManifest.ClosersOutsideTheLog(typeof(DeckleParakeetSource)));
    }
}
