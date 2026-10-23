using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class DeckleLlmSourceTests : IDisposable
{
    public DeckleLlmSourceTests()
        => OperationalLogAdmission.Configure(static _ => false);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void DisabledTranscriptionAdmissionRejectsProbeDetail()
    {
        using var listener = new TestEventListener("Deckle-Llm");

        DeckleLlmSource.Log.PsProbeRejectedDetail(503);
        DeckleLlmSource.Log.PsProbeEmpty(1_200);
        DeckleLlmSource.Log.PsProbeBusyDetail("model", 3.5, 0, 60_000, 900);
        DeckleLlmSource.Log.PsProbeErrorDetail("HttpRequestException", "offline");

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void EnabledTranscriptionAdmissionEmitsOnlyProbeDetail()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);
        using var listener = new TestEventListener("Deckle-Llm");

        DeckleLlmSource.Log.PsProbeRejectedDetail(503);
        DeckleLlmSource.Log.PsProbeEmpty(1_200);
        DeckleLlmSource.Log.PsProbeBusyDetail("model", 3.5, 0, 60_000, 900);
        DeckleLlmSource.Log.PsProbeErrorDetail("HttpRequestException", "offline");

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleLlmSource.EvtPsProbeRejectedDetail, e.EventId),
            e => Assert.Equal(DeckleLlmSource.EvtPsProbeEmpty, e.EventId),
            e => Assert.Equal(DeckleLlmSource.EvtPsProbeBusyDetail, e.EventId),
            e => Assert.Equal(DeckleLlmSource.EvtPsProbeErrorDetail, e.EventId));
        Assert.All(listener.Events, e => Assert.Equal("Verbose", e.Level.ToString()));
    }
}
