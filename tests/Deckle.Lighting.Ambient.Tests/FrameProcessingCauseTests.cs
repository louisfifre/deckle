using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Deckle.Vision;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The frame-processing incident is what a reader finds when the lamps freeze on a
// failing capture. The capture and the sampler keep the cause of the last dropped
// frame; the line that opens the incident has to carry it, or it says a frame
// failed and never why.
[Trait("Category", "observability")]
public sealed class FrameProcessingCauseTests
{
    private sealed class Host : IAmbientEngineHost
    {
        public AmbientSettings Ambient { get; } = new();
    }

    public FrameProcessingCauseTests()
        => OperationalLogAdmission.Configure(
            activity => activity == OperationalLogActivity.Ambient);

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    // Enough failures, a quarter of a second apart, to cross the one-second
    // threshold that opens the incident.
    private static void FailUntilTheIncidentOpens(AmbientEngine engine, string reason, FrameFailure? cause)
    {
        long quarterSecond = Stopwatch.Frequency / 4;
        for (int i = 1; i <= 6; i++)
            engine.RecordFrameProcessingFailure(reason, cause, i * quarterSecond);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void TheIncidentCarriesTheCauseOfTheFailingFrames()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());
        var cause = new FrameFailure("texture_query", unchecked((int)0x887A0005), "COMException", "device busy");

        FailUntilTheIncidentOpens(engine, "capture_failed", cause);

        var opened = Assert.Single(
            listener.Events, e => e.EventId == DeckleAmbientSource.EvtFrameProcessingIncidentOpened);
        Assert.Equal("capture_failed", Field(opened, "reason"));
        Assert.Equal("texture_query", Field(opened, "cause_stage"));
        Assert.Equal(unchecked((int)0x887A0005), Convert.ToInt32(opened.Payload![opened.PayloadNames!.IndexOf("cause_hr")]));
        Assert.Equal("COMException", Field(opened, "cause_ex_type"));
        Assert.Equal("device busy", Field(opened, "cause_ex_message"));
    }

    [Fact]
    public void AFailureThatKeptNoCauseSaysSo()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());

        FailUntilTheIncidentOpens(engine, "sampler_failed", cause: null);

        var opened = Assert.Single(
            listener.Events, e => e.EventId == DeckleAmbientSource.EvtFrameProcessingIncidentOpened);
        Assert.Equal("unknown", Field(opened, "cause_stage"));
    }

    [Fact]
    public void TheEventThatStopsTheRunCarriesTheCauseToo()
    {
        IReadOnlyList<string> payloads = ProviderManifest.PayloadNamesOf(typeof(DeckleAmbientSource));

        Assert.Contains("FrameProcessingFailed.cause_stage", payloads);
        Assert.Contains("FrameProcessingFailed.cause_hr", payloads);
        Assert.Contains("FrameProcessingFailed.cause_ex_type", payloads);
    }
}