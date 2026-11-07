using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "integration")]
public sealed class ExecutableIconReaderTests
{
    [Fact]
    public void ASystemExecutableGivesAnIconOfTheAskedSize()
    {
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

        ExecutableIcon? icon = ExecutableIconReader.TryRead(notepad);

        Assert.NotNull(icon);
        Assert.Equal(ExecutableIconReader.IconSize, icon.Width);
        Assert.Equal(ExecutableIconReader.IconSize, icon.Height);
        Assert.Equal(icon.Width * icon.Height * 4, icon.Bgra.Length);
        Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Deckle-no-such-folder\game.exe")]
    public void ANeverReadableFileGivesNothingAndNoError(string? path)
    {
        Assert.Null(ExecutableIconReader.TryRead(path));
    }
}

[Trait("Category", "unit")]
public sealed class ExecutableIconCacheTests
{
    private static readonly ExecutableIcon Icon = new(1, 1, [1, 2, 3, 255]);

    [Fact]
    public async Task AnIconIsReadOnceWhateverTheCase()
    {
        int reads = 0;
        var cache = new ExecutableIconCache(_ => { Interlocked.Increment(ref reads); return Icon; });

        await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None);
        ExecutableIcon? again = await cache.GetAsync(@"C:\A\GAME.EXE", CancellationToken.None);

        Assert.Same(Icon, again);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AMissingIconIsAskedAgainOnTheNextVisit()
    {
        int reads = 0;
        var cache = new ExecutableIconCache(_ => { Interlocked.Increment(ref reads); return null; });

        Assert.Null(await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None));
        Assert.Null(await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None));

        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task AFailingReadGivesNothingAndIsNotRepeated()
    {
        int reads = 0;
        var cache = new ExecutableIconCache(_ => { Interlocked.Increment(ref reads); throw new InvalidOperationException(); });

        Assert.Null(await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None));
        Assert.Null(await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None));

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task CancellingStopsTheWaitButNotTheSharedRead()
    {
        var gate = new ManualResetEventSlim();
        var cache = new ExecutableIconCache(_ => { gate.Wait(); return Icon; });
        using var cts = new CancellationTokenSource();

        Task<ExecutableIcon?> waiting = cache.GetAsync(@"C:\a\game.exe", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        gate.Set();
        Assert.Same(Icon, await cache.GetAsync(@"C:\a\game.exe", CancellationToken.None));
    }
}
