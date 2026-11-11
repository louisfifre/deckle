using Xunit;

namespace Deckle.Atelier.Tests;

// The queue the shell calls run on: its own thread, in a single-threaded
// apartment, skipping what was cancelled before its turn.
[Trait("Category", "unit")]
public sealed class ShellWorkQueueTests
{
    [Fact]
    public async Task AJobRunsOnASingleThreadedApartmentThreadOfItsOwn()
    {
        using var queue = new ShellWorkQueue("test");
        int caller = Environment.CurrentManagedThreadId;

        (ApartmentState apartment, int thread) = await queue.RunAsync(
            () => (Thread.CurrentThread.GetApartmentState(), Environment.CurrentManagedThreadId),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.NotEqual(caller, thread);
    }

    [Fact]
    public async Task AJobCancelledBeforeItsTurnNeverRuns()
    {
        using var queue = new ShellWorkQueue("test");
        using var gate = new ManualResetEventSlim();
        Task<int> blocker = queue.RunAsync(() => { gate.Wait(); return 1; }, CancellationToken.None);

        using var leave = new CancellationTokenSource();
        bool ran = false;
        Task<bool> skipped = queue.RunAsync(() => ran = true, leave.Token);
        leave.Cancel();
        gate.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skipped);
        Assert.Equal(1, await blocker);
        // The queue runs in order: once a later job answers, the skipped one
        // had its turn.
        Assert.Equal(2, await queue.RunAsync(() => 2, CancellationToken.None));
        Assert.False(ran);
    }

    [Fact]
    public async Task AFailingJobReachesItsCallerAndTheQueueGoesOn()
    {
        using var queue = new ShellWorkQueue("test");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.RunAsync<int>(() => throw new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(3, await queue.RunAsync(() => 3, CancellationToken.None));
    }
}
