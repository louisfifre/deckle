using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vad.Tests;

// A failed model download used to be final for the session: the service tried
// once, reported it, and every later take ran untrimmed until the next start. It
// now retries on a ladder of waits, in the background, and the failures are one
// incident. The download and the wait are replaced by fakes: the first reaches the
// network, the second takes minutes.
[Trait("Category", "observability")]
[Collection(VadObservabilityCollection.Name)]
public sealed class VadServiceDownloadRetryTests : IDisposable
{
    private readonly string _modelsDirectory =
        Path.Combine(Path.GetTempPath(), "deckle-vad-retry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_modelsDirectory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AFailedDownloadIsRetriedAndTheFailuresAreOneIncidentThatASuccessCloses()
    {
        var downloads = new FakeDownloads(failures: 2);
        var waits = new List<TimeSpan>();
        using var service = NewService(downloads, waits);
        using var listener = new TestEventListener("Deckle-Vad");

        Assert.Null(service.EnsureReady());
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, downloads.Calls);
        Assert.Equal(VadService.DownloadRetryDelays[..2], waits);

        // One Warning for the failed download, however many attempts failed.
        Assert.Single(listener.Events, e => IsUnavailable(e, "download_failed"));
        Assert.DoesNotContain(listener.Events, e => IsUnavailable(e, "download_gave_up"));
        // Each failed attempt is a Verbose detail of that incident.
        var attempts = listener.Events
            .Where(e => e.EventId == DeckleVadSource.EvtSpeechTrimVadDownloadAttemptFailedDetail)
            .ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, e => Assert.Equal(EventLevel.Verbose, e.Level));

        // The success closes the incident, without waiting for a take to say so.
        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimRecovered);
        Assert.Equal("download_failed", recovered.Payload![0]);
        Assert.Single(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimVadDownloadComplete);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ADownloadThatKeepsFailingGivesUpOnceAndSaysTheNextStartRetries()
    {
        int retries = VadService.DownloadRetryDelays.Length;
        var downloads = new FakeDownloads(failures: int.MaxValue);
        var waits = new List<TimeSpan>();
        using var service = NewService(downloads, waits);
        using var listener = new TestEventListener("Deckle-Vad");

        Assert.Null(service.EnsureReady());
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10));

        // The first try, then one try per wait of the ladder, and no more.
        Assert.Equal(1 + retries, downloads.Calls);
        Assert.Equal(VadService.DownloadRetryDelays, waits);

        Assert.Single(listener.Events, e => IsUnavailable(e, "download_failed"));
        EventWrittenEventArgs gaveUp = Assert.Single(
            listener.Events, e => IsUnavailable(e, "download_gave_up"));
        Assert.Equal(EventLevel.Warning, gaveUp.Level);
        Assert.Contains("next start", (string)gaveUp.Payload![2]!);
        Assert.Equal(1 + retries, listener.Events.Count(
            e => e.EventId == DeckleVadSource.EvtSpeechTrimVadDownloadAttemptFailedDetail));
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimRecovered);

        // A later take does not start another download: the next start is the retry.
        Assert.Null(service.EnsureReady());
        Assert.Equal(1 + retries, downloads.Calls);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task TheCallerIsNeverBlockedByTheWaitBetweenTwoAttempts()
    {
        var downloads = new FakeDownloads(failures: int.MaxValue);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new VadService(
            () => _modelsDirectory, downloads.ProvisionAsync, (_, _) => release.Task);

        Assert.Null(service.EnsureReady());   // returns although the retry is waiting
        Assert.Equal(1, downloads.Calls);
        Assert.False(service.DownloadEnded.IsCompleted);

        release.SetResult();
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10));
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
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var service = new VadService(
            () => _modelsDirectory,
            _ => Interlocked.Increment(ref calls) == 1
                ? firstDownload.Task
                : Task.FromResult(ProvisioningResult.Ok(1, null)),
            (_, _) =>
            {
                waiting.TrySetResult();
                return release.Task;
            });

        Assert.Null(service.EnsureReady());
        Assert.False(UserIncidentRegistry.Instance.IsOpen(UserIncidents.VadModel));

        firstDownload.SetResult(ProvisioningResult.Fail("network unreachable"));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        UserIncident incident = Assert.Single(
            UserIncidentRegistry.Instance.Snapshot(), i => i.Id == UserIncidents.VadModel);
        Assert.Equal("download_failed", incident.Reason);

        release.SetResult();
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(UserIncidentRegistry.Instance.IsOpen(UserIncidents.VadModel));
    }

    [Fact]
    public async Task DisposeStopsTheRetriesThatAreWaiting()
    {
        var downloads = new FakeDownloads(failures: int.MaxValue);
        var service = new VadService(
            () => _modelsDirectory,
            downloads.ProvisionAsync,
            static (wait, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var listener = new TestEventListener("Deckle-Vad");

        Assert.Null(service.EnsureReady());
        Assert.False(service.DownloadEnded.IsCompleted);

        service.Dispose();
        await service.DownloadEnded.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, downloads.Calls);
        Assert.DoesNotContain(listener.Events, e => IsUnavailable(e, "download_gave_up"));
    }

    [Fact]
    public void TheAttemptDetailsStayVerboseAndOutOfTheLog()
    {
        // The attempts are the incident's detail: Verbose and out of the log,
        // so a flaky network cannot fill it.
        var declared = ProviderManifest.EventsOf(typeof(DeckleVadSource))
            .Single(e => e.Id == DeckleVadSource.EvtSpeechTrimVadDownloadAttemptFailedDetail);
        Assert.Equal(EventLevel.Verbose, declared.Level);
        Assert.False(LoggedPayloads.Travels(declared));
    }

    private VadService NewService(FakeDownloads downloads, List<TimeSpan> waits)
        => new(
            () => _modelsDirectory,
            downloads.ProvisionAsync,
            (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            });

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
