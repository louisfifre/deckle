using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the take's accumulator: a retaining buffer returns every sample in
// order, a non-retaining one keeps only the count.
public class FloatTakeBufferTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void ToArray_WhenRetaining_ReturnsEverySampleInOrder()
    {
        var buffer = new FloatTakeBuffer(initialCapacity: 2);

        buffer.Append([1f, 2f, 3f]);
        buffer.Append([]);
        buffer.Append([4f, 5f, 6f, 7f, 8f]);

        Assert.Equal(8, buffer.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f], buffer.ToArray());
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ToArray_WhenNotRetaining_IsEmptyButCountKeepsTotalling()
    {
        var buffer = new FloatTakeBuffer(initialCapacity: 2, retain: false);

        buffer.Append([1f, 2f, 3f]);
        buffer.Append([4f, 5f, 6f, 7f, 8f]);

        Assert.Equal(8, buffer.Count);
        Assert.Empty(buffer.ToArray());
    }
}
