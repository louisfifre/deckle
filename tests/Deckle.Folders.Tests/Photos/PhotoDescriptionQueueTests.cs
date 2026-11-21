using System.Collections.Concurrent;
using Xunit;

namespace Deckle.Folders.Tests;

// A folder run goes through the photos one after the other, skips those
// already described, counts "n of total", can be stopped and counts a photo
// that failed without stopping. A photo asked alone waits in the same line
// and goes before the run's next photo.
[Collection("folders provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoDescriptionQueueTests
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ARunSkipsWhatIsDescribedAndDescribesTheRest()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG", "c.JPG");
        store.Set("b.JPG", Description("B before"));
        var describer = new ScriptedDescriber();
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        Assert.Equal(1, run.Described);
        Assert.Equal(3, run.Total);
        await run.Completion.WaitAsync(Patience);

        Assert.Equal(PhotoDescriptionRunState.Completed, run.State);
        Assert.Equal(["a.JPG", "c.JPG"], describer.Asked.Select(Path.GetFileName));
        Assert.Equal(3, run.Described);
        Assert.Equal(1, run.Skipped);
        Assert.Equal("B before", store.DescriptionOf("b.JPG")?.Text);
        Assert.Equal("description of c.JPG", new PhotoDescriptionStore(scratch.Path, store.Folder).DescriptionOf("c.JPG")?.Text);
    }

    [Fact]
    public async Task APhotoThatFailsIsCountedAndTheRunGoesOn()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG", "c.JPG");
        var describer = new ScriptedDescriber();
        describer.Fail("a.JPG", PhotoDescriptionFailure.PhotoUnreadable);
        describer.Fail("b.JPG", PhotoDescriptionFailure.Other);
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await run.Completion.WaitAsync(Patience);

        Assert.Equal(PhotoDescriptionRunState.Completed, run.State);
        Assert.Equal(2, run.Failed);
        Assert.Equal(1, run.Described);
        Assert.True(store.IsDescribed("c.JPG"));
    }

    [Theory]
    [InlineData(PhotoDescriptionFailure.NoModelChosen)]
    [InlineData(PhotoDescriptionFailure.ServerUnreachable)]
    [InlineData(PhotoDescriptionFailure.ImagesNotSupported)]
    [InlineData(PhotoDescriptionFailure.EndpointNotLocal)]
    public async Task AFailureThatHoldsForEveryPhotoEndsTheRunAndSaysWhich(PhotoDescriptionFailure failure)
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG", "c.JPG");
        var describer = new ScriptedDescriber();
        describer.Fail("a.JPG", failure);
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await run.Completion.WaitAsync(Patience);

        Assert.Equal(PhotoDescriptionRunState.Failed, run.State);
        Assert.Equal(failure, run.SetupFailure);
        Assert.Single(describer.Asked);
        Assert.Null(queue.RunOf(store.Folder));
    }

    [Fact]
    public async Task StoppingARunStopsThePhotoUnderWayAndLeavesTheRest()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG", "c.JPG");
        var describer = new ScriptedDescriber();
        describer.Hold("b.JPG");
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await describer.Started("b.JPG").WaitAsync(Patience);
        run.Stop();
        await run.Completion.WaitAsync(Patience);

        Assert.Equal(PhotoDescriptionRunState.Stopped, run.State);
        Assert.True(store.IsDescribed("a.JPG"));
        Assert.False(store.IsDescribed("b.JPG"));
        Assert.False(store.IsDescribed("c.JPG"));
        Assert.DoesNotContain(describer.Asked, p => p.EndsWith("c.JPG"));
    }

    [Fact]
    public async Task APhotoAskedAloneGoesBeforeTheRunsNextPhoto()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG", "c.JPG");
        var describer = new ScriptedDescriber();
        describer.Hold("a.JPG");
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await describer.Started("a.JPG").WaitAsync(Patience);
        Task<PhotoDescriptionResult> alone = queue.DescribeOneAsync(store, "c.JPG", CancellationToken.None);
        describer.Release("a.JPG");

        PhotoDescriptionResult result = await alone.WaitAsync(Patience);
        await run.Completion.WaitAsync(Patience);

        Assert.True(result.Succeeded);
        Assert.Equal(["a.JPG", "c.JPG", "b.JPG"], describer.Asked.Select(Path.GetFileName));
        Assert.Equal(3, run.Described);
        Assert.Equal(1, run.Skipped);
    }

    [Fact]
    public async Task AFolderRunCanStartWhileOnePhotoIsBeingDescribed()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG");
        var describer = new ScriptedDescriber();
        describer.Hold("a.JPG");
        using var queue = new PhotoDescriptionQueue(describer);

        Task<PhotoDescriptionResult> alone = queue.DescribeOneAsync(store, "a.JPG", CancellationToken.None);
        await describer.Started("a.JPG").WaitAsync(Patience);
        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        Assert.True(run.IsRunning);
        describer.Release("a.JPG");

        await alone.WaitAsync(Patience);
        await run.Completion.WaitAsync(Patience);
        Assert.Equal(["a.JPG", "b.JPG"], describer.Asked.Select(Path.GetFileName));
        Assert.Equal(2, run.Described);
    }

    [Fact]
    public async Task APhotoAskedAloneCanBeStopped()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, _) = Shoot(scratch, "a.JPG");
        var describer = new ScriptedDescriber();
        describer.Hold("a.JPG");
        using var queue = new PhotoDescriptionQueue(describer);
        using var stop = new CancellationTokenSource();

        Task<PhotoDescriptionResult> alone = queue.DescribeOneAsync(store, "a.JPG", stop.Token);
        await describer.Started("a.JPG").WaitAsync(Patience);
        stop.Cancel();

        PhotoDescriptionResult result = await alone.WaitAsync(Patience);
        Assert.Equal(PhotoDescriptionFailure.Stopped, result.Failure);
        Assert.False(store.IsDescribed("a.JPG"));
    }

    [Fact]
    public async Task DescribingAgainReplacesTheDescription()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, _) = Shoot(scratch, "a.JPG");
        store.Set("a.JPG", Description("old"));
        using var queue = new PhotoDescriptionQueue(new ScriptedDescriber());

        PhotoDescriptionResult result = await queue.DescribeOneAsync(store, "a.JPG", CancellationToken.None).WaitAsync(Patience);

        Assert.True(result.Succeeded);
        Assert.Equal("description of a.JPG", store.DescriptionOf("a.JPG")?.Text);
    }

    [Fact]
    public async Task ADescriberThatThrowsCountsAsAFailedPhoto()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG", "b.JPG");
        var describer = new ScriptedDescriber();
        describer.Throw("a.JPG");
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun run = queue.StartFolder(store, photos);
        await run.Completion.WaitAsync(Patience);

        Assert.Equal(1, run.Failed);
        Assert.True(store.IsDescribed("b.JPG"));
    }

    [Fact]
    public void AFolderWithARunGoingGetsThatRunBack()
    {
        using var scratch = new ScratchFolder();
        (PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos) = Shoot(scratch, "a.JPG");
        var describer = new ScriptedDescriber();
        describer.Hold("a.JPG");
        using var queue = new PhotoDescriptionQueue(describer);

        PhotoDescriptionRun first = queue.StartFolder(store, photos);
        PhotoDescriptionRun second = queue.StartFolder(store, photos);

        Assert.Same(first, second);
        first.Stop();
    }

    internal static (PhotoDescriptionStore Store, IReadOnlyList<PhotoFile> Photos) Shoot(ScratchFolder scratch, params string[] names)
    {
        string shoot = scratch.Folder("Shoot");
        foreach (string name in names) scratch.File("x", "Shoot", name);
        return (new PhotoDescriptionStore(scratch.Path, shoot), PhotoFolder.List(shoot).Photos);
    }

    static PhotoDescription Description(string text) => new(text, "test-model", DateTimeOffset.Now);

    // Stands in for the model: answers "description of <name>" unless told
    // to fail, throw, or hold until released or cancelled.
    internal sealed class ScriptedDescriber : IPhotoDescriber
    {
        private readonly ConcurrentDictionary<string, PhotoDescriptionFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _throws = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _holds = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _asked = new();

        public IReadOnlyList<string> Asked => [.. _asked];

        public void Fail(string name, PhotoDescriptionFailure failure) => _failures[name] = failure;

        public void Throw(string name) => _throws[name] = true;

        public void Hold(string name) => _holds[name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string name) => _holds[name].TrySetResult();

        public Task Started(string name) => StartedSource(name).Task;

        private TaskCompletionSource StartedSource(string name) =>
            _started.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task<PhotoDescriptionResult> DescribeAsync(string photoPath, CancellationToken cancellationToken)
        {
            string name = Path.GetFileName(photoPath);
            _asked.Enqueue(photoPath);
            StartedSource(name).TrySetResult();
            if (_holds.TryGetValue(name, out TaskCompletionSource? hold))
            {
                try { await hold.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped); }
            }
            if (_throws.ContainsKey(name)) throw new InvalidOperationException("scripted");
            if (_failures.TryGetValue(name, out PhotoDescriptionFailure failure)) return PhotoDescriptionResult.Failed(failure);
            return PhotoDescriptionResult.Of(new PhotoDescription($"description of {name}", "test-model", DateTimeOffset.Now));
        }
    }
}
