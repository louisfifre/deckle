using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class FileTranscriptionQueueTests
{
    [Fact]
    public void OneSelectionStartsAsOneImmutableBatch()
    {
        var queue = new FileTranscriptionQueue();
        FileTranscriptionBatch? started = null;

        int added = queue.Enqueue(["first.m4a", "second.wav", "third.mp3"]);
        bool didStart = queue.TryStartNext(batch =>
        {
            started = batch;
            return ToggleResult.Started;
        });

        Assert.Equal(3, added);
        Assert.True(didStart);
        Assert.Equal(["first.m4a", "second.wav", "third.mp3"], started!.AudioFilePaths);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void BusyConsumerLeavesTheHeadQueued()
    {
        var queue = new FileTranscriptionQueue();
        queue.Enqueue(["first.m4a", "second.wav"]);
        FileTranscriptionBatch? attemptedWhileBusy = null;

        bool startedWhileBusy = queue.TryStartNext(batch =>
        {
            attemptedWhileBusy = batch;
            return ToggleResult.IgnoredBusy;
        });

        FileTranscriptionBatch? started = null;
        bool startedAfterIdle = queue.TryStartNext(batch =>
        {
            started = batch;
            return ToggleResult.Started;
        });

        Assert.False(startedWhileBusy);
        Assert.Equal(["first.m4a", "second.wav"], attemptedWhileBusy!.AudioFilePaths);
        Assert.True(startedAfterIdle);
        Assert.Same(attemptedWhileBusy, started);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void BlankPathsAreNotQueued()
    {
        var queue = new FileTranscriptionQueue();

        int added = queue.Enqueue(["", "  ", "meeting.m4a"]);

        Assert.Equal(1, added);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void LaterSelectionCannotInterleaveTheActiveBatch()
    {
        var queue = new FileTranscriptionQueue();
        queue.Enqueue(["first.m4a", "second.wav"]);
        queue.Enqueue(["later.mp3"]);
        var started = new List<IReadOnlyList<string>>();

        queue.TryStartNext(batch =>
        {
            started.Add(batch.AudioFilePaths);
            return ToggleResult.Started;
        });
        queue.TryStartNext(batch =>
        {
            started.Add(batch.AudioFilePaths);
            return ToggleResult.Started;
        });

        Assert.Equal(2, started.Count);
        Assert.Equal(["first.m4a", "second.wav"], started[0]);
        Assert.Equal(["later.mp3"], started[1]);
    }
}
