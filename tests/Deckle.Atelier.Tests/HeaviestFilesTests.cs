using Xunit;

namespace Deckle.Atelier.Tests;

// The few heaviest files kept from a stream of many.
[Trait("Category", "unit")]
public sealed class HeaviestFilesTests
{
    private static HeavyFile File(long size, string name = "") =>
        new(name.Length > 0 ? name : $"f{size}", $@"C:\x\{(name.Length > 0 ? name : $"f{size}")}", size);

    [Fact]
    public void KeepsOnlyTheHeaviestHeaviestFirst()
    {
        var heap = new HeaviestFiles(3);
        foreach (long size in new long[] { 5, 90, 1, 40, 70, 3, 60 }) heap.Offer(File(size));

        Assert.Equal([90L, 70L, 60L], heap.Descending().Select(f => f.Size));
    }

    [Fact]
    public void AFileNoHeavierThanTheLightestKeptIsRefused()
    {
        var heap = new HeaviestFiles(2);
        heap.Offer(File(10, "first"));
        heap.Offer(File(20));

        Assert.False(heap.Accepts(10));
        heap.Offer(File(10, "second"));
        Assert.Equal(["f20", "first"], heap.Descending().Select(f => f.Name));
    }

    [Fact]
    public void MergingTwoKeepsTheHeaviestOfBoth()
    {
        var a = new HeaviestFiles(3);
        var b = new HeaviestFiles(3);
        foreach (long size in new long[] { 1, 8, 6 }) a.Offer(File(size));
        foreach (long size in new long[] { 7, 2, 9 }) b.Offer(File(size));

        a.OfferAll(b);

        Assert.Equal([9L, 8L, 7L], a.Descending().Select(f => f.Size));
    }

    [Fact]
    public void ACapacityOfZeroKeepsNothing()
    {
        var heap = new HeaviestFiles(0);
        heap.Offer(File(1));

        Assert.Empty(heap.Descending());
    }
}
