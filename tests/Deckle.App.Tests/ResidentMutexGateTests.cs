using Xunit;

namespace Deckle.App.Tests;

// The resident gate's outcomes, driven through the mutex factory. Each test
// uses a mutex name of its own so nothing touches the running Deckle's gate.
[Trait("Category", "regression")]
public sealed class ResidentMutexGateTests
{
    private static string UniqueName() => @"Local\Deckle.Tests." + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(WaitHandleCannotBeOpenedException))]
    public void ACreationThatFailsIsRefusedLikeASecondLaunchInsteadOfThrowing(Type exceptionType)
    {
        var outcome = ResidentMutexGate.TryAcquire(
            () => throw (Exception)Activator.CreateInstance(exceptionType)!,
            out Mutex? mutex, out string? failure);

        Assert.Equal(ResidentMutexGate.Outcome.CreationFailed, outcome);
        Assert.Null(mutex);
        Assert.Equal(exceptionType.Name, failure);
    }

    [Fact]
    public void AFreeMutexIsAcquired()
    {
        string name = UniqueName();
        var outcome = ResidentMutexGate.TryAcquire(
            () => new Mutex(false, name), out Mutex? mutex, out string? failure);

        Assert.Equal(ResidentMutexGate.Outcome.Acquired, outcome);
        Assert.NotNull(mutex);
        Assert.Null(failure);
        mutex!.ReleaseMutex();
        mutex.Dispose();
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
            var outcome = ResidentMutexGate.TryAcquire(
                () => new Mutex(false, name), out Mutex? mutex, out _);

            Assert.Equal(ResidentMutexGate.Outcome.HeldByAnother, outcome);
            Assert.Null(mutex);
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
            var outcome = ResidentMutexGate.TryAcquire(
                () => new Mutex(false, name), out Mutex? mutex, out _);

            Assert.Equal(ResidentMutexGate.Outcome.AcquiredFromAbandoned, outcome);
            Assert.NotNull(mutex);
            mutex!.ReleaseMutex();
            mutex.Dispose();
        }
        finally { keep?.Dispose(); }
    }
}
