using System;
using System.IO;
using System.Threading.Tasks;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Behaviour of the cross-process write lock: it is exclusive. While one scope is
// held, a second acquisition cannot complete; releasing the first lets it through.
// The OS file lock (FileShare.None) also denies a second open within a process, so
// the exclusion is observable in-process without spawning a peer.
[Trait("Category", "unit")]
public class SpaceWriteLockTests : IDisposable
{
    private readonly string _dir;
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SpaceWriteLockTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "deckle-write-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SecondAcquireWaitsUntilFirstReleases()
    {
        var sut = new SpaceWriteLock(_dir);

        IDisposable first = await sut.AcquireAsync("update", "obj-1", Ct);

        Task<IDisposable> second = sut.AcquireAsync("update", "obj-1", Ct);

        // The second acquisition must not complete while the first is held.
        await Task.Delay(150, Ct);
        Assert.False(second.IsCompleted);

        first.Dispose();

        // Once released, it is granted within a couple of backoff cycles.
        IDisposable granted = await second.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.NotNull(granted);
        granted.Dispose();
    }

    [Fact]
    public async Task AcquireReleaseReacquireSucceeds()
    {
        var sut = new SpaceWriteLock(_dir);

        (await sut.AcquireAsync("update", "obj-1", Ct)).Dispose();

        IDisposable again = await sut.AcquireAsync("update", "obj-1", Ct);
        Assert.NotNull(again);
        again.Dispose();
    }
}
