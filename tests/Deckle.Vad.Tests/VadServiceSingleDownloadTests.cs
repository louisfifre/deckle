using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vad.Tests;

// The model is downloaded once per session, in the background. A failure is one
// incident, shown in the tray, and the next start is the retry. The download is
// replaced by a fake: the real one reaches the network.
[Trait("Category", "observability")]
[Collection(VadObservabilityCollection.Name)]
public sealed class VadServiceSingleDownloadTests : IDisposable
{
    private readonly string _modelsDirectory =
        Path.Combine(Path.GetTempPath(), "deckle-vad-download-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_modelsDirectory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AFailedDownloadIsTriedOnceAndOpensOneIncident()
    {
        var downloads = new FakeDownloads(failures: int.MaxValue);
        using var service = new VadService(() => _modelsDirectory, downloads.ProvisionAsync);
        using var listener = new TestEventListener("Deckle-Vad");

        Assert.Null(service.EnsureReady());
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, downloads.Calls);
        Assert.Single(listener.Events, e => IsUnavailable(e, "download_failed"));
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimRecovered);

        // A later take does not start another download: the next start is the retry.
        Assert.Null(service.EnsureReady());
        Assert.Equal(1, downloads.Calls);
    }

    [Fact]
    public async Task ASuccessfulDownloadIsReportedAndOpensNoIncident()
    {
        var downloads = new FakeDownloads(failures: 0);
        using var service = new VadService(() => _modelsDirectory, downloads.ProvisionAsync);
        using var listener = new TestEventListener("Deckle-Vad");

        Assert.Null(service.EnsureReady());
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(1, downloads.Calls);
        Assert.Single(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimVadDownloadComplete);
        Assert.DoesNotContain(listener.Events, e => IsUnavailable(e, "download_failed"));
    }

    // The tray shows the download failing, not the download running: a missing
    // model the service is already fetching is no problem for the person yet.
    // The collection runs alone, so the process-wide registry sees only this test.
    [Fact]
    public async Task AFailedDownloadIsAUserIncidentButTheDownloadUnderWayIsNot()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.VadModel);
        var firstDownload = new TaskCompletionSource<ProvisioningResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new VadService(() => _modelsDirectory, _ => firstDownload.Task);

        Assert.Null(service.EnsureReady());
        Assert.False(UserIncidentRegistry.Instance.IsOpen(UserIncidents.VadModel));

        firstDownload.SetResult(ProvisioningResult.Fail("network unreachable"));
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        UserIncident incident = Assert.Single(
            UserIncidentRegistry.Instance.Snapshot(), i => i.Id == UserIncidents.VadModel);
        Assert.Equal("download_failed", incident.Reason);
    }

    private static bool IsUnavailable(EventWrittenEventArgs e, string reason)
        => e.EventId == DeckleVadSource.EvtSpeechTrimUnavailable && (string?)e.Payload![0] == reason;

    // Fails the first `failures` downloads with the downloader's own shape of
    // failure (text, not an exception), then succeeds.
    private sealed class FakeDownloads(int failures)
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ProvisioningResult> ProvisionAsync(string modelDirectory)
        {
            int call = Interlocked.Increment(ref _calls);
            return Task.FromResult(call <= failures
                ? ProvisioningResult.Fail("network unreachable")
                : ProvisioningResult.Ok(1, null));
        }
    }
}
