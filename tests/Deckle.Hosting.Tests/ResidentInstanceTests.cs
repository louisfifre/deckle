using Xunit;

namespace Deckle.Hosting.Tests;

// The resident gate's outcomes for every app of the suite, driven through the mutex factory. Each test
// uses a mutex name of its own so nothing touches the running Deckle's gate.
[Trait("Category", "regression")]
public sealed class ResidentInstanceTests
{
    private static string UniqueName() => @"Local\Deckle.Tests." + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(WaitHandleCannotBeOpenedException))]
    public void ACreationThatFailsIsRefusedLikeASecondLaunchInsteadOfThrowing(Type exceptionType)
    {
        var instance = ResidentInstance.TryAcquire(
            () => throw (Exception)Activator.CreateInstance(exceptionType)!,
            out string? failure);

        Assert.Null(instance);
        Assert.Equal(exceptionType.Name, failure);
    }

    [Fact]
    public void AFreeMutexIsAcquired()
    {
        string name = UniqueName();
        var instance = ResidentInstance.TryAcquire(
            () => new Mutex(false, name), out string? failure);

        Assert.NotNull(instance);
        Assert.False(instance!.Abandoned);
        Assert.Null(failure);
        Assert.Null(instance.Release());
    }

    [Fact]
    public void AMutexHeldByAnotherThreadIsReportedHeld()
    {
        string name = UniqueName();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var m = new Mutex(true, name);
            held.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        owner.Start();
        held.Wait(TestContext.Current.CancellationToken);
        try
        {
            var instance = ResidentInstance.TryAcquire(
                () => new Mutex(false, name), out _);

            Assert.Null(instance);
        }
        finally
        {
            release.Set();
            owner.Join();
        }
    }

    [Fact]
    public void AnAbandonedMutexIsTakenOverAndFlagged()
    {
        string name = UniqueName();
        using var created = new ManualResetEventSlim();
        Mutex? keep = null;
        var dying = new Thread(() =>
        {
            keep = new Mutex(true, name);
            created.Set();
            // Ends without releasing: the mutex becomes abandoned.
        });
        dying.Start();
        created.Wait(TestContext.Current.CancellationToken);
        dying.Join();
        try
        {
            var instance = ResidentInstance.TryAcquire(
                () => new Mutex(false, name), out _);

            Assert.NotNull(instance);
            Assert.True(instance!.Abandoned);
            Assert.Null(instance.Release());
        }
        finally { keep?.Dispose(); }
    }
}
