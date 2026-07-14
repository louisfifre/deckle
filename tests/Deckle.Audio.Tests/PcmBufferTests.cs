using System.Runtime.InteropServices;
using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

public class PcmBufferTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void Append_GrowsAndPreservesSamples()
    {
        float[] first = [0.25f, -0.5f];
        float[] second = [0.75f, 1f, -1f];
        nint source = Marshal.AllocHGlobal(second.Length * sizeof(float));

        try
        {
            var buffer = new PcmBuffer(initialCapacity: 1);

            Marshal.Copy(first, 0, source, first.Length);
            buffer.Append(source, first.Length);
            Marshal.Copy(second, 0, source, second.Length);
            buffer.Append(source, second.Length);

            Assert.Equal([0.25f, -0.5f, 0.75f, 1f, -1f], buffer.WrittenMemory.ToArray());
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }
}
