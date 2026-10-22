using System.Runtime.InteropServices;
using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

public sealed class Pcm16BufferTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void AppendGrowsAndReturnsTheNewlyWrittenSamples()
    {
        byte[] first = [1, 2];
        byte[] second = [3, 4, 5];
        nint source = Marshal.AllocHGlobal(second.Length);

        try
        {
            var buffer = new Pcm16Buffer(initialCapacity: 1);

            Marshal.Copy(first, 0, source, first.Length);
            Assert.Equal(first, buffer.Append(source, first.Length).ToArray());
            Marshal.Copy(second, 0, source, second.Length);
            Assert.Equal(second, buffer.Append(source, second.Length).ToArray());

            Assert.Equal([1, 2, 3, 4, 5], buffer.ToArray());
            Assert.Equal(5, buffer.Count);
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }

    // A live meter reads each buffer as it arrives and never the take as a
    // whole, so a buffer that keeps nothing still has to hand over the bytes
    // just written and count every byte, because the duration cap and the
    // take's length read the count.
    [Fact]
    [Trait("Category", "unit")]
    public void ANonRetainingBufferHandsOverEachAppendCountsAllAndKeepsNothing()
    {
        byte[] first = [1, 2, 3, 4];
        byte[] second = [5, 6];
        nint source = Marshal.AllocHGlobal(first.Length);

        try
        {
            var buffer = new Pcm16Buffer(initialCapacity: 1, retain: false);

            Marshal.Copy(first, 0, source, first.Length);
            Assert.Equal(first, buffer.Append(source, first.Length).ToArray());
            Marshal.Copy(second, 0, source, second.Length);
            Assert.Equal(second, buffer.Append(source, second.Length).ToArray());

            Assert.Equal(first.Length + second.Length, buffer.Count);
            Assert.Empty(buffer.ToArray());
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }

    // The point of not retaining: a take of any length reuses the same bytes.
    [Fact]
    [Trait("Category", "unit")]
    public void ANonRetainingBufferDoesNotGrowWithTheTake()
    {
        const int bufferBytes = 1600;
        nint source = Marshal.AllocHGlobal(bufferBytes);

        try
        {
            var buffer = new Pcm16Buffer(initialCapacity: bufferBytes, retain: false);
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 10_000; i++)
                buffer.Append(source, bufferBytes);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10_000L * bufferBytes, buffer.Count);
            // A retaining buffer would have doubled its way past 16 MB.
            Assert.True(allocated < 1_000_000, $"allocated {allocated} bytes while appending");
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }
}
