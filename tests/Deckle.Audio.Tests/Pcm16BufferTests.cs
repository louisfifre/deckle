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
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }
}
