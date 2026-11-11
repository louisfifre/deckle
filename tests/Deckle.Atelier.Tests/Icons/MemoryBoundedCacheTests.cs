using Xunit;

namespace Deckle.Atelier.Tests;

// Pictures stay within their budget, the least recently used leaving first.
[Trait("Category", "unit")]
public sealed class MemoryBoundedCacheTests
{
    [Fact]
    public void AnAddedPictureIsFoundAgain()
    {
        var cache = new MemoryBoundedCache<string, string?>(100);
        cache.Add("md", "icon", 10);

        Assert.True(cache.TryGet("md", out string? value));
        Assert.Equal("icon", value);
        Assert.Equal(10, cache.UsedBytes);
    }

    [Fact]
    public void ARememberedAbsenceIsFoundAsNull()
    {
        var cache = new MemoryBoundedCache<string, string?>(100);
        cache.Add("unknown", null, 1);

        Assert.True(cache.TryGet("unknown", out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void PassingTheBudgetEvictsTheLeastRecentlyUsed()
    {
        var cache = new MemoryBoundedCache<string, string>(30);
        cache.Add("a", "A", 10);
        cache.Add("b", "B", 10);
        cache.Add("c", "C", 10);
        Assert.True(cache.TryGet("a", out _)); // a is now the most recent

        cache.Add("d", "D", 10);

        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
        Assert.Equal(30, cache.UsedBytes);
    }

    [Fact]
    public void APictureLargerThanTheBudgetIsNotKept()
    {
        var cache = new MemoryBoundedCache<string, string>(30);
        cache.Add("a", "A", 10);
        cache.Add("huge", "H", 31);

        Assert.False(cache.TryGet("huge", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.Equal(10, cache.UsedBytes);
    }

    [Fact]
    public void ReplacingAKeyCountsItsBytesOnce()
    {
        var cache = new MemoryBoundedCache<string, string>(100);
        cache.Add("a", "A", 10);
        cache.Add("a", "A2", 25);

        Assert.Equal(1, cache.Count);
        Assert.Equal(25, cache.UsedBytes);
        Assert.True(cache.TryGet("a", out string? value));
        Assert.Equal("A2", value);
    }
}
