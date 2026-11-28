using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Folders.Tests;

// A description's events say what ran and how it ended with counts,
// durations and failure kinds: never a photo's name or path, nor the text
// the model wrote.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class PhotoDescriptionEventsTests
{
    [Fact]
    public async Task AFolderRunIsAPairedStartAndEndWithCountsAndNothingOfThePhotos()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) =
            PhotoDescriptionQueueTests.Shoot(scratch, "secret_a.JPG", "secret_b.JPG");
        var describer = new PhotoDescriptionQueueTests.ScriptedDescriber();
        describer.Fail("secret_b.JPG", PhotoDescriptionFailure.PhotoUnreadable);
        using var queue = new PhotoDescriptionQueue(describer);
        using var listener = new TestEventListener("Deckle-Folders");

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await run.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        EventWrittenEventArgs start = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPhotoDescriptionRunStarted);
        EventWrittenEventArgs end = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPhotoDescriptionRunEnded);
        Assert.Equal(start.Payload?[0], end.Payload?[0]);
        Assert.Equal("Completed", end.Payload?[1]);
        Assert.Equal(1, end.Payload?[2]);
        Assert.Equal(2, end.Payload?[3]);
        Assert.Equal(1, end.Payload?[4]);
        Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPhotoDescriptionRunPartial);
        AssertNothingOfThePhotos(listener, scratch.Path);
    }

    [Fact]
    public async Task APhotoAskedAloneThatFailsSaysTheKindOfFailure()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, _) = PhotoDescriptionQueueTests.Shoot(scratch, "secret_a.JPG");
        var describer = new PhotoDescriptionQueueTests.ScriptedDescriber();
        describer.Fail("secret_a.JPG", PhotoDescriptionFailure.ServerUnreachable);
        using var queue = new PhotoDescriptionQueue(describer);
        using var listener = new TestEventListener("Deckle-Folders");

        await queue.DescribeOneAsync(store, "secret_a.JPG", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        EventWrittenEventArgs failed = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPhotoDescriptionFailed);
        Assert.Equal("ServerUnreachable", failed.Payload?[1]);
        EventWrittenEventArgs end = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPhotoDescriptionEnded);
        Assert.Equal("Failed", end.Payload?[1]);
        AssertNothingOfThePhotos(listener, scratch.Path);
    }

    private static void AssertNothingOfThePhotos(TestEventListener listener, string root)
    {
        foreach (EventWrittenEventArgs e in listener.Events)
        {
            foreach (object? value in e.Payload ?? [])
            {
                string text = value?.ToString() ?? "";
                Assert.DoesNotContain(root, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("description of", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
