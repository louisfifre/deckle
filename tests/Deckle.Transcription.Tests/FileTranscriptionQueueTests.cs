using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class FileTranscriptionQueueTests
{
    [Fact]
    public void StartsFilesInSelectionOrder()
    {
        var queue = new FileTranscriptionQueue();
        var started = new List<string>();

        int added = queue.Enqueue(["first.m4a", "second.wav", "third.mp3"]);

        while (queue.TryStartNext(path =>
        {
            started.Add(path);
            return ToggleResult.Started;
        }))
        {
        }

        Assert.Equal(3, added);
        Assert.Equal(["first.m4a", "second.wav", "third.mp3"], started);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void BusyConsumerLeavesTheHeadQueued()
    {
        var queue = new FileTranscriptionQueue();
        queue.Enqueue(["first.m4a", "second.wav"]);
        string? attemptedWhileBusy = null;

        bool startedWhileBusy = queue.TryStartNext(path =>
        {
            attemptedWhileBusy = path;
            return ToggleResult.IgnoredBusy;
        });

        var started = new List<string>();
        bool startedAfterIdle = queue.TryStartNext(path =>
        {
            started.Add(path);
            return ToggleResult.Started;
        });

        Assert.False(startedWhileBusy);
        Assert.Equal("first.m4a", attemptedWhileBusy);
        Assert.True(startedAfterIdle);
        Assert.Equal(["first.m4a"], started);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void BlankPathsAreNotQueued()
    {
        var queue = new FileTranscriptionQueue();

        int added = queue.Enqueue(["", "  ", "meeting.m4a"]);

        Assert.Equal(1, added);
        Assert.Equal(1, queue.Count);
    }
}
